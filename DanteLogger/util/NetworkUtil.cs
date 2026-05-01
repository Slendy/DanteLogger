using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Serilog;

namespace DanteLogger.util;

public static class NetworkUtil
{
    public static List<IPAddress> GetLocalIpAddresses()
    {
        List<IPAddress> addresses = [];
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            // Filter for active (Up) interfaces and ignore loopback/virtual adapters if needed
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            Log.Debug("Interface: {InterfaceName} ({InterfaceDescription})", ni.Name, ni.Description);
        
            foreach (var ip in ni.GetIPProperties().UnicastAddresses)
            {
                switch (ip.Address.AddressFamily)
                {
                    // IPv4
                    case AddressFamily.InterNetwork:
                        addresses.Add(ip.Address);
                        Log.Debug("  IPv4 Address: {IpAddress}", ip.Address);
                        break;
                    // IPv6
                    case AddressFamily.InterNetworkV6:
                        // Logger.DebugLog($"  IPv6 Address: {ip.Address}");
                        break;
                    case AddressFamily.Unknown:
                    case AddressFamily.Unspecified:
                    case AddressFamily.Unix:
                    case AddressFamily.ImpLink:
                    case AddressFamily.Pup:
                    case AddressFamily.Chaos:
                    case AddressFamily.Ipx:
                    case AddressFamily.Iso:
                    case AddressFamily.Ecma:
                    case AddressFamily.DataKit:
                    case AddressFamily.Ccitt:
                    case AddressFamily.Sna:
                    case AddressFamily.DecNet:
                    case AddressFamily.DataLink:
                    case AddressFamily.Lat:
                    case AddressFamily.HyperChannel:
                    case AddressFamily.AppleTalk:
                    case AddressFamily.NetBios:
                    case AddressFamily.VoiceView:
                    case AddressFamily.FireFox:
                    case AddressFamily.Banyan:
                    case AddressFamily.Atm:
                    case AddressFamily.Cluster:
                    case AddressFamily.Ieee12844:
                    case AddressFamily.Irda:
                    case AddressFamily.NetworkDesigners:
                    case AddressFamily.Max:
                    case AddressFamily.Packet:
                    case AddressFamily.ControllerAreaNetwork:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(ip.Address.AddressFamily) + " not supported");
                }
            }
        }
        return addresses;
    }
    
}