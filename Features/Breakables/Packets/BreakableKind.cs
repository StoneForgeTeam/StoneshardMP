
namespace StoneshardMP.Features.Breakables;

/// <summary>What a BreakablePacket says about something breakable.</summary>
public enum BreakableKind : byte
{
    // It lost this much HP at the sender.
    Damage = 0,
    // It broke at the sender.
    Broken = 1,
    // The place's owner's: this much HP is left of it.
    State = 2,
}
