using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using DanteLogger.types;
using DanteLogger.util;
using Serilog;

namespace DanteLogger.services;

public class SubscriptionStatusThread
{
    public required DiscoveredDevice Parent { get; set; }

    private UdpClient _client;
    
    private Thread? _pollingThread;

    private IPEndPoint _targetDevice;

    public ushort RxChannelCount { get; set; } = 0;
    public ushort TxChannelCount { get; set; } = 0;

    public ushort?[] SubscriptionStatuses = [];

    public void StartThread()
    {
        _targetDevice = IPEndPoint.Parse($"{Parent.IpAddress}:4440");
        _client = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
        _client.Connect(IPEndPoint.Parse($"{Parent.IpAddress}:4440"));
        // _client.ExclusiveAddressUse = false;
        // _client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        // _client.Client.Bind(_targetDevice);
        GetChannelCount();
        Log.Debug("RX: {RxChannelCount}, TX: {TxChannelCount}", RxChannelCount, TxChannelCount);
        if (RxChannelCount <= 0)
        {
            Log.Debug("Skipping Device with no RX");
            return;
        }

        SubscriptionStatuses = new ushort?[RxChannelCount];
        Array.Fill(SubscriptionStatuses, null);
        _pollingThread = new Thread(async void () =>
        {
            try
            {
                await PollLoop();
            }
            catch (Exception e)
            {
                Log.Error(e, "Encountered unknown error while polling");
            }
        });
        _pollingThread.Start();
    }

    private void GetChannelCount()
    {
        var packet = CommandUtil.BuildFinalPacket(0x1000, null, RandomNumberGenerator.GetInt32(0, 65534));
        _client.Send(packet);
        var data = _client.ReceiveAsync().Result;
        if (data.Buffer.Length <= 0) return;
        var span = data.Buffer.AsSpan();
        Log.Debug("GetChannelCount(): HexDump: {HexDump}", Convert.ToHexString(data.Buffer));
        RxChannelCount = BinaryPrimitives.ReadUInt16BigEndian(span[14..16]);
        TxChannelCount = BinaryPrimitives.ReadUInt16BigEndian(span[12..14]);
    }

    private async Task PollLoop()
    {
        while (true)
        {
            await SendSubscriptionStatusCommand(_client);
            
            Thread.Sleep(1000);
        }
    }

    public async Task SendSubscriptionStatusCommand(UdpClient client)
    {
        var commandBuffer = new MemoryStream();
        var writer = new BinaryWriter(commandBuffer);

        // writer.Write([
        //     0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00,
        //     0x00, 0x00, 0x00, 0x00, 0x83, 0x02, 0x83, 0x06, 0x03, 0x10,
        // ]);
        
        writer.Write([
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x1, 0x00, 0x1, 0x00, 0x01, 0x00, 0x00,
            // 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80, 0x00, 0x00, 0x00,
        ]);
        
        var finalData = CommandUtil.BuildFinalPacket(0x3400, commandBuffer.ToArray(), RandomNumberGenerator.GetInt32(0, 65534));

        client.Send(finalData);

        var data = await client.ReceiveAsync();
        
        Log.Debug("Received {BufferLength} bytes back", data.Buffer.Length);

        var results = CommandUtil.ParseSubscriptionResponse(data.Buffer);
        Log.Debug("Parsed {ResultsCount} responses", results?.Count);
    }
    
}