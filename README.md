# scheduler-executor

Scheduler is a tool for IT administrators that enables remote execution of PowerShell commands on computers within a network. Its features include creating task templates, simultaneously executing task trees on multiple machines, and logging actions, which simplifies the management of the IT environment and increases administrative efficiency. The project can be especially useful in large IT environments where managing numerous computers requires automation and centralization of tasks.

The Executor is a worker node that receives task trees from the service and executes them on the specified system. It runs commands in a PowerShell environment, uses GraphQL to communicate with services, and logs to Zipkin for distributed tracing.

The Azure pipeline builds image and pushes it to repository at: https://hub.docker.com/repository/docker/mateusz7812/scheduler_executor/general.

More information can be found in other projects related to the Scheduler app.
