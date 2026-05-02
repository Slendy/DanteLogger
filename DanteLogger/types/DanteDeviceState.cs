using System.Collections.Concurrent;

namespace DanteLogger.types;

public class DanteDeviceState
{
    public ConcurrentDictionary<int, DanteDeviceConnectionState> ChannelConnections { get; set; } = new();
    
    public static DanteDeviceConnectionState ConnectionStateFromStatus(int status)
    {
        return status switch
        {
            9 => DanteDeviceConnectionState.ConnectedUnicast,
            10 => DanteDeviceConnectionState.ConnectedMulticast,
            14 => DanteDeviceConnectionState.ConnectedManual,
            65536 => DanteDeviceConnectionState.NoAudio,
            _ => DanteDeviceConnectionState.Unknown,
        };
    }

    public static DanteDeviceConnectionState ConnectionStateFromStatus(List<int> statusList)
    {
        var status = statusList.First();
        if (statusList.Any(s => s == 65536))
        {
            status = 65536;
        }

        return ConnectionStateFromStatus(status);
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

public static class DanteDeviceConnectionStateExtensions
{
    public static string GetDescription(this DanteDeviceConnectionState state)
    {
        return state switch
        {
            DanteDeviceConnectionState.Unknown => "Unknown",
            DanteDeviceConnectionState.ConnectedUnicast => "Connected (unicast)",
            DanteDeviceConnectionState.ConnectedMulticast => "Connected (multicast)",
            DanteDeviceConnectionState.ConnectedManual => "Manually configured",
            DanteDeviceConnectionState.NoAudio => "No audio data",
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
        };
    }
    public static string GetTransmissionType(this DanteDeviceConnectionState state)
    {
        return state switch
        {
            DanteDeviceConnectionState.Unknown => "Unknown",
            DanteDeviceConnectionState.ConnectedUnicast => "Unicast",
            DanteDeviceConnectionState.ConnectedMulticast => "Multicast",
            DanteDeviceConnectionState.ConnectedManual => "Manually configured",
            DanteDeviceConnectionState.NoAudio => "No audio data",
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
        };
    }
}