using System;
using System.IO;
using System.Configuration;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SchedulerExecutorApplication.GraphQl;
using StrawberryShake;
using System.Text;
using System.Text.Json;

namespace SchedulerExecutorApplication
{
    class Program
    {
        private static bool _exitSystem = false;
        private static ISchedulerServer _schedulerServer;
        private static Configuration config;

        private static string DynamicAccountId = null;
        private static string DynamicAccountLogin = null;
        private static string DynamicExecutorId = null;
        private static string DynamicExecutorName = null;

        public static bool EnvironmentVariablesConfig => Environment.GetEnvironmentVariable("ENVIRONMENT_VARIABLE_CONFIG").ToLower() == "true";
        public static string GraphQlServerBaseAddress => EnvironmentVariablesConfig ? Environment.GetEnvironmentVariable("GRAPHQL_SERVER_BASE_ADDRESS") : config.AppSettings.Settings["graphQlServerBaseAddress"].Value;
        public static string GraphQlServerUri => EnvironmentVariablesConfig ? Environment.GetEnvironmentVariable("GRAPHQL_SERVER_URI") : config.AppSettings.Settings["graphQlServerUri"].Value;
        public static string ExecutorId => DynamicExecutorId != null ? DynamicExecutorId : EnvironmentVariablesConfig ? Environment.GetEnvironmentVariable("EXECUTOR_ID") : config.AppSettings.Settings["executorId"].Value;
        public static string ExecutorName => DynamicExecutorName != null ? DynamicExecutorName : EnvironmentVariablesConfig ? Environment.GetEnvironmentVariable("EXECUTOR_NAME") : config.AppSettings.Settings["executorName"].Value;
        public static string AccountId => DynamicAccountId != null ? DynamicAccountId : EnvironmentVariablesConfig ? Environment.GetEnvironmentVariable("ACCOUNT_ID") : config.AppSettings.Settings["accountId"].Value;
        public static string AccountLogin => DynamicAccountLogin != null ? DynamicAccountLogin : EnvironmentVariablesConfig ? Environment.GetEnvironmentVariable("ACCOUNT_LOGIN") : config.AppSettings.Settings["accountLogin"].Value;

        /*#region Trap application termination
        [DllImport("Kernel32")]
        private static extern bool SetConsoleCtrlHandler(EventHandler handler, bool add);

        private delegate bool EventHandler(CtrlType sig);
        static EventHandler _handler;

        enum CtrlType {
            CTRL_C_EVENT = 0,
            CTRL_BREAK_EVENT = 1,
            CTRL_CLOSE_EVENT = 2,
            CTRL_LOGOFF_EVENT = 5,
            CTRL_SHUTDOWN_EVENT = 6
        }

        private static bool Handler(CtrlType sig) {
            Console.WriteLine("Exiting system due to external CTRL-C, or process kill, or shutdown");
            if (Configured())
                SendStatus(ExecutorStatusCode.Offline).Wait();
            Console.WriteLine("Cleanup complete");
            _exitSystem = true;
            Environment.Exit(-1);
            return true;
        }
        #endregion*/

        static void Main(string[] args) {
            //_handler += new EventHandler(Handler);
            //SetConsoleCtrlHandler(_handler, true);

            if(!EnvironmentVariablesConfig){
                ReadConfig();
            }

            Program p = new Program();
            p.Start();

            while(!_exitSystem) {
                Thread.Sleep(500);
            }
        }

        class FlowStartObserver : IObserver<IOperationResult<IOnFlowStartResult>>
        {
            public void OnCompleted()
            {
                throw new NotImplementedException();
            }

            public void OnError(Exception error)
            {
                throw new NotImplementedException();
            }

            public void OnNext(IOperationResult<IOnFlowStartResult> value)
            {
                Console.WriteLine($"Flow {value?.Data?.OnFlowStart?.FlowId}: start");
                SendStatus(ExecutorStatusCode.Working).Wait();
                var flowTasksTask = _schedulerServer.GetFlowTasksForFlow.ExecuteAsync(value!.Data.OnFlowStart.FlowId);
                flowTasksTask.Wait();
                var result = flowTasksTask.Result;
                var flowRunId = value.Data.OnFlowStart.Id;
                foreach (var flowTask in result.Data.FlowTasksForFlow!)
                {
                    Console.WriteLine($"({flowTask.Task.Name}): start");
                    SendFlowTaskStatus(FlowTaskStatusCode.Processing, "task started", flowRunId, flowTask.Id).Wait();
                    var process = new Process();
                    process.StartInfo.UseShellExecute = false;
                    var environmentVariables = flowTask.EnvironmentVariables.Value.EnumerateArray().ToList();
                    foreach (var environmentVariable in environmentVariables)
                    {
                        process.StartInfo.EnvironmentVariables[environmentVariable.GetProperty("key").ToString()] =
                            environmentVariable.GetProperty("value").ToString();
                    }
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.FileName = @"C:\windows\system32\windowspowershell\v1.0\powershell.exe";
                    process.StartInfo.Arguments = "\""+flowTask.Task.Command+"; exit\"";
                    process.Start();
                    string s = process.StandardOutput.ReadLine()?.ReplaceLineEndings("");
                    Console.WriteLine($"{flowTask.Task.Name}: " + s);
                    SendFlowTaskStatus(FlowTaskStatusCode.Done, s, flowRunId, flowTask.Id).Wait();
                    process.WaitForExit();
                    Console.WriteLine($"({flowTask.Task.Name}): end");
                    Thread.Sleep(1000);
                }
                Console.WriteLine($"Flow {value?.Data?.OnFlowStart?.FlowId}: end");
                SendStatus(ExecutorStatusCode.Online).Wait();
            }
        }
        
        public async void Start() {
            var serviceCollection = new ServiceCollection();
            serviceCollection
                .AddSchedulerServer()
                .ConfigureHttpClient(client =>
                    client.BaseAddress = new Uri(GraphQlServerBaseAddress))
                .ConfigureWebSocketClient(client => 
                    client.Uri = new Uri(GraphQlServerUri));
            IServiceProvider services = serviceCollection.BuildServiceProvider();
            _schedulerServer = services.GetRequiredService<ISchedulerServer>();

            if(!Configured())
                await Configure();
            var executorId = Convert.ToInt32(ExecutorId);
            await SendStatus(ExecutorStatusCode.Online);
            Console.WriteLine("Press Ctr+C to exit");
            Console.WriteLine("start working");
            IObserver<IOperationResult<IOnFlowStartResult>> observer = new FlowStartObserver();
            _schedulerServer.OnFlowStart.Watch($"executor{executorId}").Subscribe(observer);
        }

        public static void ReadConfig(){
            var currentDirectory = Directory.GetCurrentDirectory();
            var executablePath = Path.Combine(currentDirectory, "Program.cs");
            Console.WriteLine($"executablePath: {executablePath}");
            config = ConfigurationManager.OpenExeConfiguration(executablePath);
        }

        public static void SaveConfig(){
            if(DynamicAccountId != null)
                config.AppSettings.Settings.Add("accountId", DynamicAccountId);
            if(DynamicAccountLogin != null)
                config.AppSettings.Settings.Add("accountLogin", DynamicAccountLogin);
            if(DynamicExecutorId != null)   
                config.AppSettings.Settings.Add("executorId", DynamicExecutorId);
            if(DynamicExecutorName != null)
                config.AppSettings.Settings.Add("executorName", DynamicExecutorName);
            config.Save(ConfigurationSaveMode.Minimal);
        }

        private static async Task SendStatus(ExecutorStatusCode code)
        {
            var result = await _schedulerServer.CreateExecutorStatus.ExecuteAsync(new ExecutorStatusInput
            {
                Date = DateTime.UtcNow.Ticks,
                ExecutorId = Convert.ToInt32(ExecutorId),
                StatusCode = code
            });
            result.EnsureNoErrors();
        }

        private static async Task SendFlowTaskStatus(FlowTaskStatusCode code, string description, int flowRunId, int flowTaskId)
        {
            var result = await _schedulerServer.CreateFlowTaskStatus.ExecuteAsync(new FlowTaskStatusInput
            {
                Description = description,
                FlowRunId = flowRunId,
                FlowTaskId = flowTaskId,
                Date = DateTime.UtcNow.Ticks,
                StatusCode = code
            });
            result.EnsureNoErrors();
        }

        private static bool Configured()
        {
            return AccountId != null &&
                   ExecutorId != null;
        }
        
        private static async Task Configure()
        {
            if (AccountId == null)
            {
                Console.WriteLine("Account not found, before running flows you have to login");
                IOperationResult<IGetLoginResult> result = null;
                while (true)
                {
                    Console.Write("Login: ");

                    string login = Console.ReadLine();
                    Console.Write("Password: ");
                    string password = ReadPassword();
                    Console.WriteLine("Logging in...");

                    result = await _schedulerServer.GetLogin.ExecuteAsync(login, password);
                    result.EnsureNoErrors();
                    if (result?.Data?.LocalLogin is not null)
                    {
                        break;
                    }

                    Console.WriteLine("User not found");
                }

                DynamicAccountId = result.Data.LocalLogin.Id.ToString();
                DynamicAccountLogin = result.Data.LocalLogin.Login;
            }
            
            Console.WriteLine($"User {AccountLogin} with id {AccountId} logged in");

            if (ExecutorId == null)
            {
                Console.WriteLine("Executor not registered");
                Console.Write("Executor name: ");
                var name = Console.ReadLine();
                Console.WriteLine("Executor description: ");
                var description = Console.ReadLine();
                var accountId = Int32.Parse(AccountId);
                Console.WriteLine("Registering...");
                var executorInput = new CreateExecutorInput { AccountId = accountId, Name = name, Description = description};
                var result = await _schedulerServer.CreateExecutor.ExecuteAsync(executorInput);
                result.EnsureNoErrors();
                DynamicExecutorId = result.Data?.CreateExecutor?.Id.ToString();
                DynamicExecutorName = result.Data?.CreateExecutor?.Name;
            }
            Console.WriteLine($"Executor {ExecutorName} is registered with id {ExecutorId}");
            
            if(!EnvironmentVariablesConfig){
                SaveConfig();
            }
        }

        private static string ReadPassword()
        {
            var pass = string.Empty;
            ConsoleKey key;
            do
            {
                var keyInfo = Console.ReadKey(intercept: true);
                key = keyInfo.Key;

                if (key == ConsoleKey.Backspace && pass.Length > 0)
                {
                    Console.Write("\b \b");
                    pass = pass[0..^1];
                }
                else if (!char.IsControl(keyInfo.KeyChar))
                {
                    Console.Write("*");
                    pass += keyInfo.KeyChar;
                }
            } while (key != ConsoleKey.Enter);

            Console.WriteLine();

            return pass;
        }
    }
}