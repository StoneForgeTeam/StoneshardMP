using System;
using System.Globalization;
using StoneForge;

namespace StoneshardMP.Net;

/// <summary>The light a player carries - a lit torch, lantern or candelabrum - as the game makes it: the on-unit effects
/// it puts on the player as they're equipped or lit (scr_playerObjectEffectsUpdate: its effects_objects_ids_array), each
/// light one with a spotlight (its "light": radius in cells - a torch's and a lantern's 9 - its colour, a tinted
/// lantern's, and its brightness). Their stand-in in the others' games gets the same, so their light lights the place
/// there too. Radius 0: no light.</summary>
public readonly record struct PlayerLight(double Radius, int Colour, double Alpha)
{
    public bool Lit => Radius > 0;

    public override string ToString() => string.Join(":", Radius.ToString(CultureInfo.InvariantCulture),
        Colour.ToString(CultureInfo.InvariantCulture), Alpha.ToString(CultureInfo.InvariantCulture));

    public static PlayerLight Parse(string text)
    {
        string[] p = text.Split(':');
        return p.Length == 3
            && double.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double radius)
            && int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int colour)
            && double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double alpha)
            ? new PlayerLight(radius, colour, alpha) : default;
    }

    /// <summary>Our player's strongest light (none if they carry none lit).</summary>
    public static PlayerLight Of(Instance player)
    {
        if (player.Get("effects_objects_ids_array").AsArray is not { } effects)
            return default;
        PlayerLight best = default;
        using (effects)
            foreach (GmValue value in effects)
            {
                Instance effect = Instance.Of(value), light = effect.Exists ? Instance.Of(effect.Get("light")) : default;
                if (!light.Exists || light.Get("light_radius") is not { Kind: GmKind.Real } radius || radius.AsReal <= best.Radius)
                    continue;
                int colour = light.Get("blend") is { Kind: GmKind.Real } blend ? blend.AsInt : -1;
                double alpha = light.Get("alpha") is { Kind: GmKind.Real } a ? a.AsReal : -1;
                best = new PlayerLight(radius.AsReal, colour, alpha);
            }
        return best;
    }

    /// <summary>A stand-in's light made this one: the one it has taken off, this put on (the game's torch light effect, at
    /// this radius, colour and brightness).</summary>
    public static void Apply(Instance standIn, ref Instance effect, ref PlayerLight current, PlayerLight wanted)
    {
        if (wanted == current && (!wanted.Lit || effect.Exists))
            return;
        if (effect.Exists)
            Game.CallScript("scr_onUnitEffectDestroy", default, effect, false);
        effect = default;
        current = wanted;
        if (!wanted.Lit)
            return;
        effect = Instance.Of(Game.CallScript("scr_onUnitEffectCreate", default, standIn, (int)GameObjectId.o_onUnitEffectTorchFireLight, -1, 12, -13)).Persist();
        if (!effect.Exists)
            return;
        effect.Set("ownerIsGroundMax", 0);
        Instance light = Instance.Of(effect.Get("light"));
        if (!light.Exists)
            return;
        light.Set("light_radius", wanted.Radius);
        if (wanted.Colour >= 0)
        {
            light.Set("blend", wanted.Colour);
            light.Set("image_blend", wanted.Colour);
        }
        if (wanted.Alpha >= 0)
        {
            light.Set("alpha", wanted.Alpha);
            light.Set("lumalpha", wanted.Alpha);
        }
    }
}
