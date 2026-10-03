using StoneshardMP.Memory;

namespace StoneshardMP.Net.Packets;

public readonly record struct EffectEndPacket(int InstanceId) : IPacket
{
    public const byte PacketId = 7;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer) => writer.Write(InstanceId);

    public static EffectEndPacket Read(ref SpanReadWrite reader) => new(
        InstanceId: reader.ReadInt32());
}
