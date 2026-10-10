using StoneshardMP.Memory;

namespace StoneshardMP.Net.Packets;

// Follower to owner: a ground effect our player made (Added), or a request for the owner's whole list (Request).
public readonly record struct GroundEffectChangePacket(string Place, GroundEffectChangeKind Kind, GroundEffectState Effect) : IPacket
{
    public const byte PacketId = 46;
    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer)
    {
        writer.Write(Place);
        writer.Write((byte)Kind);
        writer.Write(Effect.Object ?? "");
        writer.Write(Effect.X);
        writer.Write(Effect.Y);
        writer.Write(Effect.Duration);
        writer.Write(Effect.Executing ? (byte)1 : (byte)0);
        writer.Write(Effect.Activation);
        writer.Write(Effect.Active ? (byte)1 : (byte)0);
    }

    public static GroundEffectChangePacket Read(ref SpanReadWrite reader) => new(
        Place: reader.ReadString(),
        Kind: (GroundEffectChangeKind)reader.ReadByte(),
        Effect: new GroundEffectState(
            Object: reader.ReadString(),
            X: reader.ReadDouble(),
            Y: reader.ReadDouble(),
            Duration: reader.ReadDouble(),
            Executing: reader.ReadByte() != 0,
            Activation: reader.ReadDouble(),
            Active: reader.ReadByte() != 0));
}

public enum GroundEffectChangeKind : byte
{
    Added,
    Request,
}
