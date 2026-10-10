using StoneshardMP.Memory;

namespace StoneshardMP.Net.Packets;

/// <summary>The place's owner to everyone there: the traps players have set in it (Place: AreaOwnership's), one a line -
/// "object|x|y|armed|duration" (armed 1 or 0; duration, the uses it has left) - as they are in the owner's game.</summary>
public readonly record struct PlacedTrapsPacket(string Place, string Traps) : IPacket
{
    public const byte PacketId = 65;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer)
    {
        writer.Write(Place);
        writer.Write(Traps);
    }

    public static PlacedTrapsPacket Read(ref SpanReadWrite reader) => new(reader.ReadString(), reader.ReadString());
}
