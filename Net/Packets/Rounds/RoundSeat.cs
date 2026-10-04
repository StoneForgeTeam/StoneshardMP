namespace StoneshardMP.Net.Packets;

/// <summary>One player's place in a round (RoundPacket).</summary>
public readonly record struct RoundSeat(int Slot, byte Reason, bool Acted);
