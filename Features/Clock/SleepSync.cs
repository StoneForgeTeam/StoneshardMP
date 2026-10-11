using System;
using System.Linq;
using StoneForge;
using StoneshardMP.Features.Players;
using StoneshardMP.Net;

// The game's sleep (a bed's skip-time panel, a jail bed): the host's starts the fade everyone shares.
[assembly: HookScript(nameof(Scripts.scr_smoothSaveSleep))]
// (And the vigor it gives - o_b_fresh, through the game's modifier script - for those who slept beside it.)
[assembly: HookScript(nameof(Scripts.scr_modifier_change))]

namespace StoneshardMP.Features.Clock;

// Sleep in a shared world: only the host sleeps, and everyone sleeps with it.
// - A client can't: a bed (c_bed_sleep's use, and a jail bed's) only says so - its hours would be its own, and the
//   world's time is the host's.
// - The host's sleep (o_sleepController, the game's own: its fade, the hours passing, the save) fades every client out
//   with it, and when the host wakes they fade back in with the host's clock - the hours it slept (SleepPacket). Their
//   characters don't sleep: only time passes for them - but a client in the same place as the host's bed wakes with
//   the vigor the host's sleep gave it (o_b_fresh: the same turns, and the same most - the bed's, the hours'; none after a
//   poor bed or nightmares).
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
            _session.Send(new SleepPacket(false, GameClock.Snapshot(), _sleeping ? _sleptAt : "", _sleeping ? _vigor : 0, _vigorMax));
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
        // (Slept beside the host: its vigor ours too.)
        if (packet.Vigor > 0 && packet.Place.Length > 0 && OurPlayer.Place == packet.Place && StoneForge.Player.Exists)
            Game.CallScript("scr_modifier_change", default, StoneForge.Player.Instance, (int)GameObjectId.o_b_fresh, packet.Vigor, packet.VigorMax);
        Unfade();
    }

    private void Unfade()
    {
        if (_faded)
            Blackout.Hide();
        _faded = false;
    }
}
