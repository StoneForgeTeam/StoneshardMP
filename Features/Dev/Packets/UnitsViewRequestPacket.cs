using StoneshardMP.Memory;
using StoneshardMP.Net.Packets;

namespace StoneshardMP.Features.Dev;

/// <summary>Dev tools: asks a player in the same place for how they see its units (UnitsViewPacket), to compare.</summary>
public readonly record struct UnitsViewRequestPacket(string Place, int Nonce) : IPacket
{
    public const byte PacketId = 50;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer)
    {
        writer.Write(Place);
        writer.Write(Nonce);
    }

    public static UnitsViewRequestPacket Read(ref SpanReadWrite reader) => new(reader.ReadString(), reader.Read<int>());
}
