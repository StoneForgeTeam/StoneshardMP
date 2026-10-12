using StoneshardMP.Memory;
using StoneshardMP.Net.Packets;

namespace StoneshardMP.Features.Doors;

/// <summary>A building's entrance in the sender's place (its key: EntranceLocks.KeyOf) is unlocked - with its key, or
/// picked - so it's unlocked in every game there.</summary>
public readonly record struct EntranceUnlockedPacket(string Place, string Key) : IPacket
{
    public const byte PacketId = 70;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer)
    {
        writer.Write(Place);
        writer.Write(Key);
    }

    public static EntranceUnlockedPacket Read(ref SpanReadWrite reader) => new(
        Place: reader.ReadString(),
        Key: reader.ReadString());
}
