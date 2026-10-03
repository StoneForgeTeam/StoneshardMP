using StoneshardMP.Memory;

namespace StoneshardMP.Net.Packets;

// Client -> host: this player's character (its save sections), compressed JSON, for the host to keep in its world.
// Sent on each of the client's saves.
public readonly record struct JoinCharacterPacket(string Name, byte[] Character) : IPacket
{
    public const byte PacketId = 17;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer)
    {
        writer.Write(Name);
        writer.Write(Character.Length);
        writer.Write((System.ReadOnlySpan<byte>)Character);
    }

    public static JoinCharacterPacket Read(ref SpanReadWrite reader) => new(
        Name: reader.ReadString(),
        Character: JoinCompression.ReadBlob(ref reader));
}
