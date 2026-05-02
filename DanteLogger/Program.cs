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
    if (args[0] == "-v" || args[0] == "--version")
    {
        Log.Information("DanteLogger v{Tag} {GitCommit}", ThisAssembly.Git.Tag, ThisAssembly.Git.Commit);
        Log.Information("Git Branch: {Branch}", ThisAssembly.Git.Branch);
        Log.Information("Git Commit Date: {CommitDate}", ThisAssembly.Git.CommitDate);
        Log.Information("Git Repo Url: {RepoUrl}", ThisAssembly.Git.RepositoryUrl);
        Log.Information("Git Tag: {Tag}", ThisAssembly.Git.Tag);
        Log.Information("Git BaseTag: {BaseTag}", ThisAssembly.Git.BaseTag);
        Log.Information("Git BaseVersion: {Major}.{Minor}.{Patch}", ThisAssembly.Git.BaseVersion.Major, ThisAssembly.Git.BaseVersion.Minor, ThisAssembly.Git.BaseVersion.Patch);
        Log.Information("Git SemVer Source: {Source}", ThisAssembly.Git.SemVer.Source);
        Log.Information("Git SemVer: {Major}.{Minor}.{Patch}", ThisAssembly.Git.SemVer.Major,  ThisAssembly.Git.SemVer.Minor, ThisAssembly.Git.SemVer.Patch);
        return;
    }
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
    Log.Information("Version: {Version}, Commit {Commit}", ThisAssembly.Git.Tag, ThisAssembly.Git.Commit);
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
