using System.Collections.Concurrent;

namespace DanteLogger.types;

public class DanteDeviceState
{
    public ConcurrentDictionary<int, DanteDeviceConnectionState> ChannelConnections { get; set; } = new();

    public static DanteDeviceConnectionState ConnectionStateFromStatus(List<int> statusList)
    {
        var status = statusList.First();
        if (statusList.Any(s => s == 65536))
        {
            status = 65536;
        }
        return status switch
        {
            9 => DanteDeviceConnectionState.ConnectedUnicast,
            10 => DanteDeviceConnectionState.ConnectedMulticast,
            14 => DanteDeviceConnectionState.ConnectedManual,
            65536 => DanteDeviceConnectionState.NoAudio,
            _ => DanteDeviceConnectionState.Unknown,
        };
    }
}

public enum DanteDeviceConnectionState
{
    Unknown,
    ConnectedUnicast,
    ConnectedMulticast,
    ConnectedManual,
    NoAudio,
}
