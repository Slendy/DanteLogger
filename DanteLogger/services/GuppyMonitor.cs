using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using DanteLogger.types;
using DanteLogger.util;
using Serilog;
using Serilog.Core;

namespace DanteLogger.services;

public class GuppyMonitor : IDisposable
{
    private const string GuppyGroup = "224.0.0.233";
    private const string GuppyPort = "8708";

    private readonly CancellationToken _stopToken;
    private readonly ConcurrentDictionary<string, DiscoveredDevice> _devices;

    // private readonly ReadOnlySpan<byte> AudinateHeader = "Audinate"u8;

    private readonly UdpClient _client;
    private readonly IPEndPoint _receiveEndpoint = IPEndPoint.Parse($"0.0.0.0:{GuppyPort}");
    public GuppyMonitor(ConcurrentDictionary<string, DiscoveredDevice> devices, CancellationToken stopToken)
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
                        Log.Debug("  IPv6 Address: {IpAddress}", ip.Address);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(ip.Address.AddressFamily) + " not supported");
                }
            }
        }
        _stopToken = stopToken;
        _devices = devices;
        _client = new UdpClient();
        _client.ExclusiveAddressUse = false;
        _client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _client.Client.Bind(new IPEndPoint(IPAddress.Any, 8708));
        foreach (var ip in addresses)
        {
            _client.JoinMulticastGroup(IPAddress.Parse(GuppyGroup), ip);    
        }
        
    }

    private ConcurrentDictionary<string, DiscoveredDevice> devices = new();

    public async Task StartGuppyMonitor()
    {
        while (!_stopToken.IsCancellationRequested)
        {
            var udpResult = await _client.ReceiveAsync(_stopToken);
            
            var sourceIp = udpResult.RemoteEndPoint.Address.ToString();
            var device = devices.GetValueOrDefault(sourceIp);
            if (device == null)
            {
                device = new DiscoveredDevice
                {
                    IpAddress = sourceIp,
                    Name = "Unknown",
                };
                devices.TryAdd(sourceIp, device);
            }
            
            DanteUtils.ParseMonitoringMessage(udpResult.Buffer, ref device);
        }
    }

    public void Dispose()
    {
        _client.Dispose();
        GC.SuppressFinalize(this);
    }
}