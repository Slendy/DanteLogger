using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using DanteLogger.types;
using DanteLogger.util;
using Serilog;

namespace DanteLogger.services;

public class VendorMonitor : IDisposable
{
    private const string VendorGroup = "224.0.0.231";
    private const int VendorPort = 8702;

    private readonly CancellationToken _stopToken;
    private readonly ConcurrentDictionary<string, DiscoveredDevice> _devices;

    // private readonly ReadOnlySpan<byte> AudinateHeader = "Audinate"u8;

    private readonly UdpClient _client;
    private readonly IPEndPoint _receiveEndpoint = IPEndPoint.Parse($"0.0.0.0:{VendorPort}");
    public VendorMonitor(ConcurrentDictionary<string, DiscoveredDevice> devices, CancellationToken stopToken)
    {
        Log.Information($"Started vendor monitoring on {VendorGroup}");
        _stopToken = stopToken;
        _devices = devices;
        _client = new UdpClient();
        _client.ExclusiveAddressUse = false;
        _client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _client.Client.Bind(new IPEndPoint(IPAddress.Any, VendorPort));
        foreach (var ip in NetworkUtil.GetLocalIpAddresses())
        {
            _client.JoinMulticastGroup(IPAddress.Parse(VendorGroup), ip);    
        }
        
    }

    private readonly ConcurrentDictionary<string, DiscoveredDevice> devices = new();

    public async Task StartVendorMonitoring()
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
            Log.Debug("Received message from " + sourceIp);

            var data = udpResult.Buffer.AsSpan();

            var protocolId = BinaryPrimitives.ReadUInt16BigEndian(data);
            Log.Debug("Protocol Id: 0x{ProtocolId:X}", protocolId);
            var versionMajor = data[0x18];
            var versionMinor = data[0x19];
            Log.Debug("version: {VersionMajor}.{VersionMinor}", versionMajor, versionMinor);
            var messageType = BinaryPrimitives.ReadUInt16BigEndian(data[0x1A..]);
            Log.Debug("Message type: 0x{MessageType:X}", messageType);
            if (messageType is 0x102 or 0x104 or 0x105)
            {
                Log.Debug("Discarding message type {MessageType}", messageType);
                continue;
            }
            var delay = BinaryPrimitives.ReadUInt32BigEndian(data[0x1C..]);
            var numChannels = BinaryPrimitives.ReadUInt16BigEndian(data[0x20..]);
            for (var i = 0; i < numChannels; i++)
            {
                var channelBitFlag =  data[0x21 + i];
                Log.Debug("CH group {ChNumber}: {ChannelBitFlag:b8}", i + 1, channelBitFlag);
                for (var bit = 0; bit < 8; bit++)
                {
                    Log.Debug("Channel {Bit}: {BitFlag} ({ChannelBitFlag} & {I})", bit, (channelBitFlag & 1 << bit) > 0, channelBitFlag, 1 << bit);
                }
            }


            
        }
    }

    public void Dispose()
    {
        _client.Dispose();
        GC.SuppressFinalize(this);
    }
}