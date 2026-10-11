using StoneshardMP.Memory;
using StoneshardMP.Net.Packets;

namespace StoneshardMP.Features.Join;

// Host -> a joining client: its save - the host's world with its character - as compressed JSON (JoinCompression).
public readonly record struct JoinWorldPacket(byte[] Save) : IPacket
{
    public const byte PacketId = 16;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer)
    {
        writer.Write(Save.Length);
        writer.Write((System.ReadOnlySpan<byte>)Save);
    }

    public static JoinWorldPacket Read(ref SpanReadWrite reader) => new(Save: JoinCompression.ReadBlob(ref reader));
}
