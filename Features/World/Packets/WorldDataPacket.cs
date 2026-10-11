using StoneshardMP.Memory;
using StoneshardMP.Net.Packets;

namespace StoneshardMP.Features.World;

// A piece of the shared world (WorldSync), from a game in the world with that seed: a location's saved state, a
// world-map tile (its areas' seeds, its dungeon's layout), the weather - or a player asking the host for a copy of
// everything (CopyRequest, no data). The data is compressed JSON (JoinCompression).
public readonly record struct WorldDataPacket(WorldData Kind, double Seed, byte[] Data) : IPacket
{
    public const byte PacketId = 20;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer)
    {
        writer.Write((byte)Kind);
        writer.Write(Seed);
        writer.Write(Data.Length);
        writer.Write((System.ReadOnlySpan<byte>)Data);
    }

    public static WorldDataPacket Read(ref SpanReadWrite reader) => new(
        Kind: (WorldData)reader.ReadByte(),
        Seed: reader.ReadDouble(),
        Data: JoinCompression.ReadBlob(ref reader));
}
