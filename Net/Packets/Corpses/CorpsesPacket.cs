using System;
using StoneshardMP.Memory;

namespace StoneshardMP.Net.Packets;

// Request has no payload; Snapshot is the owner's compressed corpse save data.
public readonly record struct CorpsesPacket(string Place, bool Request, byte[] Data) : IPacket
{
    public const byte PacketId = 42;
    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer)
    {
        writer.Write(Place);
        writer.Write(Request);
        writer.Write(Data.Length);
        writer.Write((ReadOnlySpan<byte>)Data);
    }

    public static CorpsesPacket Read(ref SpanReadWrite reader) => new(
        Place: reader.ReadString(),
        Request: reader.ReadBoolean(),
        Data: JoinCompression.ReadBlob(ref reader));
}
