using System.Net;
using System.Net.Sockets;
using DanteLogger.services;
using DanteLogger.util;
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
    .WriteTo.File("dante-logger-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

if (args.Length > 0)
{
    if (args.Length == 2 && args[0].Equals("substatus", StringComparison.InvariantCultureIgnoreCase))
    {
        levelSwitch.MinimumLevel = LogEventLevel.Debug;
        Log.Information("Checking subscription statuses of {IpAddress}", args[1]);
        var deviceIp = args[1];
        if (!IPAddress.TryParse(deviceIp, out var ipAddress))
        {
            Log.Error("Invalid IP address");
            return;
        }

        var client = new UdpClient();
        client.ExclusiveAddressUse = false;
        client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        client.Connect(ipAddress, 4440);
        var (txChannelCount, rxChannelCount) = await CommandUtil.GetChannelCount(client);
        if (rxChannelCount == null)
        {
            Log.Error("Failed to fetch channel counts");
            return;
        }
        Log.Information("Total channel counts: RX={RxCount} Tx={TxCount}", rxChannelCount, txChannelCount);
        var subscriptionStatus = await CommandUtil.GetSubscriptionStatus(client, rxChannelCount.Value);
        
        Log.Information("Total subscriptions {TotalSubscriptions}", subscriptionStatus.Count);
        Log.Information("Highest Channel Number {HighestChannel}", subscriptionStatus.Max(s => s.ChannelNumber));
        foreach (var data in subscriptionStatus)
        {
            var statuses = DanteUtils.DetermineRxStatus(data.Status, data.SupportedConnections, data.ActiveConnections);
            
            Log.Debug("RX statuses CH {ChNum}: {Statuses} status={Status} active={Active} supported={Supported}", data.ChannelNumber, statuses, data.Status, data.ActiveConnections, data.SupportedConnections);
        }
        return;
    }
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
