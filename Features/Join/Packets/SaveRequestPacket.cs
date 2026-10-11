using StoneshardMP.Memory;
using StoneshardMP.Net.Packets;

namespace StoneshardMP.Features.Join;

// Host -> everyone: save your game now (the host is saving and leaving - each client's save sends its character to
// the host, JoinCharacterPacket, so the host's save holds everyone's).
public readonly record struct SaveRequestPacket : IPacket
{
    public const byte PacketId = 18;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer) { }

    public static SaveRequestPacket Read(ref SpanReadWrite reader) => new();
}
