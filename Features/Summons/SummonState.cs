namespace StoneshardMP.Features.Summons;

/// <summary>One of a player's summons as their game has it: its id there (the instance's), its object, where it is (room
/// position), its health, the way it faces, and how it looks - Sprite: its sprite's name if it's one of the game's own (a
/// Mana Crystal's), "" if it's a copy of its caster's (an Astral Phantasm: a duplicate of the caster's composited sprite,
/// made in their game only) - with its frame and alpha, and its name for the hover.</summary>
public sealed record SummonState(long Id, string Object, double X, double Y, double Health, double MaxHealth, bool Flip,
    string Sprite, double Frame, double Alpha, string Name);
