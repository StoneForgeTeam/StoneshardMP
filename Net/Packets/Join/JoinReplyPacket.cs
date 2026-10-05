using StoneshardMP.Memory;

namespace StoneshardMP.Net.Packets;

// Host -> a client that asked to join: wait (not in a world yet), make a character (none here yet - the world's seed
// to make it in), or your save follows (JoinWorldPacket) - and the world slot it plays (WorldSlots).
public readonly record struct JoinReplyPacket(JoinReply Reply, double Seed, byte WorldSlot) : IPacket
{
    public const byte PacketId = 15;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer)
    {
        writer.Write((byte)Reply);
        writer.Write(Seed);
        writer.Write(WorldSlot);
    }

    public static JoinReplyPacket Read(ref SpanReadWrite reader) => new(
        Reply: (JoinReply)reader.ReadByte(),
        Seed: reader.ReadDouble(),
        WorldSlot: reader.ReadByte());
}

public enum JoinReply : byte
{
    Wait = 0,
    MakeCharacter = 1,
    WorldFollows = 2,
}
