using System;
using System.Linq;
using StoneForge;
using StoneshardMP.Features.Areas;
using StoneshardMP.Features.Players;
using StoneshardMP.Net;
using StoneshardMP.Net.Packets;

// Every noise in the game - a step, a blow, a door, something broken, a spell: whoever's in range hears it.
[assembly: HookScript(nameof(Scripts.scr_noise_produce))]

namespace StoneshardMP.Features.Combat;

// Noise where players are together. The game's noise (scr_noise_produce: how loud, at which cell, from what) is heard by
// the units in range in the game that made it - and a follower's units are the owner's copies, their AI off, so a
// follower's footsteps, fights and spells woke nobody: only the owner's own noise did. Now a follower's own noise - made
// by its player, its summon, or anything of theirs (CombatSync.IsOursDeep) - goes to the owner, which makes it there,
// from the follower's stand-in: the owner's units turn, investigate, wake, as for the owner's own.
// (Being seen needs nothing more: the game's sight is a unit's vision range and line of sight - the stand-in's, which
// is where the follower is - with darkness cutting it and light countering, and the stand-in carries the follower's
// light.)
public sealed class NoiseSync
{
    private readonly Session _session;
    private readonly AreaOwnership _ownership;
    private readonly PlayerManager _players;
    private readonly Func<bool> _inSharedWorld;
    // Owner: making a follower's noise (not one of ours to send).
    private bool _replaying;

    public NoiseSync(ModContext context, Session session, AreaOwnership ownership, PlayerManager players, Func<bool> inSharedWorld)
    {
        _session = session;
        _ownership = ownership;
        _players = players;
        _inSharedWorld = inSharedWorld;
        session.On<NoisePacket>(Receive);
        Scripts.scr_noise_produce.Before(context, call =>
        {
            Produced(call);
            return false;
        });
    }

    // Follower: a noise of ours, to the owner.
    private void Produced(ScriptCall call)
    {
        if (_replaying || _ownership.Role != AreaRole.Follower || !_session.Connected || !_inSharedWorld() || call.Args.Length < 3)
            return;
        Instance source = call.Args.Length > 3 && !call.Args[3].IsUndefined ? Instance.Of(call.Args[3]) : call.Self;
        if (!CombatSync.IsOursDeep(source))
            return;
        GmValue faction = Game.Global["__noise_faction_key"];
        _session.Send(new NoisePacket((float)call.Args[0].AsReal, (short)call.Args[1].AsReal, (short)call.Args[2].AsReal,
            faction.Kind == GmKind.String ? faction.AsString : "Any"), _ownership.Owner);
    }

    // Owner: a follower's noise, made here from their stand-in.
    private void Receive(RemotePlayer from, NoisePacket noise)
    {
        if (_ownership.Role != AreaRole.Owner || !_ownership.Others.Contains(from.Slot) || !Gm.InGame)
            return;
        Instance standIn = _players.UnitOf(from.Slot);
        GmValue previous = Game.Global["__noise_faction_key"];
        _replaying = true;
        try
        {
            Game.Global["__noise_faction_key"] = noise.Faction;
            Game.CallScript("scr_noise_produce", standIn.IsNone ? default : standIn, noise.Power, noise.CellX, noise.CellY,
                standIn.IsNone ? (GmValue)(-4) : (GmValue)standIn);
        }
        finally
        {
            Game.Global["__noise_faction_key"] = previous;
            _replaying = false;
        }
    }
}
