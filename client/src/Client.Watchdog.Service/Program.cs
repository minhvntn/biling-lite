using Client.Watchdog.Service;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "ServerManagerBillingWatchdog";
});
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
