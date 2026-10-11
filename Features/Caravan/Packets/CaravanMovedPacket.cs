using StoneshardMP.Memory;
using StoneshardMP.Net.Packets;

namespace StoneshardMP.Features.Caravan;

/// <summary>Host to everyone: the caravan moved from one world-map tile to another - players at its old camp come
/// along.</summary>
public readonly record struct CaravanMovedPacket(int FromX, int FromY, int ToX, int ToY) : IPacket
{
    public const byte PacketId = 61;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer)
    {
        writer.Write(FromX);
        writer.Write(FromY);
        writer.Write(ToX);
        writer.Write(ToY);
    }

    public static CaravanMovedPacket Read(ref SpanReadWrite reader)
        => new(reader.Read<int>(), reader.Read<int>(), reader.Read<int>(), reader.Read<int>());
}
