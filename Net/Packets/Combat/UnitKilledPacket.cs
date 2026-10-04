using StoneshardMP.Memory;

namespace StoneshardMP.Net.Packets;

/// <summary>Host to client: one of the host's units (its sync id) the client hit has been killed - its XP is the client's too.</summary>
public readonly record struct UnitKilledPacket(long UnitId) : IPacket
{
    public const byte PacketId = 29;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer) => writer.Write(UnitId);

    public static UnitKilledPacket Read(ref SpanReadWrite reader) => new(UnitId: reader.Read<long>());
}
