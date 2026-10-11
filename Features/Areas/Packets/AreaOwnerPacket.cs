using StoneshardMP.Memory;
using StoneshardMP.Net.Packets;

namespace StoneshardMP.Features.Areas;

/// <summary>Host to a client: who runs the place the client is in (Place, its WorldMap place string), by slot - or
/// <see cref="Alone"/>, nobody else is there (the client runs it itself). Sent when it changes, and every second.</summary>
public readonly record struct AreaOwnerPacket(string Place, byte Owner) : IPacket
{
    public const byte PacketId = 35;
    public const byte Alone = 255;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer)
    {
        writer.Write(Place);
        writer.Write(Owner);
    }

    public static AreaOwnerPacket Read(ref SpanReadWrite reader) => new(
        Place: reader.ReadString(),
        Owner: reader.ReadByte());
}
