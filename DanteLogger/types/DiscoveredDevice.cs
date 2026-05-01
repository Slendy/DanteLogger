using DanteLogger.services;

namespace DanteLogger.types;

public class DiscoveredDevice
{
    public required string IpAddress { get; set; }
    public int Port { get; set; }
    public SubscriptionStatusThread? Thread { get; set; }

    public required string Name { get; set; }

    public int[] RxChannelStatus { get; set; } = [];
    public int[] TxChannelStatus { get; set; } = [];
}