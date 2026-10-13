using System;
using System.Globalization;
using System.Linq;
using StoneForge;
using StoneshardMP.Features.Players;
using StoneshardMP.Net;

// The game's sleep (a bed's skip-time panel, a jail bed): the host's starts the fade everyone shares.
[assembly: HookScript(nameof(Scripts.scr_smoothSaveSleep))]
// (And the vigor it gives - o_b_fresh, through the game's modifier script - for those who slept beside it.)
[assembly: HookScript(nameof(Scripts.scr_modifier_change))]
// (And how it rests the sleeper - fatigue, health, energy, pain, hunger and thirst over the hours - for the clients too.)
[assembly: HookScript(nameof(Scripts.scr_lifeParamsUpdate))]

namespace StoneshardMP.Features.Clock;

// Sleep in a shared world: only the host sleeps, and everyone sleeps with it.
// - A client can't: a bed (c_bed_sleep's use, and a jail bed's) only says so - its hours would be its own, and the
//   world's time is the host's.
// - The host's sleep (o_sleepController, the game's own: its fade, the hours passing, the save) fades every client out
//   with it, and when the host wakes they fade back in with the host's clock - the hours it slept (SleepPacket) - and
//   rested as the game rests a sleeper (scr_lifeParamsUpdate: fatigue, health and energy restored, pain eased, hunger
//   and thirst grown over the hours). A client in the same place as the host's bed rests as the host did (the bed's,
//   the inn's own rates) and wakes with the vigor the host's sleep gave it (o_b_fresh: the same turns, and the same most;
//   none after a poor bed or nightmares); one elsewhere rests plainly (no morale or sanity from it). Before, a client's
//   fatigue never went down.
// - A host's time that jumps without a sleep (a fast travel: o_sleepController too) is sent the same way, without the fade.
public sealed class SleepSync
{
    private readonly Session _session;
    private readonly Func<bool> _inSharedWorld;
    // Host: the game's sleep (or travel) under way, and whether it's a sleep (our clients faded).
    private bool _was, _sleeping;
    // Host: where we slept, and the vigor our sleep gave us (its turns and most; 0: none yet).
    private string _sleptAt = "";
    private double _vigor, _vigorMax;
    // Host: how our sleep rested us (scr_lifeParamsUpdate's arguments, its defaults filled in; null: not yet).
    private double[]? _rest;
    // (scr_lifeParamsUpdate's defaults: every rate 1 but immunity's, 0.)
    private static readonly double[] RestDefaults = { 0, 1, 1, 1, 0, 1, 1, 1, 1, 1, 1, 1 };
    // Client: faded out for the host's sleep.
    private bool _faded;

    public SleepSync(ModContext context, Session session, Func<bool> inSharedWorld)
    {
        _session = session;
        _inSharedWorld = inSharedWorld;
        session.On<SleepPacket>(Receive);
        Scripts.scr_smoothSaveSleep.Before(context, call =>
        {
            // (A sleep - a bed's types, 0-5 - not the caravan's travel, 13 and 14, which goes through it too: its time
            // comes as the host's clock, without the fade.)
            if (_session.Mode == Session.SessionMode.Host
                && call.Args.Length >= 2 && call.Args[1].Kind == GmKind.Real && call.Args[1].AsReal is >= 0 and <= 5)
            {
                _sleeping = true;
                _sleptAt = OurPlayer.Place ?? "";
                _vigor = _vigorMax = 0;
                _rest = null;
            }
            return false;
        });
        // (Our sleep's rest, as it's given.)
        Scripts.scr_lifeParamsUpdate.Before(context, call =>
        {
            if (_session.Mode == Session.SessionMode.Host && _sleeping)
            {
                var rest = (double[])RestDefaults.Clone();
                for (int i = 0; i < rest.Length && i < call.Args.Length; i++)
                    if (call.Args[i].Kind is GmKind.Real or GmKind.Bool)
                        rest[i] = call.Args[i].AsReal;
                _rest = rest;
            }
            return false;
        });
        // (Our sleep's vigor, as it's given: scr_modifier_change(o_player, o_b_fresh, turns, most).)
        Scripts.scr_modifier_change.Before(context, call =>
        {
            if (_session.Mode == Session.SessionMode.Host && _sleeping && call.Args.Length >= 4
                && call.Args[1].Kind == GmKind.Real && call.Args[1].AsInt == (int)GameObjectId.o_b_fresh)
            {
                _vigor = call.Args[2].AsReal;
                _vigorMax = call.Args[3].AsReal;
            }
            return false;
        });
        foreach (string bed in new[] { "c_bed_sleep", "c_bed_sleep_jail" })
            context.OnCode($"gml_Object_{bed}_Other_10", before: (_, _) => Refused());
    }

    public void Clear()
    {
        _was = _sleeping = false;
        Unfade();
    }

    // A client in the host's world at a bed: no.
    private bool Refused()
    {
        if (_session.Mode != Session.SessionMode.Client || !_inSharedWorld())
            return false;
        string host = _session.Players.FirstOrDefault(p => p.Slot == 0)?.Name ?? "the host";
        Gm.AudioPlaySound(Sound.snd_mouse_skill_denied, 4);
        Game.CallScript("scr_actionsLogAddMessage", default, $"Only {host} can sleep - you'll sleep when they do.");
        return true;
    }

    // Host, each frame: our sleep starting (the clients fade out), and ending (they fade in, at our clock).
    public void Tick()
    {
        if (_session.Mode != Session.SessionMode.Host || !_session.Connected)
        {
            _was = _sleeping = false;
            return;
        }
        bool now = Gm.InstanceExists(GameObjectId.o_sleepController);
        if (now && !_was && _sleeping)
            _session.Send(new SleepPacket(true, GameClock.Snapshot(), _sleptAt, 0, 0));
        else if (!now && _was)
        {
            string rest = _sleeping && _rest != null ? string.Join(",", _rest.Select(r => r.ToString("R", CultureInfo.InvariantCulture))) : "";
            _session.Send(new SleepPacket(false, GameClock.Snapshot(), _sleeping ? _sleptAt : "", _sleeping ? _vigor : 0, _vigorMax, rest));
            _sleeping = false;
        }
        _was = now;
    }

    private void Receive(RemotePlayer from, SleepPacket packet)
    {
        if (_session.Mode != Session.SessionMode.Client || from.Slot != 0 || !_inSharedWorld())
            return;
        if (packet.Asleep)
        {
            _faded = true;
            Blackout.Show($"{from.Name} is sleeping...");
            return;
        }
        WorldClock.ApplyClock(packet.Clock);
        bool beside = packet.Place.Length > 0 && OurPlayer.Place == packet.Place;
        if (StoneForge.Player.Exists && Rest(packet.Rest, beside) is { } rest)
            Game.CallScript("scr_lifeParamsUpdate", default, rest.Select(r => (GmValue)r).ToArray());
        // (Slept beside the host: its vigor ours too.)
        if (packet.Vigor > 0 && beside && StoneForge.Player.Exists)
            Game.CallScript("scr_modifier_change", default, StoneForge.Player.Instance, (int)GameObjectId.o_b_fresh, packet.Vigor, packet.VigorMax);
        Unfade();
    }

    // How we rest for the host's sleep: as it did, beside it; elsewhere, plainly - its hours at the plain rates, with no
    // morale or sanity from it (no bed of ours). Null: no sleep's rest sent (a fast travel, an older host).
    private static double[]? Rest(string sent, bool beside)
    {
        string[] parts = sent.Split(',');
        if (sent.Length == 0 || parts.Length != RestDefaults.Length)
            return null;
        var rest = new double[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out rest[i]))
                return null;
        if (rest[0] <= 0)
            return null;
        if (beside)
            return rest;
        var plain = (double[])RestDefaults.Clone();
        plain[0] = rest[0];
        plain[6] = plain[7] = 0;
        return plain;
    }

    private void Unfade()
    {
        if (_faded)
            Blackout.Hide();
        _faded = false;
    }
}
