using StoneshardMP.Memory;
using StoneshardMP.Net.Packets;

namespace StoneshardMP.Features.Talk;

/// <summary>To everyone: what a player is doing with an NPC now, sent as it changes - nothing, a conversation, or a trade -
/// with which NPC (the area owner's sync id for it, AreaUnits; -1: none) in which place, and, for a conversation, what
/// their dialogue window shows: the NPC's line (the page of it shown), the player's last answer, and the answers to choose
/// from as their game made them (its random variants, its locks) - each with whether it can be pressed. Others use it to
/// keep that NPC to them, to show it in a listener's window, and for the speech clouds over them.</summary>
public readonly record struct TalkPacket(
    TalkKind Kind, string Place, long NpcId, string NpcName, string Text, string LastAnswer, string[] Answers, bool[] Pressable)
    : IPacket
{
    public const byte PacketId = 49;
    // (Bounds a malformed packet can't exceed.)
    private const int MaxAnswers = 32;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer)
    {
        writer.Write((byte)Kind);
        writer.Write(Place);
        writer.Write(NpcId);
        writer.Write(NpcName);
        writer.Write(Text);
        writer.Write(LastAnswer);
        writer.Write((byte)Answers.Length);
        for (int i = 0; i < Answers.Length; i++)
        {
            writer.Write(Answers[i]);
            writer.Write(Pressable[i]);
        }
    }

    public static TalkPacket Read(ref SpanReadWrite reader)
    {
        var kind = (TalkKind)reader.ReadByte();
        string place = reader.ReadString();
        long npc = reader.Read<long>();
        string name = reader.ReadString(), text = reader.ReadString(), last = reader.ReadString();
        int count = reader.ReadByte();
        if (count > MaxAnswers)
            throw new System.IO.InvalidDataException("Too many answers");
        var answers = new string[count];
        var pressable = new bool[count];
        for (int i = 0; i < count; i++)
        {
            answers[i] = reader.ReadString();
            pressable[i] = reader.ReadBoolean();
        }
        return new(kind, place, npc, name, text, last, answers, pressable);
    }
}
