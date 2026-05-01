using System.Buffers.Binary;
using System.Diagnostics;
using System.Numerics;
using DanteLogger.types;
using Serilog;

namespace DanteLogger.util;

public static class DanteUtils
{
    const string danteArcService = "_netaudio-arc._udp";
    const string DanteControlService = "_netaudio-cmc._udp";
    const string DanteChannelService = "_netaudio-chan._udp";
    
    private static bool IsConnectedStatus(ushort rxStatus)
    {
            return rxStatus is 9 or 10 or 14;
    }

    public static List<int> DetermineRxStatus(ushort rxStatus, byte supportedConnections, byte activeConnections)
    {
        var nsc = BitOperations.PopCount(supportedConnections);
        var nac = BitOperations.PopCount(activeConnections);

        if (nsc <= 1) return nsc == 1 && activeConnections == 0 ? [65536] : [rxStatus];
        
        var temp = new int[nsc];
        var i = 0;

        for (var pos = 0; i < nsc; i++)
        {
            if ((supportedConnections & 1 << i) <= 0) continue;
                
            if ((activeConnections & 1 << i) > 0)
            {
                temp[pos] = rxStatus;
            }
            else if (nac > 0 || IsConnectedStatus(rxStatus))
            {
                temp[pos] = 65536;
            }
            else
            {
                temp[pos] = rxStatus;
            }

            pos++;
        }

        return [..temp];

    }
    public static void ParseMonitoringMessage(byte[] data, ref DiscoveredDevice? device)
    {
        if (data.Length < 24)
        {
            // skip invalid packet
            return;
        }

        if (data[0] != 0xFF && data[1] != 0xFE)
        {
            
            Log.Debug("DEBUG: message class is not guppy ({B:X}{B1:X})", data[0], data[1]);
            return;
        }

        if (data.AsSpan()[16..24].SequenceCompareTo("Audinate"u8) != 0)
        {
            Log.Debug("ParseMonitoringMessage(): Audinate Header not found");
            return;
        }

        var reader = new BinaryReader(new MemoryStream(data, false));
        reader.BaseStream.Position = 24;
        var msgType = BinaryPrimitives.ReadUInt16LittleEndian(reader.ReadBytes(2));
        
        if (msgType != 0x800)
        {
            Log.Debug("ParseMonitoringMessage(): Message type is not monitor message ({MsgType:X} != 0x800)", msgType);
            return;
        }

        if (data.Length < 40)
        {
            Log.Debug("ParseMonitoringMessage(): Packet is too small");
            return;
        }

        // skip to body
        reader.BaseStream.Position = 32;

        while (reader.BaseStream.Position + 4 < data.Length)
        {
            var baseOffset = reader.BaseStream.Position;
            int messageLength = BinaryPrimitives.ReadUInt16BigEndian(reader.ReadBytes(2));
            Log.Debug("ParseMonitoringMessage(): Message length: {MessageLength}", messageLength);
            if (messageLength < 8)
            {
                continue;
            }

            int opcode = BinaryPrimitives.ReadUInt16BigEndian(reader.ReadBytes(2));
            Log.Debug("ParseMonitoringMessage(): OpCode: {OpCode:X}", opcode);
            if (opcode < 0x8000)
            {
                // skip message
                reader.BaseStream.Position += messageLength - 4; // we already read 4 bytes
                continue;
            }

            int subheaderLength = BinaryPrimitives.ReadUInt16BigEndian(reader.ReadBytes(2));
            int payloadLength = BinaryPrimitives.ReadUInt16BigEndian(reader.ReadBytes(2));
            if (messageLength < 8 + subheaderLength + payloadLength)
            {
                Log.Debug("ParseMonitoringMessage(): Invalid payload length");
                continue;
            }

            var monitorSeqNum = BinaryPrimitives.ReadUInt16BigEndian(reader.ReadBytes(2));
            Log.Debug("ParseMonitoringMessage(): subHeaderLength: {SubheaderLength}, payloadLength: {PayloadLength}, monitorSeqnum: {MonitorSeqNum}", subheaderLength, payloadLength, monitorSeqNum);
            reader.BaseStream.Position = baseOffset + subheaderLength + 8; // subheaderOffset is 8
            switch (opcode)
            {
                case 0x8000: // ifstats
                {
                    int ifStatsArrayOffset = BinaryPrimitives.ReadUInt16BigEndian(reader.ReadBytes(2));
                    if (ifStatsArrayOffset + 4 > messageLength)
                    {
                        Log.Debug("ParseMonitoringMessage(): Error: packet too short to for ifstats array header");
                        continue;
                    }
                    
                    break;
                }
                case 0x8001: // clock monitoring
                {
                    break;
                }
                case 0x8002: // signal presence
                {
                    if (payloadLength < 12)
                    {
                        Log.Debug("ParseMonitoringMessage(): Received malformed signal presence packet (too smol)");
                        return;
                    }

                    int numTxChannels = BinaryPrimitives.ReadUInt16BigEndian(reader.ReadBytes(2));
                    int firstTxChannelIndex = BinaryPrimitives.ReadUInt16BigEndian(reader.ReadBytes(2));
                    int numRxChannels = BinaryPrimitives.ReadUInt16BigEndian(reader.ReadBytes(2));
                    int firstRxChannelIndex = BinaryPrimitives.ReadUInt16BigEndian(reader.ReadBytes(2));
                    int arrayOffset = BinaryPrimitives.ReadUInt16BigEndian(reader.ReadBytes(2));
                    Log.Debug("numTxChannels: {NumTxChannels}", numTxChannels);
                    Log.Debug("firstTxChannelIndex: {FirstTxChannelIndex}", firstTxChannelIndex);
                    Log.Debug("numRxChannels: {NumRxChannels}", numRxChannels);
                    Log.Debug("firstRxChannelIndex: {FirstRxChannelIndex}", firstRxChannelIndex);
                    Log.Debug("arrayOffset: {ArrayOffset}", arrayOffset);
                    if (arrayOffset + numTxChannels + numRxChannels > messageLength)
                    {
                        Log.Debug("ParseMonitoringMessage(): Invalid packet: message too small for signal presence array for message");
                        return;
                    }
                    
                    Log.Debug("Received signal presence from {IpAddress}", device?.IpAddress);

                    reader.BaseStream.Position = baseOffset + arrayOffset;

                    if (firstTxChannelIndex != 0 || firstRxChannelIndex != 0)
                    {
                        Log.Debug("TX/RX OFFSET EDGE CASE!!! dumping hex");
                        Log.Debug("{HexDump}", Convert.ToHexString(data));
                        return;
                    }

                    int[] txValues = [];
                    int[] rxValues = [];

                    if (numTxChannels > 0)
                    {
                        txValues = new int[numTxChannels];
                        for (var i = 0; i < numTxChannels; i++)
                        {
                            txValues[i] = reader.ReadByte();
                            Log.Debug("TX Channel {ChannelNumber} status: {SubscriptionStatus}", i, SubscriptionStatusToString(txValues[i]));
                        }
                    }

                    if (numRxChannels > 0)
                    {
                        rxValues = new int[numRxChannels];
                        for (var i = 0; i < numRxChannels; i++)
                        {
                            rxValues[i] = reader.ReadByte();
                            Log.Debug("RX Channel {ChannelNumber} status: {SubscriptionStatus}", i, SubscriptionStatusToString(rxValues[i]));
                        }
                    }

                    if (device != null)
                    {
                        // set 
                        if (device.TxChannelStatus.Length != txValues.Length)
                        {
                            device.TxChannelStatus = rxValues;
                        }
                        else
                        {
                            for (var i = 0; i < txValues.Length; i++)
                            {
                                if (txValues[i] == device.TxChannelStatus[i]) continue;
                                Log.Debug("OLD TX: {OldTxStatus:b8}", device.TxChannelStatus[i]);
                                Log.Debug("NEW TX: {NewTxStatus:b8}", txValues[i]);
                                Log.Debug("XOR TX: {XorTxStatus:b8}", device.TxChannelStatus[i] ^ txValues[i]);
                                Log.Debug(
                                    "Device {DeviceName} ({DeviceIp} Ch {ChannelNumber} TX status changed. {OldStatus} -> {NewStatus}",
                                    device.Name, device.IpAddress, i, device.TxChannelStatus[i], txValues[i]);
                                device.TxChannelStatus[i] = txValues[i];
                            }
                        }
                        
                        if (device.RxChannelStatus.Length != rxValues.Length)
                        {
                            device.RxChannelStatus = rxValues;
                        }
                        else
                        {
                            for (var i = 0; i < rxValues.Length; i++)
                            {
                                if (rxValues[i] == device.RxChannelStatus[i]) continue;
                                Log.Debug("OLD RX: {OldRxStatus:b8}", device.RxChannelStatus[i]);
                                Log.Debug("NEW RX: {NewRxStatus:b8}", rxValues[i]);
                                Log.Debug("XOR RX: {XorRxStatus:b8}", device.RxChannelStatus[i] ^ rxValues[i]);
                                Log.Debug(
                                    "Device {DeviceName} ({DeviceIp} Ch {ChannelNumber} RX status changed. {OldStatus} -> {NewStatus}",
                                    device.Name, device.IpAddress, i, device.RxChannelStatus[i], rxValues[i]);
                                device.RxChannelStatus[i] = rxValues[i];
                            }
                        }
                    }
                    break;
                }
                case 0x8003: // latency samples
                case 0x8004: // late packets
                {
                    break;
                }
                default:
                {
                    Log.Debug("ParseMonitoringMessage(): UNKNOWN PACKET: {Opcode}", opcode);
                    break;
                }
            }
            // set reader position to next message
            reader.BaseStream.Position = baseOffset + messageLength;
        }
    }


    public static string SubscriptionStatusToString(int subStatus)
        =>
            subStatus switch
            {
                0 => "none",
                1 => "unresolved",
                2 => "resolved",
                3 => "resolveFail",
                4 => "subscribeSelf",
                5 => "resolvedNone",
                7 => "idle",
                8 => "inProgress",
                9 => "dynamic",
                10 => "static",
                14 => "manual",
                15 => "noConnection",
                16 => "channelFormat",
                17 => "bundleFormat",
                18 => "noRx",
                19 => "rxFail",
                20 => "noTx",
                21 => "txFail",
                22 => "qosFailRx",
                23 => "qosFailTx",
                24 => "txRejectedAddr",
                25 => "invalidMsg",
                26 => "channelLatency",
                27 => "clockDomain",
                28 => "unsupported",
                29 => "rxLinkDown",
                30 => "txLinkDown",
                31 => "dynamicProtocol",
                32 => "invalidChannel",
                33 => "txSchedulerFailure",
                34 => "subscribeSelfPolicy",
                35 => "txNotReady",
                36 => "rxNotReady",
                37 => "txFanoutLimitReached",
                38 => "txChannelEncrypted",
                39 => "txResponseUnexpected",
                64 => "templateMismatchDevice",
                65 => "templateMismatchFormat",
                66 => "templateMissingChannel",
                67 => "templateMismatchConfig",
                68 => "templateFull",
                69 => "rxUnsupportedSubMode",
                70 => "txUnsupportedSubMode",
                96 => "txAccessControlDenied",
                97 => "txAccessControlPending",
                112 => "hdcpNegotiationError",
                113 => "rxEncryptionUnsupported",
                114 => "rxTransportUnsupported",
                128 => "flowDataStatusError",
                129 => "flowDataStatusPending",
                255 => "systemFail",
                256 => "flagNoAdvert",
                512 => "flagNoDbcp",
                65536 => "noData",
                _ => $"unknown ({subStatus})"
            };
}
    