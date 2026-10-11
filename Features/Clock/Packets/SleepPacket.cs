using StoneshardMP.Memory;
using StoneshardMP.Net.Packets;

namespace StoneshardMP.Features.Clock;

/// <summary>Host to everyone: the host is sleeping (Asleep: its sleep began - fade out) or awake again (not: fade back
/// in), with its clock as it is (GameClock.Snapshot: "seconds|minutes|hours|days|months") - the hours slept, ours too.
/// Waking, where it slept (its place) and the vigor its sleep gave it (o_b_fresh: the turns, and the most it can stack to;
/// 0: none - a poor bed, nightmares): a client in the same place gets the same. Also sent, not Asleep and without a fade
/// before it, when its time jumped another way (a fast travel).</summary>
public readonly record struct SleepPacket(bool Asleep, string Clock, string Place, double Vigor, double VigorMax) : IPacket
{
    public const byte PacketId = 55;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer)
    {
        writer.Write(Asleep);
        writer.Write(Clock);
        writer.Write(Place);
        writer.Write(Vigor);
        writer.Write(VigorMax);
    }

    public static SleepPacket Read(ref SpanReadWrite reader)
        => new(reader.ReadBoolean(), reader.ReadString(), reader.ReadString(), reader.ReadDouble(), reader.ReadDouble());
}
