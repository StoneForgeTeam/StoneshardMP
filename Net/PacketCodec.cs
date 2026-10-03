using System;
using System.IO;
using StoneshardMP.Memory;
using StoneshardMP.Net.Packets;
namespace StoneshardMP.Net;

public static class PacketCodec
{
    public const int MaxBytes = 1024 * 1024;
    public static byte[] Encode(IPacket packet, int from = 0, int to = Session.Everyone)
    {
        var writer = new SpanReadWrite();
        writer.Write(packet.Id); writer.Write((byte)from); writer.Write((byte)to);
        packet.Write(ref writer);
        if (writer.Position > MaxBytes) throw new InvalidDataException("Packet too large");
        return writer.GetBufferToPosition().ToArray();
    }
    public static IPacket Decode(ReadOnlySpan<byte> data, out byte from, out byte to)
    {
        if (data.Length < 3 || data.Length > MaxBytes) throw new InvalidDataException("Invalid packet length");
        var reader = new SpanReadWrite(data);
        byte id = reader.ReadByte(); from = reader.ReadByte(); to = reader.ReadByte();
        if (from >= 8 || (to >= 8 && to != 255)) throw new InvalidDataException("Invalid slot");
        IPacket packet = id switch
        {
            WelcomePacket.PacketId => WelcomePacket.Read(ref reader),
            JoinedPacket.PacketId => JoinedPacket.Read(ref reader),
            LeftPacket.PacketId => LeftPacket.Read(ref reader),
            StatePacket.PacketId => StatePacket.Read(ref reader),
            LookPacket.PacketId => LookPacket.Read(ref reader),
            EffectPacket.PacketId => EffectPacket.Read(ref reader),
            EffectEndPacket.PacketId => EffectEndPacket.Read(ref reader),
            ProfilePacket.PacketId => ProfilePacket.Read(ref reader),
            WorldTickPacket.PacketId => WorldTickPacket.Read(ref reader),
            WorldActionPacket.PacketId => WorldActionPacket.Read(ref reader),
            AreaUnitsPacket.PacketId => AreaUnitsPacket.Read(ref reader),
            HelloPacket.PacketId => HelloPacket.Read(ref reader),
            RejectedPacket.PacketId => RejectedPacket.Read(ref reader),
            JoinRequestPacket.PacketId => JoinRequestPacket.Read(ref reader),
            JoinReplyPacket.PacketId => JoinReplyPacket.Read(ref reader),
            JoinWorldPacket.PacketId => JoinWorldPacket.Read(ref reader),
            JoinCharacterPacket.PacketId => JoinCharacterPacket.Read(ref reader),
            SaveRequestPacket.PacketId => SaveRequestPacket.Read(ref reader),
            HostLeftPacket.PacketId => HostLeftPacket.Read(ref reader),
            _ => throw new InvalidDataException("Unknown packet ID")
        };
        if (reader.Position != reader.Length) throw new InvalidDataException("Trailing packet data");
        return packet;
    }
}

