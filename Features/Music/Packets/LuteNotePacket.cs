using StoneshardMP.Memory;
using StoneshardMP.Net.Packets;

namespace StoneshardMP.Features.Music;

/// <summary>To everyone: the sender played a note on their lute - the note's sound (its asset index), heard from their
/// character in the others' games.</summary>
public readonly record struct LuteNotePacket(int Sound) : IPacket
{
    public const byte PacketId = 72;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer) => writer.Write(Sound);

    public static LuteNotePacket Read(ref SpanReadWrite reader) => new(Sound: reader.Read<int>());
}
