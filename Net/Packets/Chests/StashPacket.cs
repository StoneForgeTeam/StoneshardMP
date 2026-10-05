using StoneshardMP.Memory;

namespace StoneshardMP.Net.Packets;

// Client -> host: what's in the client's own stash in a chest (PersonalStash), by the chest's id (its place and
// position), for the host to keep as theirs. The data is the items as compressed JSON (JoinCompression:
// Containers.ContentsJson).
public readonly record struct StashPacket(string Chest, byte[] Data) : IPacket
{
    public const byte PacketId = 38;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer)
    {
        writer.Write(Chest);
        writer.Write(Data.Length);
        writer.Write((System.ReadOnlySpan<byte>)Data);
    }

    public static StashPacket Read(ref SpanReadWrite reader) => new(
        Chest: reader.ReadString(),
        Data: JoinCompression.ReadBlob(ref reader));
}
