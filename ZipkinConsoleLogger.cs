using System;
using zipkin4net;

namespace SchedulerExecutorApplication
{
    public class ZipkinConsoleLogger : ILogger
    {
        public void LogError(string message)
        {
            Console.WriteLine(message);
        }

        public void LogInformation(string message)
        {
            Console.WriteLine(message);
        }

        public void LogWarning(string message)
        {
            Console.WriteLine(message);
        }
    }
}