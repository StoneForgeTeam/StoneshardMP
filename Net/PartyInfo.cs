using System;
using System.Globalization;

namespace StoneshardMP.Net;

// A player as their party frame shows them, beyond their state (PlayerState: health, energy, where): level, the head
// sprite their portrait is drawn from, the health and energy caps the HUD shows their bars to (the thresholds, in %),
// whether enemies are after them, the status effects they're under (the game's objects, by name - harmful ones
// first), and the light they carry (a lit torch, lantern or candelabrum - PlayerLight; none: radius 0). Sent as text
// (PartyPacket): "level|head|healthCap|energyCap|combat|effect,effect...|radius:colour:alpha".
public sealed record PartyInfo(int Level, string Head, float HealthCap, float EnergyCap, bool InCombat, string[] Effects,
    PlayerLight Light = default)
{
    public override string ToString() => string.Join("|",
        Level.ToString(CultureInfo.InvariantCulture), Head.Replace("|", ""),
        HealthCap.ToString(CultureInfo.InvariantCulture), EnergyCap.ToString(CultureInfo.InvariantCulture),
        InCombat ? "1" : "0", string.Join(",", Effects), Light.ToString());

    public static PartyInfo? Parse(string text)
    {
        string[] fields = text.Split('|');
        if (fields.Length is not (6 or 7)
            || !int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int level)
            || !float.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float healthCap)
            || !float.TryParse(fields[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float energyCap))
            return null;
        string[] effects = fields[5].Length > 0 ? fields[5].Split(',') : Array.Empty<string>();
        return new PartyInfo(level, fields[1], healthCap, energyCap, fields[4] == "1", effects,
            fields.Length == 7 ? PlayerLight.Parse(fields[6]) : default);
    }

    /// <summary>Whether they're out cold (the game's coma).</summary>
    public bool Unconscious => Array.IndexOf(Effects, "o_db_coma") >= 0;
}
