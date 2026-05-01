namespace DanteLogger.types;

public class RxSubscriptionData
{
    public int ChannelNumber { get; set; }
    public required string CurrentChannelName { get; set; }
    public required string DefaultChannelName { get; set; }
    public string? TxDeviceName { get; set; }
    public string? TxChannelName { get; set; }
    public byte SupportedConnections { get; set; }
    public byte ActiveConnections { get; set; }
}