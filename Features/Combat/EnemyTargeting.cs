using System;
using System.Collections.Generic;
using StoneForge;
using StoneshardMP.Features.Areas;
using StoneshardMP.Features.Players;

// An enemy choosing what to do (scr_enemy_choose_state_new: its turn's decision): hooked to turn it on a player beside it.
[assembly: HookScript(nameof(Scripts.scr_enemy_choose_state_new))]

namespace StoneshardMP.Features.Combat;

// Owner: an enemy fighting one player while another stands right beside it turns on the one beside it. The game's AI
// goes for its own player (o_player) and changes target only when a unit hurts it - so it chased the player it found
// first across the room, past another player next to it (#47). Turned the way a hit turns it: that player as its last
// attacker, taken as having just hurt it - the game's own choice then makes them its target, in the right attack state
// (scr_stateAttack for our player, scr_stateNPCCombat for another's stand-in). Several beside it: one of them at random,
// as the MSL version did.
public sealed class EnemyTargeting
{
    private readonly AreaOwnership _ownership;
    private readonly PlayerManager _players;
    private readonly Func<bool> _inSharedWorld;
    private readonly Random _random = new();

    public EnemyTargeting(ModContext context, AreaOwnership ownership, PlayerManager players, Func<bool> inSharedWorld)
    {
        _ownership = ownership;
        _players = players;
        _inSharedWorld = inSharedWorld;
        Scripts.scr_enemy_choose_state_new.Before(context, call =>
        {
            if (_ownership.Role == AreaRole.Owner && _inSharedWorld())
                TurnToNearest(call.Self);
            return false;
        });
    }

    private void TurnToNearest(Instance enemy)
    {
        if (enemy.IsNone || !enemy.Exists || !enemy.Get("ai_is_on").AsBool || enemy.Get("is_neutral").AsBool
            || enemy.Get("object_index").AsInt == _players.ObjectIndex
            || !Gm.ObjectIsAncestor(enemy.Get("object_index").AsInt, (int)GameObjectId.o_enemy))
            return;
        // (Only one already fighting a player - not one that hasn't noticed anyone: being beside it isn't being seen.)
        string state = enemy.Get("state").AsString;
        if (state != "attack" && state != "npc combat")
            return;
        Instance target = Handles.Owner(enemy.Get("target"));
        if (!IsPlayer(target) || Distance(enemy, target) <= 1)
            return;
        var beside = new List<Instance>();
        foreach (Instance player in Players())
            if (!player.Equals(target) && Distance(enemy, player) <= 1)
                beside.Add(player);
        if (beside.Count == 0)
            return;
        Instance chosen = beside[_random.Next(beside.Count)];
        enemy["last_attacker"] = chosen;
        enemy["is_damage_taken"] = true;
    }

    // Our player and the other players here (their stand-ins - not a summon's).
    private IEnumerable<Instance> Players()
    {
        if (OurPlayer.Instance is { IsNone: false } ours && ours.Exists)
            yield return ours;
        foreach (Instance standIn in Instances.All(_players.ObjectIndex))
            if (standIn.Get("mp_summon").IsUndefined && !standIn.Get("mp_slot").IsUndefined)
                yield return standIn;
    }

    private bool IsPlayer(Instance unit)
    {
        if (unit.IsNone || !unit.Exists)
            return false;
        int obj = unit.Get("object_index").AsInt;
        return Handles.IsPlayerObject(obj) || obj == _players.ObjectIndex && unit.Get("mp_summon").IsUndefined;
    }

    // In cells, as the game counts it (the larger of the two axes).
    private static int Distance(Instance a, Instance b)
        => (int)Math.Max(Math.Abs(Math.Floor(a.Get("x").AsReal / 26) - Math.Floor(b.Get("x").AsReal / 26)),
            Math.Abs(Math.Floor(a.Get("y").AsReal / 26) - Math.Floor(b.Get("y").AsReal / 26)));
}
