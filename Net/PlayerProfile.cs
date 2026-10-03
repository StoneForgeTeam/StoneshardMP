using System;
using System.Globalization;

namespace StoneshardMP.Net;

// The resistance values the game's enemy inspection card reads. They change far less often than movement/vitals,
// so GhostManager sends this profile only when it changes (and to a newcomer).
public sealed record PlayerProfile(float[] Values)
{
    public static readonly string[] ResistanceNames =
    {
        "Fortitude", "Physical_Resistance", "Nature_Resistance", "Magic_Resistance", "Slashing_Resistance",
        "Piercing_Resistance", "Blunt_Resistance", "Rending_Resistance", "Fire_Resistance", "Shock_Resistance",
        "Poison_Resistance", "Caustic_Resistance", "Frost_Resistance", "Arcane_Resistance", "Unholy_Resistance",
        "Sacred_Resistance", "Psionic_Resistance", "Stun_Resistance", "Knockback_Resistance", "Bleeding_Resistance",
        "Pain_Resistance",
    };

    public static PlayerProfile? Parse(string text)
    {
        string[] fields = text.Split('|');
        if (fields.Length != ResistanceNames.Length)
            return null;
        var values = new float[fields.Length];
        for (int i = 0; i < values.Length; i++)
            if (!float.TryParse(fields[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
                return null;
        return new PlayerProfile(values);
    }
}
