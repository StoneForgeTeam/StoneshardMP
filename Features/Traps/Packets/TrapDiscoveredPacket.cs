using StoneshardMP.Memory;
using StoneshardMP.Net.Packets;

namespace StoneshardMP.Features.Traps;

/// <summary>To everyone in the place: a trap (by its object and position) was spotted - or, Disarmed, is spent: disarmed
/// by a player, or already sprung - so it's harmless in every game there.</summary>
public readonly record struct TrapDiscoveredPacket(string Place, string Key, bool Disarmed) : IPacket
{
    public const byte PacketId = 41;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer)
    {
        writer.Write(Place);
        writer.Write(Key);
        writer.Write(Disarmed);
    }

    public static TrapDiscoveredPacket Read(ref SpanReadWrite reader) => new(
        Place: reader.ReadString(),
        Key: reader.ReadString(),
        Disarmed: reader.ReadBoolean());
}
