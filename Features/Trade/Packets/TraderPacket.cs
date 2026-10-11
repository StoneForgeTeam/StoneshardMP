using System;
using StoneshardMP.Memory;
using StoneshardMP.Net.Packets;

namespace StoneshardMP.Features.Trade;

/// <summary>A trader's stock as a player's trade with it ended: the NPC (its id_name, in the world data of the settlement
/// at TileX, TileY) and its trade state - what it sells (trade_list, its gold there too, as a moneybag), the one-of-a-kind
/// items already bought from it (singular_stock), its free slots and restock flags and times - as JSON, compressed. To
/// everyone (a client's goes through the host); each game takes it in place of its own.</summary>
public readonly record struct TraderPacket(short TileX, short TileY, string Npc, byte[] Data) : IPacket
{
    public const byte PacketId = 63;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer)
    {
        writer.Write(TileX);
        writer.Write(TileY);
        writer.Write(Npc);
        writer.Write(Data.Length);
        writer.Write((ReadOnlySpan<byte>)Data);
    }

    public static TraderPacket Read(ref SpanReadWrite reader)
        => new(reader.Read<short>(), reader.Read<short>(), reader.ReadString(), JoinCompression.ReadBlob(ref reader));
}
