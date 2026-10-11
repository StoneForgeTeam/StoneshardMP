using System;

namespace StoneshardMP.Features.GroundEffects;

// A ground effect (a fire, an acid pool, a smoke or poison cloud, blood, lava): matched by its object's name and cell, as
// the game's location saves keep one (scr_locationRoomEntityMarksSaveDataGet) - no instance ids cross the wire.
// Duration: the turns it has left (-4: lasts); Executing: it's ending (its end animation playing); Activation and
// Active: a cloud's turns until it thickens, and whether it has (c_skill_aura_smoke).
public readonly record struct GroundEffectState(
    string Object, double X, double Y, double Duration, bool Executing, double Activation, bool Active)
{
    public string Key => $"{Object}|{Math.Floor(X / 26)}|{Math.Floor(Y / 26)}";
    public bool Valid => !string.IsNullOrEmpty(Object) && Object.Length <= 64 && double.IsFinite(X) && double.IsFinite(Y)
        && double.IsFinite(Duration) && double.IsFinite(Activation);
}
