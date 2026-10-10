using StoneshardMP.Memory;

namespace StoneshardMP.Net.Packets;

/// <summary>Follower to the place's owner: its own player made a noise - a step, a fight, something broken, a spell
/// (scr_noise_produce: how loud, at which cell, and which faction may hear it, "Any" for all) - played on the owner, from
/// the follower's stand-in, so the owner's units hear it.</summary>
public readonly record struct NoisePacket(float Power, short CellX, short CellY, string Faction) : IPacket
{
    public const byte PacketId = 62;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer)
    {
        writer.Write(Power);
        writer.Write(CellX);
        writer.Write(CellY);
        writer.Write(Faction);
    }

    public static NoisePacket Read(ref SpanReadWrite reader)
        => new(reader.Read<float>(), reader.Read<short>(), reader.Read<short>(), reader.ReadString());
}
