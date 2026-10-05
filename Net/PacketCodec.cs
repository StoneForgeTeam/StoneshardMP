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
            PartyPacket.PacketId => PartyPacket.Read(ref reader),
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
            WorldReloadPacket.PacketId => WorldReloadPacket.Read(ref reader),
            WorldDataPacket.PacketId => WorldDataPacket.Read(ref reader),
            SharedCallPacket.PacketId => SharedCallPacket.Read(ref reader),
            QuestItemsPacket.PacketId => QuestItemsPacket.Read(ref reader),
            ContractPacket.PacketId => ContractPacket.Read(ref reader),
            LootPacket.PacketId => LootPacket.Read(ref reader),
            UnitHitPacket.PacketId => UnitHitPacket.Read(ref reader),
            EnemyAttackPacket.PacketId => EnemyAttackPacket.Read(ref reader),
            UnitKilledPacket.PacketId => UnitKilledPacket.Read(ref reader),
            UnitMovedPacket.PacketId => UnitMovedPacket.Read(ref reader),
            UnitEffectPacket.PacketId => UnitEffectPacket.Read(ref reader),
            TurnStatusPacket.PacketId => TurnStatusPacket.Read(ref reader),
            RoundPacket.PacketId => RoundPacket.Read(ref reader),
            DoorPacket.PacketId => DoorPacket.Read(ref reader),
            AreaOwnerPacket.PacketId => AreaOwnerPacket.Read(ref reader),
            MapMarkersPacket.PacketId => MapMarkersPacket.Read(ref reader),
            ChestPacket.PacketId => ChestPacket.Read(ref reader),
            StashPacket.PacketId => StashPacket.Read(ref reader),
            SlotsPacket.PacketId => SlotsPacket.Read(ref reader),
            _ => throw new InvalidDataException("Unknown packet ID")
        };
        if (reader.Position != reader.Length) throw new InvalidDataException("Trailing packet data");
        return packet;
    }
}

