using StoneshardMP.Memory;

namespace StoneshardMP.Net.Packets;

// A container in the world in a place players share (ChestSync), by its key (ChestSync.KeyOf), for that place
// (PlayerState.Place): the sender has opened it, or closed it with these contents, or the place's owner says what's in it.
// The data is the contents as compressed JSON (JoinCompression: Containers.ContentsJson); none for Opened.
public readonly record struct ChestPacket(string Place, string Key, ChestKind Kind, byte[] Data) : IPacket
{
    public const byte PacketId = 37;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer)
    {
        writer.Write(Place);
        writer.Write(Key);
        writer.Write((byte)Kind);
        writer.Write(Data.Length);
        writer.Write((System.ReadOnlySpan<byte>)Data);
    }

    public static ChestPacket Read(ref SpanReadWrite reader) => new(
        Place: reader.ReadString(),
        Key: reader.ReadString(),
        Kind: (ChestKind)reader.ReadByte(),
        Data: JoinCompression.ReadBlob(ref reader));
}
