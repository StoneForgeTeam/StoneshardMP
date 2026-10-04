using System;
using System.Collections.Generic;
using StoneForge;
using StoneshardMP.Features.Players;

namespace StoneshardMP.Features.Rounds;

// Our player's reason, read from the game (legacy: scr_mp_turn_reason, scr_mp_bleed_fatal, scr_mp_burning).
internal static class TurnReasons
{
    private static int _bleedParent = -2, _fire = -2;

    /// <summary>Why our player needs turns now: in combat (an enemy after us - the game's own count), bleeding to death
    /// (their bleeds would take them to 0 before they stop and health comes back - low health alone doesn't count: it
    /// comes back), or on fire - so nobody fights, bleeds out or burns in real time.</summary>
    public static TurnReason Ours()
    {
        Instance player = OurPlayer.Instance;
        if (player.IsNone)
            return TurnReason.None;
        if (Game.CallScript("scr_getAgredMobsCount", default, true).AsReal > 0)
            return TurnReason.Combat;
        var effects = Effects(player);
        if (BleedingOut(player, effects))
            return TurnReason.BleedingOut;
        if (_fire >= 0 && effects.Exists(e => Gm.ObjectIsAncestor(e.Object, _fire) || e.Object == _fire))
            return TurnReason.OnFire;
        return TurnReason.None;
    }

    /// <summary>A reason as the turn order's banner says it ("In combat"...), for us or another player.</summary>
    public static string Say(TurnReason reason, string? who) => reason switch
    {
        TurnReason.Combat => "In combat",
        TurnReason.BleedingOut => who == null ? "You are bleeding out" : who + " is bleeding out",
        TurnReason.OnFire => who == null ? "You are on fire" : who + " is on fire",
        _ => "",
    };

    // The effects on the player: each one's instance and object.
    private static List<(Instance Effect, int Object)> Effects(Instance player)
    {
        if (_bleedParent == -2)
        {
            _bleedParent = Gm.AssetGetIndex("o_db_bleed_parent");
            _fire = Gm.AssetGetIndex("o_db_fire");
        }
        var effects = new List<(Instance, int)>();
        if (player.Get("buffs").AsDsList is not { } buffs)
            return effects;
        for (int i = 0; i < buffs.Count; i++)
        {
            Instance effect = UnitInstance(buffs[i]);
            if (!effect.IsNone && effect.Exists)
                effects.Add((effect, effect.Get("object_index").AsInt));
        }
        return effects;
    }

    // Whether the bleeds, played out turn by turn (each its damage a turn - its Pure_Damage_Self - for the turns it has
    // left), against health coming back each turn (scr_unit_regen: 5% of max health per 100 Health_Restoration, scaled
    // by Healing_Received, plus HP_turn), take the player to 0.
    private static bool BleedingOut(Instance player, List<(Instance Effect, int Object)> effects)
    {
        if (_bleedParent < 0)
            return false;
        var bleeds = new List<(double Damage, double Turns)>();
        foreach (var (effect, obj) in effects)
        {
            if (!Gm.ObjectIsAncestor(obj, _bleedParent))
                continue;
            double damage = effect.Get("data").AsDsMap is { } data && data.Get("Pure_Damage_Self", 0) is { Kind: GmKind.Real } d ? d.AsReal : 0;
            if (damage <= 0 && effect.Get("bleeding_damage") is { Kind: GmKind.Real } own)
                damage = own.AsReal;
            double turns = effect.Get("duration") is { Kind: GmKind.Real } t ? t.AsReal : 0;
            if (damage > 0 && turns > 0)
                bleeds.Add((damage, turns));
        }
        if (bleeds.Count == 0)
            return false;
        double Number(string name, double fallback) => player.Get(name) is { Kind: GmKind.Real } v ? v.AsReal : fallback;
        double health = Number("HP", 0);
        double regen = 0.05 * Number("max_hp", 0) * (Number("Health_Restoration", 0) / 100) * (Number("Healing_Received", 100) / 100)
            + Number("HP_turn", 0);
        double longest = 0;
        foreach (var bleed in bleeds)
            longest = Math.Max(longest, bleed.Turns);
        for (int turn = 1; turn <= Math.Min(longest, 500); turn++)
        {
            foreach (var bleed in bleeds)
                if (bleed.Turns >= turn)
                    health -= bleed.Damage;
            if (health < 1)
                return true;
            health += regen;
        }
        return false;
    }

    private static Instance UnitInstance(GmValue value) => Areas.UnitGrid.InstanceOf(value);
}
