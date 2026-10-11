using StoneshardMP.Memory;
using StoneshardMP.Net.Packets;

namespace StoneshardMP.Features.Loot;

// Ground loot in a place two players share (LootSync), for that place (PlayerState.Place): the owner's snapshot and
// changes, and a follower's pickups, drops, and asking for a snapshot. The data is compressed JSON (JoinCompression).
public readonly record struct LootPacket(LootKind Kind, string Place, byte[] Data) : IPacket
{
    public const byte PacketId = 21;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer)
    {
        writer.Write((byte)Kind);
        writer.Write(Place);
        writer.Write(Data.Length);
        writer.Write((System.ReadOnlySpan<byte>)Data);
    }

    public static LootPacket Read(ref SpanReadWrite reader) => new(
        Kind: (LootKind)reader.ReadByte(),
        Place: reader.ReadString(),
        Data: JoinCompression.ReadBlob(ref reader));
}
