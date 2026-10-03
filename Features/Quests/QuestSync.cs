using System;
using System.Collections.Generic;
using System.Linq;
using StoneForge;
using StoneshardMP.Features.Join;
using StoneshardMP.Net;
using StoneshardMP.Net.Packets;

// The game scripts quests and the rest of the shared story go through (the patcher makes them hookable).
[assembly: HookScript(nameof(Scripts.scr_quest_start))]
[assembly: HookScript(nameof(Scripts.scr_quest_set_progress))]
[assembly: HookScript(nameof(Scripts.scr_quest_set_complete))]
[assembly: HookScript(nameof(Scripts.scr_quest_set_failed))]
[assembly: HookScript(nameof(Scripts.scr_quest_set_field))]
[assembly: HookScript(nameof(Scripts.scr_quest_set_timestamp))]
[assembly: HookScript(nameof(Scripts.scr_quest_discard))]
[assembly: HookScript(nameof(Scripts.scr_quest_complete_until))]
[assembly: HookScript(nameof(Scripts.scr_quest_next_target))]
[assembly: HookScript(nameof(Scripts.scr_quest_dismiss_incomplete))]
[assembly: HookScript(nameof(Scripts.scr_globaltile_reputation_update))]
[assembly: HookScript(nameof(Scripts.scr_dialogue_complete))]
[assembly: HookScript(nameof(Scripts.scr_globalFraction))]
[assembly: HookScript(nameof(Scripts.scr_locationRoomPresetFlagSet))]
[assembly: HookScript(nameof(Scripts.scr_locationRoomPresetFlagUnset))]
[assembly: HookScript(nameof(Scripts.scr_locationRoomPresetFlagsReset))]
[assembly: HookScript(nameof(Scripts.scr_everyPlayerTurnQuestTriggers))]
[assembly: HookScript(nameof(Scripts.scr_everyHourQuestTriggers))]
[assembly: HookScript(nameof(Scripts.scr_instance_exists_item))]

namespace StoneshardMP.Features.Quests;

// One story for everyone in a world (legacy StoneshardMP's quest sync):
// - Shared calls: every call to a script that changes shared story state - a quest step, settlement reputation, a
//   dialogue flag (what's been said), a location's flags, a faction's crime record - goes to the others, who make the
//   same call (SharedCallPacket). The real script runs there, so their quest book, journal and reputation log update
//   the game's own way. Not sent back (calls made while applying one aren't sent), and a nested call sent twice is
//   harmless: the scripts skip what's done already.
// - Shared quest items: both games run the quest triggers (every turn, every hour), which move a quest on or back by
//   whether *our* character has an item (the lost plane, the black tablet's key and relic), 1000 gold (the abbey
//   contract) or the thief's wine. The player without it wound the quest back, which synced, and the holder's game
//   moved it on again. While they run, "has it" means anyone in the world has it (QuestItemsPacket says who has what).
// Only between games in the same world (the seed), a client once it plays the host's save.
public sealed class QuestSync
{
    // (Our checks go out again this often, for players who've just come in.)
    private const long ItemsResendMs = 2000;
    private static readonly HashSet<string> CrimeFields = new() { "Crime Status", "Penalty", "Attack_Count", "Crime_State", "Crime_Timestamp" };

    private readonly ModContext _context;
    private readonly Session _session;
    private readonly JoinManager _join;
    // Scripts whose calls are shared, by name: how a call's arguments go out (null: not this call - a read).
    private readonly Dictionary<string, Func<GmValue[], GmValue[]?>> _shared = new();
    private bool _applying;
    // Quest triggers running (and the item check inside one, asking the game itself).
    private bool _triggers, _askingGame;
    // Which checks we pass, and each player's.
    private readonly SortedSet<string> _have = new(StringComparer.Ordinal);
    private readonly Dictionary<int, HashSet<string>> _theirs = new();
    private bool _haveChanged;
    private long _haveSentAt;

    public QuestSync(ModContext context, Session session, JoinManager join)
    {
        _context = context;
        _session = session;
        _join = join;
        session.On<SharedCallPacket>(Apply);
        session.On<QuestItemsPacket>((from, packet) =>
            _theirs[from.Slot] = new HashSet<string>(packet.Have.Split(',', StringSplitOptions.RemoveEmptyEntries)));
        session.PlayerLeft += player => _theirs.Remove(player.Slot);
        session.PlayerJoined += _ => _haveChanged = true;

        // Quests and the quest book: as they are.
        foreach (var script in new[]
        {
            Scripts.scr_quest_start, Scripts.scr_quest_set_progress, Scripts.scr_quest_set_complete,
            Scripts.scr_quest_set_failed, Scripts.scr_quest_set_field, Scripts.scr_quest_set_timestamp,
            Scripts.scr_quest_discard, Scripts.scr_quest_complete_until, Scripts.scr_quest_next_target,
            Scripts.scr_quest_dismiss_incomplete,
            // A location's flags (cleared, looted, its event done...): what every setter comes down to.
            Scripts.scr_locationRoomPresetFlagSet, Scripts.scr_locationRoomPresetFlagUnset,
            Scripts.scr_locationRoomPresetFlagsReset,
        })
            Share(script, args => args);
        // Settlement reputation (change, x, y, min, max, silent): the tile defaults to where the caller stands - here,
        // not there - so it's filled in before it goes; none from off the world map.
        Share(Scripts.scr_globaltile_reputation_update, args =>
        {
            double gridX = Game.Global["playerGridX"].AsReal;
            if (gridX == -4)
                return null;
            var sent = Pad(args, 6);
            if (sent[1].IsUndefined)
                sent[1] = gridX;
            if (sent[2].IsUndefined)
                sent[2] = Game.Global["playerGridY"];
            return sent;
        });
        // Dialogue flags (said, offered, settled): with one argument it only reads.
        Share(Scripts.scr_dialogue_complete, args => args.Length > 1 && !args[1].IsUndefined ? args : null);
        // A faction's crime record - set by the town alarm and the game's crime code: these fields, and only sets (with
        // two arguments it reads); absolute values, so a nested repeat is harmless.
        Share(Scripts.scr_globalFraction, args =>
            args.Length > 2 && !args[2].IsUndefined && CrimeFields.Contains(args[1].AsString) ? args : null);

        // The quest triggers, with the quest item checks shared.
        Scripts.scr_everyPlayerTurnQuestTriggers.Before(context, call =>
        {
            call.Result = RunTriggers(call, perTurn: true);
            return true;
        });
        Scripts.scr_everyHourQuestTriggers.Before(context, call =>
        {
            call.Result = RunTriggers(call, perTurn: false);
            return true;
        });
        Scripts.scr_instance_exists_item.Before(context, call =>
        {
            if (!_triggers || _askingGame || !(Arg(call, 1).IsUndefined || Arg(call, 1).AsString == "count"))
                return false;
            _askingGame = true;
            GmValue mine;
            try { mine = Scripts.scr_instance_exists_item.CallOriginal(call); }
            finally { _askingGame = false; }
            string key = Arg(call, 0).AsString;
            Have(key, mine.AsReal != 0);
            call.Result = mine.AsReal != 0 || !TheyHave(key) ? mine : 1;
            return true;
        });
    }

    public void Clear()
    {
        _have.Clear();
        _theirs.Clear();
        _applying = _triggers = _askingGame = false;
    }

    // Whether we share our story: the host in a world with players, a client in the host's.
    private bool Sharing => _session.Mode switch
    {
        Session.SessionMode.Host => Gml.MpHostInWorld() && _session.Players.Any(),
        Session.SessionMode.Client => _join.ClientInWorld,
        _ => false,
    };

    // Each frame: our quest item checks again every couple of seconds (and to a newcomer), for players who've just
    // come in.
    public void Tick()
    {
        if (Sharing && _have.Count > 0 && (_haveChanged || Environment.TickCount64 - _haveSentAt >= ItemsResendMs))
            SendHave();
    }

    private void SendHave()
    {
        _haveChanged = false;
        _haveSentAt = Environment.TickCount64;
        _session.Send(new QuestItemsPacket(string.Join(",", _have)));
    }

    // ---- shared calls ----

    /// <summary>Shares a script's calls: each one goes to the others, made with the arguments <paramref name="outgoing"/>
    /// gives (null: not this call), and they make it too. The mod declares it hookable ([assembly: HookScript]).</summary>
    public void Share(Script script, Func<GmValue[], GmValue[]?> outgoing)
    {
        _shared[script.Name] = outgoing;
        script.Before(_context, call =>
        {
            if (!_applying && Sharing && outgoing(call.Args) is { } args)
                _session.Send(new SharedCallPacket(Gml.MpWorldSeed(), script.Name, args));
            return false;
        });
    }

    private void Apply(RemotePlayer sender, SharedCallPacket packet)
    {
        // Only listed scripts, from and for a game in our world.
        if (!_shared.ContainsKey(packet.Script) || !Sharing || packet.Seed != Gml.MpWorldSeed())
            return;
        // (As our player, as the game's own calls are made.)
        Instance player = Game.CallBuiltin("instance_find", (int)GameObjectId.o_player, 0).AsInstance;
        _applying = true;
        try { Game.CallScript(packet.Script, player, packet.Args); }
        catch (Exception e) { _context.Log($"{packet.Script} from {sender.Name} failed: {e.Message}"); }
        finally { _applying = false; }
        if (packet.Script == Scripts.scr_quest_set_complete.Name)
            _context.Log($"{sender.Name} completed a quest ({packet.Args.FirstOrDefault().AsString})");
    }

    // ---- quest items ----

    // The quest triggers (per turn: as our player; hourly) with the item checks shared: our 1000 gold counts as anyone's,
    // and when another player holds the thief's wine and we don't, this turn's are left to their game (the wine check
    // is inside another script, out of reach) - its quest changes reach us as shared calls.
    private GmValue RunTriggers(ScriptCall call, bool perTurn)
    {
        if (!Sharing)
            return CallOriginal(call, perTurn);
        Instance self = call.Self;
        double gold = 0;
        if (perTurn && self.Exists)
        {
            gold = self["gold"].AsReal;
            Have("gold1000", gold >= 1000);
            bool wine = HasWine(self);
            Have("wine", wine);
            if (!wine && TheyHave("wine"))
                return GmValue.Undefined;
            if (gold < 1000 && TheyHave("gold1000"))
                self["gold"] = 1000;
        }
        _triggers = true;
        try { return CallOriginal(call, perTurn); }
        finally
        {
            _triggers = false;
            if (perTurn && self.Exists)
                self["gold"] = gold;
        }
    }

    // The vineyard thief's quest: whether our character holds his marked wine (no if the check can't be made).
    private bool HasWine(Instance self)
    {
        try { return Game.CallScript("scr_npc_lines_vineyard_thief_check_wine", self).AsBool; }
        catch (Exception e)
        {
            _context.Log($"The thief's wine check failed: {e.Message}");
            return false;
        }
    }

    private static GmValue CallOriginal(ScriptCall call, bool perTurn) => perTurn
        ? Scripts.scr_everyPlayerTurnQuestTriggers.CallOriginal(call)
        : Scripts.scr_everyHourQuestTriggers.CallOriginal(call);

    // Our answer to a check; told to the others as soon as it changes - before the quest change it leads to, so
    // theirs never sees a step it can't account for.
    private void Have(string key, bool have)
    {
        bool changed = have ? _have.Add(key) : _have.Remove(key);
        if (changed && Sharing)
            SendHave();
    }

    // Whether another player in our world passes a check.
    private bool TheyHave(string key)
        => _session.Players.Any(p => p.State != null && _theirs.TryGetValue(p.Slot, out var have) && have.Contains(key));

    private static GmValue Arg(ScriptCall call, int index) => index < call.Args.Length ? call.Args[index] : GmValue.Undefined;

    private static GmValue[] Pad(GmValue[] args, int count)
    {
        var padded = new GmValue[Math.Max(count, args.Length)];
        Array.Copy(args, padded, args.Length);
        return padded;
    }
}
