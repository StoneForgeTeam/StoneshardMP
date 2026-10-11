using System;
using StoneshardMP.Memory;
using StoneshardMP.Net.Packets;

namespace StoneshardMP.Features.Summons;

/// <summary>To everyone: the summons a player has (Astral Phantasm, Mana Crystal...) in this place, as compressed JSON
/// of SummonState - the caster's game runs them; the others show a stand-in for each (SummonSync). An empty list: none.</summary>
public readonly record struct SummonsPacket(string Place, byte[] Data) : IPacket
{
    public const byte PacketId = 57;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer)
    {
        writer.Write(Place);
        writer.Write(Data.Length);
        writer.Write((ReadOnlySpan<byte>)Data);
    }

    public static SummonsPacket Read(ref SpanReadWrite reader) => new(reader.ReadString(), JoinCompression.ReadBlob(ref reader));
}
