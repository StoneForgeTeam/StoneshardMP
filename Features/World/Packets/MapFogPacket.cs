using StoneshardMP.Memory;
using StoneshardMP.Net.Packets;

namespace StoneshardMP.Features.World;

/// <summary>To everyone: world-map tiles the sender has uncovered (global.globalmapFogGrid), as x, y pairs - those just
/// uncovered, or all of them when someone comes into the world. Each game uncovers those it hasn't.</summary>
public readonly record struct MapFogPacket(ushort[] Cells) : IPacket
{
    public const byte PacketId = 73;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer)
    {
        writer.Write(Cells.Length);
        foreach (ushort cell in Cells)
            writer.Write(cell);
    }

    public static MapFogPacket Read(ref SpanReadWrite reader)
    {
        int count = reader.Read<int>();
        var cells = new ushort[count];
        for (int i = 0; i < count; i++)
            cells[i] = reader.Read<ushort>();
        return new MapFogPacket(cells);
    }
}
