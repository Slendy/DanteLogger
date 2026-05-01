using System.Net;

namespace DanteLogger.types;

public class DanteDevice
{
    public required IPAddress IpAddress { get; set; }
    public bool HasAudioData { get; set; }
    public int RxChannelCount { get; set; }
}