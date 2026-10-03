using System.IO;
using StoneshardMP.Memory;

namespace StoneshardMP.Net.Packets;

public readonly record struct WelcomePacket(byte Slot, JoinedPacket[] Players) : IPacket
{
    public const byte PacketId = 1;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer)
    {
        writer.Write(Slot);
        writer.Write((byte)Players.Length);
        foreach (var player in Players)
            player.Write(ref writer);
    }

    public static WelcomePacket Read(ref SpanReadWrite reader)
    {
        byte slot = reader.ReadByte();
        byte count = reader.ReadByte();

        if (slot < 1 || slot >= 8 || count < 1 || count >= 8)
            throw new InvalidDataException("Invalid welcome roster");
        var players = new JoinedPacket[count];
        for (int i = 0; i < count; i++)
            players[i] = JoinedPacket.Read(ref reader);
        return new(Slot: slot, Players: players);
    }
}
