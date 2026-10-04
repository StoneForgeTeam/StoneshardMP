using StoneshardMP.Memory;

namespace StoneshardMP.Net.Packets;

/// <summary>Client to host: why this player needs turns (TurnReason, 0: doesn't), and the last round they acted in
/// (-1: none) - sent when either changes, and every second (one lost would hold the round up).</summary>
public readonly record struct TurnStatusPacket(byte Reason, int ActedRound) : IPacket
{
    public const byte PacketId = 30;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer)
    {
        writer.Write(Reason);
        writer.Write(ActedRound);
    }

    public static TurnStatusPacket Read(ref SpanReadWrite reader) => new(
        Reason: reader.ReadByte(),
        ActedRound: reader.ReadInt32());
}
