using System;
using StoneshardMP.Memory;

namespace StoneshardMP.Net.Packets;

// The area owner's ground effects, to followers: a compressed JSON array of GroundEffectState.
public readonly record struct GroundEffectsPacket(string Place, byte[] Data) : IPacket
{
    public const byte PacketId = 45;
    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer)
    {
        writer.Write(Place);
        writer.Write(Data.Length);
        writer.Write((ReadOnlySpan<byte>)Data);
    }

    public static GroundEffectsPacket Read(ref SpanReadWrite reader) => new(
        Place: reader.ReadString(),
        Data: JoinCompression.ReadBlob(ref reader));
}
