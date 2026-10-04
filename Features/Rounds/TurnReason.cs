namespace StoneshardMP.Features.Rounds;

/// <summary>Why a player needs turn-based play (TurnRounds): none, in combat, bleeding to death, on fire. (The values go
/// on the wire.)</summary>
public enum TurnReason : byte
{
    None = 0,
    Combat = 1,
    BleedingOut = 2,
    OnFire = 3,
}
