using System;
using StoneshardMP.Memory;
using StoneshardMP.Net.Packets;

namespace StoneshardMP.Features.Placeables;

// Compressed JSON array of PlacedObjectState, from the area owner to followers.
public readonly record struct PlaceablesPacket(string Place, byte[] Data) : IPacket
{
    public const byte PacketId = 43;
    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer)
    {
        writer.Write(Place);
        writer.Write(Data.Length);
        writer.Write((ReadOnlySpan<byte>)Data);
    }

    public static PlaceablesPacket Read(ref SpanReadWrite reader) => new(
        Place: reader.ReadString(),
        Data: JoinCompression.ReadBlob(ref reader));
}
