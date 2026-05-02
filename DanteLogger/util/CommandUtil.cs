using System.Buffers.Binary;
using System.Diagnostics;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DanteLogger.types;
using Serilog;

namespace DanteLogger.util;

public static class CommandUtil
{
    public static void WriteUint16BigEndian(ushort value, BinaryWriter writer)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(buffer, value);
        writer.Write(buffer);
    }

    public static byte[] BuildFinalPacket(ushort opcode, byte[]? payload = null, int transactionId = 0)
    {
        payload ??= "\0\0"u8.ToArray();
        var length = 8 + payload.Length;
        var ms = new MemoryStream();
        var writer = new BinaryWriter(ms);
        // const int protocolId = 0x27FF;
        const int protocolId = 0x2809;
        WriteUint16BigEndian(protocolId, writer);
        WriteUint16BigEndian((ushort)length, writer);
        WriteUint16BigEndian((ushort)transactionId, writer);
        WriteUint16BigEndian(opcode, writer);
        writer.Write(payload);
        return ms.ToArray();
    }

    public static async Task<string?> GetDeviceName(UdpClient client)
    {
        var packet = BuildFinalPacket(0x1002, null, RandomNumberGenerator.GetInt32(0, 65534));
        client.Send(packet);
        var data = await client.ReceiveAsync();
        return data.Buffer.Length <= 0 ? null : ReadNullTerminatedString(data.Buffer.AsSpan()[10..]);
    }

    public static async Task<(int? txChannelCount, int? rxChannelCount)> GetChannelCount(UdpClient client)
    {
        var packet = BuildFinalPacket(0x1000, null, RandomNumberGenerator.GetInt32(0, 65534));
        client.Send(packet);
        var data = await client.ReceiveAsync();
        if (data.Buffer.Length <= 0) return (null, null);
        var span = data.Buffer.AsSpan();
        var txChannelCount = BinaryPrimitives.ReadUInt16BigEndian(span[12..14]);
        var rxChannelCount = BinaryPrimitives.ReadUInt16BigEndian(span[14..16]);

        return (txChannelCount, rxChannelCount);
    }

    public static async Task<List<RxSubscriptionData>> GetSubscriptionStatus(UdpClient client, int rxChannelCount)
    {
        List<RxSubscriptionData> rxSubscriptions = [];
        for (var page = 0; page < Math.Max(rxChannelCount / 16, 1); page++)
        {
            var commandBuffer = new MemoryStream();
            var writer = new BinaryWriter(commandBuffer);
            
            var startingChannel = (byte)(page * 16 + 1);

            writer.Write([
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x1, 0x00, 0x1, 0x00, startingChannel, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x83, 0x02, 0x83, 0x06, 0x03, 0x10,
            ]);

            var finalData = BuildFinalPacket(0x3400, commandBuffer.ToArray(), RandomNumberGenerator.GetInt32(0, 65534));

            client.Send(finalData);

            var data = await client.ReceiveAsync();

            var subscriptionData = ParseSubscriptionResponse(data.Buffer);
            if (subscriptionData == null)
            {
                Log.Warning("Subscription data is null for page {PageNumber}", page);
                Log.Warning("Dumping Hex Response: {HexDump}", Convert.ToHexString(data.Buffer));
                break;
            }
            rxSubscriptions.AddRange(subscriptionData);
        }
        return rxSubscriptions;
    }

    private static string ReadNullTerminatedString(Span<byte> buffer)
    {
        var nullIndex = buffer.IndexOf((byte)'\0');

        return Encoding.ASCII.GetString(buffer.ToArray(), 0, nullIndex);
    }

    public static async Task<List<RxChannelData>> GetRxChannels(UdpClient client, int rxChannelCount)
    {
        List<RxChannelData> channels = [];
        for (var page = 0; page < Math.Max(rxChannelCount / 16, 1); page++)
        {
            var commandBuffer = new MemoryStream();
            var writer = new BinaryWriter(commandBuffer);
            var startingChannel = page * 16 + 1;
            // write 2 byte zero
            WriteUint16BigEndian(0, writer);

            // write 1 byte zero
            writer.Write((byte)0);
            // write 1 byte one
            writer.Write((byte)1);
            // 2 byte starting channel index
            WriteUint16BigEndian((ushort)startingChannel, writer);
            // 2 byte zero
            WriteUint16BigEndian(0, writer);

            var finalData = BuildFinalPacket(0x3000, commandBuffer.ToArray(), RandomNumberGenerator.GetInt32(0, 65534));

            client.Send(finalData);

            var data = await client.ReceiveAsync();

            if (data.Buffer.Length <= 0) continue;

            var body = data.Buffer.AsSpan()[10..];
            // TODO dont over-read
            for (var i = 0; i < 16; i++)
            {
                const int rxRecordSize = 20;
                const int bodyHeaderSize = 2;
                var recordOffset = bodyHeaderSize + (i * rxRecordSize);
                if (recordOffset + rxRecordSize > body.Length)
                {
                    break;
                }

                var record = body[recordOffset..(recordOffset + rxRecordSize)];
                var channelNumber = BinaryPrimitives.ReadUInt16BigEndian(record);
                var expected = page * 16 + i + 1;
                if (channelNumber == 0 || channelNumber != expected)
                {
                    break;
                }

                var txChannelOffset = BinaryPrimitives.ReadUInt16BigEndian(record[6..8]);
                var txDeviceOffset = BinaryPrimitives.ReadUInt16BigEndian(record[8..10]);
                var rxChannelOffset = BinaryPrimitives.ReadUInt16BigEndian(record[10..12]);
                var rxChannelStatusCode = BinaryPrimitives.ReadUInt16BigEndian(record[12..14]);
                var subscriptionStatusCode = BinaryPrimitives.ReadUInt16BigEndian(record[14..16]);

                var channelData = new RxChannelData
                {
                    ChannelNumber  = channelNumber,
                    RxChannelStatusCode = rxChannelStatusCode,
                    SubscriptionStatusCode = subscriptionStatusCode,
                };
                channels.Add(channelData);
            }
        }
        return channels;
    }

    public static List<RxSubscriptionData>? ParseSubscriptionResponse(byte[] data)
        {
            var reader = new BinaryReader(new MemoryStream(data));
            var proto = BinaryPrimitives.ReadUInt16BigEndian(reader.ReadBytes(2));
            var totalLength = BinaryPrimitives.ReadUInt16BigEndian(reader.ReadBytes(2));
            if (totalLength < 0x12)
            {
                return null;
            }

            var seqNum = BinaryPrimitives.ReadUInt16BigEndian(reader.ReadBytes(2));
            var opCode = BinaryPrimitives.ReadUInt16BigEndian(reader.ReadBytes(2));
            if (opCode != 0x3400)
            {
                return null;
            }

            // random unknown data
            reader.ReadBytes(8);
            var totalChannels = reader.ReadByte();
            var totalChannels2 = reader.ReadByte();
            if (totalChannels != totalChannels2)
            {
                Log.Warning("ParseSubscriptionResponse(): Weird edge case {TotalChannels} != {TotalChannels2}", totalChannels, totalChannels2);
                Log.Warning("Hex dump: {HexDump}", Convert.ToHexString(data));
            }

            var channelData = new List<RxSubscriptionData>();

            var channelIndices = new ushort[totalChannels];

            for (var i = 0; i < totalChannels; i++)
            {
                channelIndices[i] = BinaryPrimitives.ReadUInt16BigEndian(reader.ReadBytes(2));
            }

            var dataSpan = data.AsSpan();

            for (var i = 0; i < totalChannels; i++)
            {
                reader.BaseStream.Position = channelIndices[i];
                
                // vendor id or dante chip type
                var recordLength1 = reader.ReadByte();
                var recordLength2 = reader.ReadByte();
                
                var channelNumber = BinaryPrimitives.ReadUInt16BigEndian(reader.ReadBytes(2));
                
                // unknown constant 3
                reader.ReadBytes(4);
                
                // channel number again
                reader.ReadBytes(2);
                
                // unknown zero constant
                reader.ReadBytes(4);
                
                // unknown constant (0xE)
                reader.ReadBytes(2);
                
                // unknown zero constant
                reader.ReadBytes(4);
                
                var currentNameIndex = BinaryPrimitives.ReadUInt16BigEndian(reader.ReadBytes(2));

                // unknown zeros
                reader.ReadBytes(4);

                if (recordLength1 == 0x16)
                {
                    reader.ReadBytes(2);
                    
                    // another name index?
                    reader.ReadBytes(2);
                }
                // unknown
                reader.ReadBytes(2);
                
                var defaultNameIndex = BinaryPrimitives.ReadUInt16BigEndian(reader.ReadBytes(2));
                
                // unknown
                reader.ReadBytes(12);

                var txDeviceNameIndex = BinaryPrimitives.ReadUInt16BigEndian(reader.ReadBytes(2));
                var txChannelNameIndex = BinaryPrimitives.ReadUInt16BigEndian(reader.ReadBytes(2));

                // unknown zeros
                reader.ReadBytes(2);

                var totalConnections = reader.ReadByte();
                var activeConnections = reader.ReadByte();

                // always seems to be '02 02'
                reader.ReadBytes(2);

                // zeros
                reader.ReadBytes(2);

                var currentName = ReadNullTerminatedString(dataSpan[currentNameIndex..]);
                var defaultName = ReadNullTerminatedString(dataSpan[defaultNameIndex..]);
                string? txChannelName = null;
                string? txDeviceName = null;
                if (txChannelNameIndex != 0 && txDeviceNameIndex != 0)
                {
                    txChannelName = ReadNullTerminatedString(dataSpan[txChannelNameIndex..]);
                    txDeviceName = ReadNullTerminatedString(dataSpan[txDeviceNameIndex..]);
                }

                RxSubscriptionData rxData = new()
                {
                    ActiveConnections = activeConnections,
                    SupportedConnections = totalConnections,
                    ChannelNumber = channelNumber,
                    CurrentChannelName = currentName,
                    DefaultChannelName = defaultName,
                    TxChannelName = txChannelName,
                    TxDeviceName = txDeviceName,
                };
                channelData.Add(rxData);
                #if DEBUG
                Log.Debug("subscriptionResponse ch {ChannelNumber}: {JsonDump}", i, JsonSerializer.Serialize(rxData));
                #endif
            }

            return channelData;
        }
    }