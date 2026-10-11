using System;
using StoneshardMP.Memory;
using StoneshardMP.Net.Packets;

namespace StoneshardMP.Features.Caravan;

/// <summary>The caravan's state (Stash false: caravanDataMap - its tile, cooldown, upgrades, followers, events, camp,
/// appearance) or its storage (Stash true: its four tabs, caravanStashDataList1-4, as a JSON array of four), as JSON,
/// compressed. The host's to everyone; a client's to the host when its own action changed it (the host takes it, and
/// sends it on).</summary>
public readonly record struct CaravanDataPacket(bool Stash, byte[] Data) : IPacket
{
    public const byte PacketId = 59;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer)
    {
        writer.Write(Stash);
        writer.Write(Data.Length);
        writer.Write((ReadOnlySpan<byte>)Data);
    }

    public static CaravanDataPacket Read(ref SpanReadWrite reader) => new(reader.ReadBoolean(), JoinCompression.ReadBlob(ref reader));
}
