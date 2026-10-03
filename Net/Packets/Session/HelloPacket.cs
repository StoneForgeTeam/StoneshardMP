using StoneshardMP.Memory;

namespace StoneshardMP.Net.Packets;

public readonly record struct HelloPacket(
    string Key,
    ushort Protocol,
    string Version,
    string Name) : IPacket
{
    public const byte PacketId = 12;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer)
    {
        writer.Write(Key);
        writer.Write(Protocol);
        writer.Write(Version);
        writer.Write(Name);
    }

    public static HelloPacket Read(ref SpanReadWrite reader) => new(
        Key: reader.ReadString(),
        Protocol: reader.ReadUInt16(),
        Version: reader.ReadString(),
        Name: reader.ReadString());
}
