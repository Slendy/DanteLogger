using DanteLogger.services;
using Serilog;
using Serilog.Core;
using Serilog.Events;

var levelSwitch = new LoggingLevelSwitch
{
    MinimumLevel = LogEventLevel.Information
};

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.ControlledBy(levelSwitch)
    .WriteTo.Console(outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File("logs/dante-logger.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

if (args.Length > 0)
{
    if (args.Any(a => a == "-debug=true"))
    {
        levelSwitch.MinimumLevel = LogEventLevel.Debug;
        Log.Debug("Debug logging enabled.");
    }
}

try
{
    Log.Information("Starting Dante Disconnect Monitor...");
    var danteMonitor = new DanteDisconnectMonitor(CancellationToken.None);
    await danteMonitor.MonitorTask;
}
catch (Exception ex)
{
    Log.Error(ex, "Something went wrong");
}
finally
{
    await Log.CloseAndFlushAsync();
}
