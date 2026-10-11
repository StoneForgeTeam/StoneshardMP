using StoneshardMP.Memory;
using StoneshardMP.Net.Packets;

namespace StoneshardMP.Features.Quests;

// Which of the quest triggers' checks this player passes - quest items held ("o_inv_plane"), 1000 gold ("gold1000"),
// the thief's wine ("wine") - joined by ",". Sent when one changes and every couple of seconds (QuestSync).
public readonly record struct QuestItemsPacket(string Have) : IPacket
{
    public const byte PacketId = 23;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer) => writer.Write(Have);

    public static QuestItemsPacket Read(ref SpanReadWrite reader) => new(Have: reader.ReadString());
}
