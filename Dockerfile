FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build-env
WORKDIR /app
RUN git clone https://github.com/mateusz7812/scheduler-executor.git
WORKDIR /app/scheduler-executor
RUN dotnet restore
RUN dotnet publish -c Debug -o out

FROM mcr.microsoft.com/dotnet/aspnet:6.0
WORKDIR /app
COPY --from=build-env /app/scheduler-executor/out .
ENTRYPOINT ["dotnet", "SchedulerExecutorApplication.dll"]