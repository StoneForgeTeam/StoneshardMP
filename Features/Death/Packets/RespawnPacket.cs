using StoneshardMP.Memory;
using StoneshardMP.Net.Packets;

namespace StoneshardMP.Features.Death;

/// <summary>Client to host: we died - our world slot's character goes back to its checkpoint (the character we were at the
/// host's last save, where the host saved), for the world we load next: now (Rejoin - the Respawn button), or the next
/// time we join (Disconnect).</summary>
public readonly record struct RespawnPacket(bool Rejoin) : IPacket
{
    public const byte PacketId = 52;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer) => writer.Write(Rejoin);

    public static RespawnPacket Read(ref SpanReadWrite reader) => new(reader.ReadBoolean());
}
