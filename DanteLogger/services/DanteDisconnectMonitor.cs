using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using DanteLogger.types;
using DanteLogger.util;
using Serilog;

namespace DanteLogger.services;

public class DanteDisconnectMonitor
{
    private const string NotificationGroup = "224.0.0.231";
    private const int NotificationPort = 8702;

    private const int CommandPort = 4440;
    
    private readonly CancellationToken _stopToken;
    private readonly UdpClient _client;

    // key: ipv4 address, value: device Task
    private readonly ConcurrentDictionary<IPAddress, Task> _deviceTasks = [];
    
    public Task MonitorTask { get; set; }

    private readonly ConcurrentDictionary<string, DanteDeviceState> _deviceState = [];
    
    public DanteDisconnectMonitor(CancellationToken stopToken)
    {
        _stopToken = stopToken;
        
        _client = new UdpClient();
        _client.ExclusiveAddressUse = false;
        _client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _client.Client.Bind(new IPEndPoint(IPAddress.Any, NotificationPort));
        foreach (var ip in NetworkUtil.GetLocalIpAddresses())
        {
            _client.JoinMulticastGroup(IPAddress.Parse(NotificationGroup), ip);    
        }
        MonitorTask = Task.Run(NotificationMonitorLoop, stopToken);
    }

    private void SetDeviceState(string deviceName, RxSubscriptionData subscriptionData, DanteDeviceConnectionState newState)
    {
        if (!_deviceState.ContainsKey(deviceName))
        {
            _deviceState.TryAdd(deviceName, new DanteDeviceState());
        }
        
        var deviceState = _deviceState[deviceName];
        if (deviceState.ChannelConnections.TryGetValue(subscriptionData.ChannelNumber, out var oldState))
        {
            if (oldState == DanteDeviceConnectionState.NoAudio && newState != DanteDeviceConnectionState.NoAudio)
            {
                Log.Warning("[{DeviceName}]: Audio traffic has been restored on channel {SubscriptionDataChannelNumber} ({SubscriptionDataCurrentChannelName})", deviceName, subscriptionData.ChannelNumber, subscriptionData.CurrentChannelName);
            } else if (newState == DanteDeviceConnectionState.NoAudio && oldState != DanteDeviceConnectionState.NoAudio)
            {
                Log.Fatal("[{DeviceName}]: No audio data on channel {SubscriptionDataChannelNumber} ({SubscriptionDataCurrentChannelName})", deviceName, subscriptionData.ChannelNumber + 1, subscriptionData.CurrentChannelName);
            }
        }
        deviceState.ChannelConnections.AddOrUpdate(subscriptionData.ChannelNumber, newState, (_, _) => newState);
    }

    private async Task QueryDeviceState(IPAddress address)
    {
        var client = new UdpClient(_client.Client.AddressFamily);
        try
        {
            client.Connect(address, CommandPort);
        }
        catch (Exception e)
        {
            Log.Error("Failed to connect to device at {DeviceIp}: {Error}", address.ToString(), e);
            return;
        }

        var (txChannelCount, rxChannelCount) = await CommandUtil.GetChannelCount(client);

        if (rxChannelCount == null || txChannelCount == null)
        {
            Log.Error("Failed to fetch channel count for device at {DeviceIp}", address.ToString());
            return;
        }

        var deviceName = await CommandUtil.GetDeviceName(client);
        if (deviceName == null)
        {
            Log.Error("Failed to fetch device name for device at {DeviceIp}", address.ToString());
            return;
        }
        Log.Debug("Received RX update notification from {DeviceName}: {RxChannelCount} RX, {TxChannelCount TX}", deviceName, rxChannelCount, txChannelCount);

        var rxChannels = await CommandUtil.GetRxChannels(client, rxChannelCount.Value);

        var subscriptionData = await CommandUtil.GetSubscriptionStatus(client, rxChannelCount.Value);

        if (rxChannels.Count != subscriptionData.Count)
        {
            Log.Warning("Channel count mismatch ({RxChannelsCount} channels != {SubscriptionDataCount} subscriptions)", rxChannels.Count, subscriptionData.Count);
            return;
        }

        for (var i = 0; i < rxChannels.Count; i++)
        {
            var rxChannel = rxChannels[i];
            var subscriptionStatus = subscriptionData[i];
            var statuses = DanteUtils.DetermineRxStatus(rxChannel.SubscriptionStatusCode, subscriptionStatus.SupportedConnections, subscriptionStatus.ActiveConnections);
            
            SetDeviceState(deviceName, subscriptionStatus, DanteDeviceState.ConnectionStateFromStatus(statuses));
        }
    }
    
    private async Task NotificationMonitorLoop()
    {
        while (!_stopToken.IsCancellationRequested)
        {
            await ReadNotification();
        }
    }

    private async Task ReadNotification()
    {
        var notificationData = await _client.ReceiveAsync(_stopToken);
            
        var parsedNotification = VendorMessage.Parse(notificationData.Buffer);

        if (parsedNotification is not { MessageType: 0x102 or 0x104 or 0x105 })
        {
            return;
        }
        Debug.Assert(parsedNotification != null, nameof(parsedNotification) + " != null");
        
        Log.Debug("Received message type 0x{MessageType:X} from {DeviceIp}", parsedNotification.MessageType, notificationData.RemoteEndPoint.Address.ToString());

        var dataSpan = parsedNotification.Body.AsSpan();
        
        var numChannels = BinaryPrimitives.ReadUInt16BigEndian(dataSpan);
        for (var i = 0; i < numChannels; i++)
        {
            var channelBitFlag =  dataSpan[2 + i];
            Log.Debug("CH group {GroupNumber}: {ChannelBitFlag:b8}", i + 1, channelBitFlag);
            for (var bit = 0; bit < 8; bit++)
            {
                Log.Debug("Channel {ChannelNumber}: {Bit} ({BitFlag} & {ShiftAmount})", i + 1, channelBitFlag & 1 << bit, channelBitFlag, 1 << bit);
            }
        }
        if (!_deviceTasks.ContainsKey(notificationData.RemoteEndPoint.Address))
        {
            _deviceTasks.TryAdd(notificationData.RemoteEndPoint.Address, Task.Run(async () =>
            {
                await QueryDeviceState(notificationData.RemoteEndPoint.Address);
                _deviceTasks.Remove(notificationData.RemoteEndPoint.Address, out _);
            }, _stopToken));    
        }
    }
}