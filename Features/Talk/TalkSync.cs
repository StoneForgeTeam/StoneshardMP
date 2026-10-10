using System;
using System.Collections.Generic;
using System.Linq;
using StoneForge;
using StoneshardMP.Features.Areas;
using StoneshardMP.Features.Players;
using StoneshardMP.Net;
using StoneshardMP.Net.Packets;

// A conversation starting (every way in: talking to an NPC, a trigger, a warning - run as the NPC), and the dialogue
// window moving on (an answer, the number keys, Space): a listener's window is ours to fill, not the game's.
[assembly: HookScript(nameof(Scripts.scr_dialogue_start))]
[assembly: HookScript(nameof(Scripts.scr_dialogue_advance))]

namespace StoneshardMP.Features.Talk;

// NPC conversations where players are together: one player talks or trades with an NPC at a time, and the others can
// listen in.
// - Each player sends what they're doing with an NPC as it changes (TalkPacket): a conversation or a trade, with which NPC
//   (the area owner's sync id for it, so every game finds its own), and what their dialogue window shows - the NPC's line,
//   their last answer, their answers as their game made them. Mirroring the window, not running the dialogue, is what
//   keeps it right: the game picks lines and answers at random, and by quest, reputation and what's been said in that
//   player's world, so another game would pick others.
// - Someone else's NPC: talking to it opens a listener's window instead of the conversation - the game's own dialogue
//   window, filled with the talker's line and answers (greyed: only the talker chooses) and a [Stop listening] button.
//   The game's dialogue never starts in the listener's game, so nothing in it is said, rolled or flagged there. Trading:
//   only the trader; others are told who's trading with it. Esc or [Stop listening] leaves; it closes itself when the
//   conversation ends. Closing it doesn't take a turn (it's a monologue to the game).
// - Two players starting on one NPC at once: whoever started first keeps it (within half a second, the lower slot), and
//   the other's window turns into a listener's, or their trade closes.
// - An NPC in a conversation or trade with anyone stays put: its turn is skipped (in single player the world waits for a
//   conversation; here others' turns go on).
// - A speech cloud bobs over each other player who's talking or trading, and over their NPC (PlayerManager draws them).
public sealed class TalkSync
{
    private const int Interval = 3;
    private const long ResendMs = 3000, TieMs = 500;
    private const string StopKey = "mp_listen_stop", AnswerKey = "mp_listen_";

    private readonly ModContext _context;
    private readonly Session _session;
    private readonly AreaUnits _areaUnits;
    private readonly AreaOwnership _ownership;
    private readonly Func<bool> _inSharedWorld;
    // What each other player is doing (their last TalkPacket), and when we heard they started.
    private readonly Dictionary<int, (TalkPacket Talk, long Since)> _remote = new();
    // Ours: the NPC we're talking or trading with, since when, and what we last sent.
    private Instance _ourNpc;
    private long _ourSince, _sentAt;
    private string _sent = "";
    private int _frame;
    // Listening: to whom, in our window (made by us), what it was last filled with.
    private int _listenSlot = -1;
    private Instance _panel;
    private string _built = "", _builtText = "";
    private int _builtButtons, _settle;

    public TalkSync(ModContext context, Session session, AreaUnits areaUnits, AreaOwnership ownership, PlayerManager players,
        Func<bool> inSharedWorld)
    {
        _context = context;
        _session = session;
        _areaUnits = areaUnits;
        _ownership = ownership;
        _inSharedWorld = inSharedWorld;
        session.On<TalkPacket>(Receive);
        session.PlayerLeft += player => _remote.Remove(player.Slot);
        players.TalkingTo = TalkingTo;
        Scripts.scr_dialogue_start.Before(context, call => Starting(call.Self));
        // (A listener's window: the game doesn't move it on - not an answer, not a key.)
        Scripts.scr_dialogue_advance.Before(context, call => _panel.Exists && call.Self.Persist().Equals(_panel));
        context.OnCode("gml_Object_o_contract_button_Mouse_4", before: (self, _) => Clicked(self));
        // (An NPC's turn: o_NPC's user event 0.)
        context.OnCode("gml_Object_o_NPC_Other_10", before: (self, _) => Together && InTalk(self.Persist()));
    }

    public void Clear()
    {
        _remote.Clear();
        _ourNpc = default;
        _sent = "";
        _sentAt = 0;
        StopListening();
    }

    private bool Together => _session.Connected && _inSharedWorld() && Gm.InGame;

    // ---- which NPC ----

    // The id every game in the place knows an NPC by: the owner's sync id (-1: none - not shared, or not synced yet).
    private long IdOf(Instance npc) => npc.IsNone ? -1 : _ownership.Role switch
    {
        AreaRole.Owner => _areaUnits.SyncIdOf(npc) ?? -1,
        AreaRole.Follower => _areaUnits.OwnerIdOf(npc) ?? -1,
        _ => -1,
    };

    private Instance NpcOf(long id) => id < 0 ? default : _ownership.Role switch
    {
        AreaRole.Owner => _areaUnits.UnitOf(id),
        AreaRole.Follower => _areaUnits.LocalOf(id),
        _ => default,
    };

    // Another player here talking or trading with this NPC.
    private (int Slot, TalkPacket Talk, long Since)? HolderOf(Instance npc)
    {
        if (npc.IsNone || OurPlayer.Place is not { } place)
            return null;
        foreach (var (slot, (talk, since)) in _remote)
            if (talk.Kind != TalkKind.None && talk.Place == place && talk.NpcId >= 0 && NpcOf(talk.NpcId).Persist().Equals(npc.Persist()))
                return (slot, talk, since);
        return null;
    }

    // Whether anyone - us or another player here - is talking or trading with it.
    private bool InTalk(Instance npc)
    {
        if (HolderOf(npc) != null)
            return true;
        foreach (GameObjectId window in new[] { GameObjectId.o_dialogue, GameObjectId.o_trade_inventory })
            foreach (Instance open in Instances.All(window))
                if (Instance.Of(open.Get("owner")).Persist().Equals(npc))
                    return true;
        return false;
    }

    // For the speech clouds: what a player is talking to here (none: not here, or not one we know); null if they aren't.
    private Instance? TalkingTo(int slot)
    {
        if (!Together || !_remote.TryGetValue(slot, out var remote) || remote.Talk.Kind == TalkKind.None
            || remote.Talk.Place != OurPlayer.Place)
            return null;
        return NpcOf(remote.Talk.NpcId);
    }

    // ---- someone else's NPC ----

    // A conversation starting with an NPC someone else has: ours listens in, or is told they're trading.
    private bool Starting(Instance npc)
    {
        if (!Together || HolderOf(npc) is not { } holder)
            return false;
        string who = _session.Players.FirstOrDefault(p => p.Slot == holder.Slot)?.Name ?? "Someone";
        if (holder.Talk.Kind == TalkKind.Trade)
            Message($"{who} is trading with {holder.Talk.NpcName}.");
        else
            Listen(holder.Slot, npc, who, holder.Talk.NpcName);
        return true;
    }

    private void Listen(int slot, Instance npc, string who, string npcName)
    {
        StopListening();
        // (The game's dialogue window, as it makes one for a conversation, given a context of its own so the window's own
        // code finds what it reads - and no turn when it closes.)
        Instance panel = Instance.Of(Game.CallScript("scr_dialog_create", npc, npc, false)).Persist();
        if (!panel.Exists)
            return;
        using (var context = GmStruct.Create())
        {
            foreach (string member in new[] { "Fragments", "Scripts", "Specs", "Sounds", "Variables", "Speakers", "Strings" })
            {
                using var value = GmStruct.Create();
                context[member] = value;
            }
            context["RootFragment"] = "stoneshardmp_listen";
            context["Monologue"] = true;
            context["owner"] = npc;
            panel.Set("dialog_id", context);
        }
        panel.Set("interact_id", npc);
        panel.Set("is_monologue", true);
        panel.Set("dialogue_ended", false);
        _panel = panel;
        _listenSlot = slot;
        _built = "";
        _settle = 0;
        Message($"Listening to {who} and {npcName} (Esc to stop).");
        Fill();
    }

    private void StopListening()
    {
        _listenSlot = -1;
        _panel = default;
        _built = _builtText = "";
    }

    // Our listener's window closed by us: the talker finished, or we left.
    private void CloseListening(string message)
    {
        Instance panel = _panel;
        StopListening();
        if (panel.Exists)
        {
            panel.Set("is_monologue", true);
            panel.Set("dialogue_ended", false);
            panel.Destroy();
        }
        if (message.Length > 0)
            Message(message);
    }

    // A dialogue button pressed: in a listener's window, only [Stop listening] does anything.
    private bool Clicked(Instance button)
    {
        if (!_panel.Exists || !Instance.Of(button.Get("parent")).Persist().Equals(_panel))
            return false;
        if (button.Get("func").AsString == StopKey)
            CloseListening("");
        return true;
    }

    // The listener's window shows the talker's: their NPC's line (under their last answer), their answers greyed, and
    // [Stop listening]. Made again when theirs changes - or when ours no longer shows what we put there (the game's
    // window filling itself in over its first frames), and a few times as it settles.
    private void Fill()
    {
        if (!_panel.Exists || !_remote.TryGetValue(_listenSlot, out var remote))
            return;
        TalkPacket talk = remote.Talk;
        string who = _session.Players.FirstOrDefault(p => p.Slot == _listenSlot)?.Name ?? "";
        string text = talk.LastAnswer.Length > 0 ? $"{who}: {talk.LastAnswer}\n\n{talk.Text}" : talk.Text;
        string signature = text + "\u0001" + string.Join("\u0001", talk.Answers);
        _settle++;
        bool settling = _settle is 2 or 5 or 10 or 20 or 45 or 90;
        if (!settling && signature == _built && _panel.Get("text").AsString == _builtText && Buttons(_panel).Count == _builtButtons)
            return;
        _panel.Set("text", text);
        _panel.Set("full_text", text);
        _panel.Set("text_wrap_number", 0);
        _panel.Set("text_wrap_max_number", 0);
        _panel.Set("is_text_changed", true);
        Instance render = Instance.Of(_panel.Get("render"));
        if (render.Exists)
            Game.CallScript("scr_guiContainerChildrenDestroy", render, render.Get("buttonsContainer"));
        for (int i = 0; i < talk.Answers.Length; i++)
        {
            Instance answer = Instance.Of(Game.CallScript("scr_create_contract_button", _panel, talk.Answers[i], AnswerKey + i));
            if (answer.Exists)
                answer.Set("canPress", false);
        }
        Game.CallScript("scr_create_contract_button", _panel, "[Stop listening]", StopKey);
        if (render.Exists)
        {
            render.Set("surfaceDraw", _panel.Get("is_activate"));
            Game.CallBuiltinAs("event_user", render, _panel, 0);
        }
        _built = signature;
        _builtText = text;
        _builtButtons = talk.Answers.Length + 1;
    }

    private static List<Instance> Buttons(Instance panel)
        => Instances.All(GameObjectId.o_contract_button).Where(b => Instance.Of(b.Get("parent")).Persist().Equals(panel)).ToList();

    // ---- ours ----

    public void Tick()
    {
        if (!Together)
        {
            if (!_session.Connected)
                _remote.Clear();
            if (_listenSlot >= 0)
                CloseListening("");
            return;
        }
        if (++_frame % Interval != 0)
            return;
        if (_listenSlot >= 0)
        {
            TickListening();
            Send(TalkKind.None, default, default);
            return;
        }
        // What we're doing: our dialogue window (one we didn't make to listen), or a trade.
        Instance panel = Instances.All(GameObjectId.o_dialogue).FirstOrDefault();
        Instance trade = Instances.All(GameObjectId.o_trade_inventory).FirstOrDefault();
        TalkKind kind = panel.Exists ? TalkKind.Dialogue : trade.Exists ? TalkKind.Trade : TalkKind.None;
        Instance npc = kind switch
        {
            TalkKind.Dialogue => Instance.Of(panel.Get("owner")).Persist(),
            TalkKind.Trade => Instance.Of(trade.Get("owner")).Persist(),
            _ => default,
        };
        if (!npc.Equals(_ourNpc))
        {
            _ourNpc = npc;
            _ourSince = Environment.TickCount64;
        }
        // (Someone else started on it first - or at the same moment, with the lower slot: theirs.)
        if (kind != TalkKind.None && HolderOf(npc) is { } holder
            && (_ourSince >= holder.Since || (_session.Slot > holder.Slot && _ourSince >= holder.Since - TieMs)))
        {
            Yield(kind, panel, trade, npc, holder.Slot, holder.Talk);
            return;
        }
        Send(kind, panel, npc);
    }

    private void TickListening()
    {
        if (!_panel.Exists)
        {
            StopListening();
            return;
        }
        string who = _session.Players.FirstOrDefault(p => p.Slot == _listenSlot)?.Name ?? "They";
        if (!_remote.TryGetValue(_listenSlot, out var remote) || remote.Talk.Kind != TalkKind.Dialogue
            || remote.Talk.Place != OurPlayer.Place || !NpcOf(remote.Talk.NpcId).Persist().Equals(Instance.Of(_panel.Get("owner")).Persist()))
        {
            CloseListening(remote.Talk.Kind == TalkKind.Trade ? $"{who} is trading with {remote.Talk.NpcName}." : $"{who} finished talking.");
            return;
        }
        Fill();
    }

    private void Yield(TalkKind kind, Instance panel, Instance trade, Instance npc, int slot, TalkPacket theirs)
    {
        string who = _session.Players.FirstOrDefault(p => p.Slot == slot)?.Name ?? "Someone";
        _context.Log($"Talk: {who} got to {theirs.NpcName} first - ours yields");
        _ourNpc = default;
        if (kind == TalkKind.Dialogue && theirs.Kind == TalkKind.Dialogue)
        {
            // (Ours closed without a turn, and theirs opened in its place to listen.)
            panel.Set("is_monologue", true);
            panel.Set("dialogue_ended", false);
            panel.Destroy();
            Listen(slot, npc, who, theirs.NpcName);
            return;
        }
        if (panel.Exists)
        {
            panel.Set("is_monologue", true);
            panel.Set("dialogue_ended", false);
            panel.Destroy();
        }
        if (trade.Exists)
            trade.Destroy();
        Message(theirs.Kind == TalkKind.Trade ? $"{who} is trading with {theirs.NpcName}." : $"{who} is talking to {theirs.NpcName}.");
    }

    // Ours to everyone, as it changes (and again now and then, for anyone who's come since).
    private void Send(TalkKind kind, Instance panel, Instance npc)
    {
        string place = OurPlayer.Place ?? "";
        string name = "", text = "", last = "";
        var answers = new List<string>();
        var pressable = new List<bool>();
        if (kind != TalkKind.None && npc.Exists)
            name = Game.CallScript("scr_id_get_name", npc, npc).AsString;
        if (kind == TalkKind.Dialogue)
        {
            text = panel.Get("text").AsString;
            last = panel.Get("previous_answer").AsString;
            foreach (Instance button in Buttons(panel).OrderBy(b => b.Get("number").AsReal))
            {
                answers.Add(button.Get("name").AsString);
                pressable.Add(button.Get("canPress").AsBool);
            }
        }
        var packet = new TalkPacket(kind, place, IdOf(npc), name, text, last, answers.ToArray(), pressable.ToArray());
        string signature = $"{(int)kind}|{place}|{packet.NpcId}|{name}|{text}|{last}|{string.Join("\u0001", answers)}|{string.Join(",", pressable)}";
        long now = Environment.TickCount64;
        if (signature == _sent && (kind == TalkKind.None || now - _sentAt < ResendMs))
            return;
        _sent = signature;
        _sentAt = now;
        _session.Send(packet);
    }

    private void Receive(RemotePlayer from, TalkPacket packet)
    {
        long since = _remote.TryGetValue(from.Slot, out var before) && before.Talk.Kind != TalkKind.None
            && before.Talk.NpcId == packet.NpcId && before.Talk.Place == packet.Place ? before.Since : Environment.TickCount64;
        _remote[from.Slot] = (packet, since);
    }

    private static void Message(string text) => Game.CallScript("scr_actionsLogAddMessage", default, text);
}
