using StoneshardMP.Memory;

namespace StoneshardMP.Net.Packets;

// Host -> everyone: I'm loading a save - stay in the game, ask to join again, and load my world in place once it's up
// (with your character as that save has it).
public readonly record struct WorldReloadPacket : IPacket
{
    public const byte PacketId = 25;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer) { }

    public static WorldReloadPacket Read(ref SpanReadWrite reader) => new();
}
