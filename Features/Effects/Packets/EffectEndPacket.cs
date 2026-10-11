using StoneshardMP.Memory;
using StoneshardMP.Net.Packets;

namespace StoneshardMP.Features.Effects;

public readonly record struct EffectEndPacket(int InstanceId) : IPacket
{
    public const byte PacketId = 7;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer) => writer.Write(InstanceId);

    public static EffectEndPacket Read(ref SpanReadWrite reader) => new(
        InstanceId: reader.ReadInt32());
}
