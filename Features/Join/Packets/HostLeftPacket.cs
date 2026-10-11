using StoneshardMP.Memory;
using StoneshardMP.Net.Packets;

namespace StoneshardMP.Features.Join;

// Host -> everyone: I've left my world (back on the main menu) - everyone in it goes back to theirs, to join again
// when I play again.
public readonly record struct HostLeftPacket : IPacket
{
    public const byte PacketId = 19;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer) { }

    public static HostLeftPacket Read(ref SpanReadWrite reader) => new();
}
