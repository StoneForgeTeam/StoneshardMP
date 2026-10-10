using StoneshardMP.Memory;

namespace StoneshardMP.Net.Packets;

/// <summary>To everyone: a player opened the caravan's storage (Open) or closed it - one at a time.</summary>
public readonly record struct CaravanStashUsePacket(bool Open) : IPacket
{
    public const byte PacketId = 60;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer) => writer.Write(Open);

    public static CaravanStashUsePacket Read(ref SpanReadWrite reader) => new(reader.ReadBoolean());
}
