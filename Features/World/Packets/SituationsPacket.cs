using System;
using StoneshardMP.Memory;
using StoneshardMP.Net.Packets;

namespace StoneshardMP.Features.World;

/// <summary>The host's to everyone: every village's situations (its world-map tile's situationsDataMap - a fair, a
/// pilgrimage, a pest, its economy), as a JSON object of "x_y" to that map, compressed.</summary>
public readonly record struct SituationsPacket(byte[] Data) : IPacket
{
    public const byte PacketId = 69;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer)
    {
        writer.Write(Data.Length);
        writer.Write((ReadOnlySpan<byte>)Data);
    }

    public static SituationsPacket Read(ref SpanReadWrite reader) => new(JoinCompression.ReadBlob(ref reader));
}
