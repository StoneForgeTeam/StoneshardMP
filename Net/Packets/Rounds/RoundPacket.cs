using StoneshardMP.Memory;

namespace StoneshardMP.Net.Packets;

/// <summary>Host to everyone: the shared round in the host's place - whether rounds are on, the round's number, and
/// each player in it in turn order (their slot, why they need turns, whether they've acted this round). Whose turn it
/// is: the first who hasn't acted; once all have, the enemies'.</summary>
public readonly record struct RoundPacket(bool Active, int Round, RoundSeat[] Seats) : IPacket
{
    public const byte PacketId = 31;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer)
    {
        writer.Write(Active);
        writer.Write(Round);
        writer.Write((byte)Seats.Length);
        foreach (var seat in Seats)
        {
            writer.Write((byte)seat.Slot);
            writer.Write(seat.Reason);
            writer.Write(seat.Acted);
        }
    }

    public static RoundPacket Read(ref SpanReadWrite reader)
    {
        bool active = reader.ReadBoolean();
        int round = reader.ReadInt32();
        var seats = new RoundSeat[reader.ReadByte()];
        for (int i = 0; i < seats.Length; i++)
            seats[i] = new RoundSeat(reader.ReadByte(), reader.ReadByte(), reader.ReadBoolean());
        return new RoundPacket(active, round, seats);
    }
}
