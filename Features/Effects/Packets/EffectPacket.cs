using StoneshardMP.Memory;
using StoneshardMP.Net.Packets;

namespace StoneshardMP.Features.Effects;

public readonly record struct EffectPacket(
    int InstanceId,
    int Sprite,
    float X,
    float Y,
    int Depth,
    float ScaleX,
    float ScaleY,
    float Angle,
    float Frame,
    float Alpha,
    int Blend) : IPacket
{
    public const byte PacketId = 6;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer)
    {
        writer.Write(InstanceId);
        writer.Write(Sprite);
        writer.Write(X);
        writer.Write(Y);
        writer.Write(Depth);
        writer.Write(ScaleX);
        writer.Write(ScaleY);
        writer.Write(Angle);
        writer.Write(Frame);
        writer.Write(Alpha);
        writer.Write(Blend);
    }

    public static EffectPacket Read(ref SpanReadWrite reader) => new(
        InstanceId: reader.ReadInt32(),
        Sprite: reader.ReadInt32(),
        X: reader.ReadSingle(),
        Y: reader.ReadSingle(),
        Depth: reader.ReadInt32(),
        ScaleX: reader.ReadSingle(),
        ScaleY: reader.ReadSingle(),
        Angle: reader.ReadSingle(),
        Frame: reader.ReadSingle(),
        Alpha: reader.ReadSingle(),
        Blend: reader.ReadInt32());
}
