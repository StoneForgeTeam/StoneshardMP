using StoneshardMP.Memory;
using StoneshardMP.Net.Packets;

namespace StoneshardMP.Features.Caravan;

/// <summary>To everyone: the sender rode Verren's cart from a place (its key: WorldMap.Place) to a settlement (the game's
/// location name: "BrynnSuburbs") - the others in that place ride with it.</summary>
public readonly record struct CartTravelPacket(string From, string Settlement) : IPacket
{
    public const byte PacketId = 71;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer)
    {
        writer.Write(From);
        writer.Write(Settlement);
    }

    public static CartTravelPacket Read(ref SpanReadWrite reader) => new(
        From: reader.ReadString(),
        Settlement: reader.ReadString());
}
