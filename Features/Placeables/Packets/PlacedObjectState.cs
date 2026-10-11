using System;

namespace StoneshardMP.Features.Placeables;

// No instance or asset IDs cross the wire. Match an object by its name and tile.
public readonly record struct PlacedObjectState(
    string Object, double X, double Y, double Timestamp, double Health, double Duration, int Caster)
{
    public string Key => $"{Object}|{Math.Floor(X / 26)}|{Math.Floor(Y / 26)}";
    public bool Spell => Object is "o_runic_boulder" or "o_stone_spikes_instance";
    public bool Supported => Object is "o_campbed_crafted" or "o_campfire_crafted"
        or "o_runic_boulder" or "o_stone_spikes_instance";
    public bool Valid => Supported && double.IsFinite(X) && double.IsFinite(Y)
        && double.IsFinite(Timestamp) && double.IsFinite(Health) && double.IsFinite(Duration)
        && Caster >= 0 && Caster < 8;
}
