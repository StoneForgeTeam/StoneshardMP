using StoneshardMP.Memory;
using StoneshardMP.Net.Packets;

namespace StoneshardMP.Features.Dev;

/// <summary>Dev tools: how a player sees the units of their place - each by the owner's sync id, with its object, cell and
/// health (the owner: its real units; a follower: its copies bound to them).</summary>
public readonly record struct UnitsViewPacket(string Place, int Nonce, UnitView[] Units) : IPacket
{
    public const byte PacketId = 51;
    private const int MaxUnits = 2000;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer)
    {
        writer.Write(Place);
        writer.Write(Nonce);
        writer.Write((ushort)Units.Length);
        foreach (var unit in Units)
        {
            writer.Write(unit.SyncId);
            writer.Write(unit.Object);
            writer.Write(unit.CellX);
            writer.Write(unit.CellY);
            writer.Write(unit.Health);
        }
    }

    public static UnitsViewPacket Read(ref SpanReadWrite reader)
    {
        string place = reader.ReadString();
        int nonce = reader.Read<int>();
        int count = reader.Read<ushort>();
        if (count > MaxUnits)
            throw new System.IO.InvalidDataException("Too many units");
        var units = new UnitView[count];
        for (int i = 0; i < count; i++)
            units[i] = new UnitView(reader.Read<long>(), reader.ReadString(), reader.Read<short>(), reader.Read<short>(), reader.Read<float>());
        return new(place, nonce, units);
    }
}
