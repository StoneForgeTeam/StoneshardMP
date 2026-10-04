using System;
using System.Collections.Generic;
using System.Linq;
using StoneForge;
using StoneshardMP.Features.Players;
using StoneshardMP.Net;
using StoneshardMP.Net.Packets;

namespace StoneshardMP.Features.Party;

// Party frames: one per other player, down the right edge of the screen, in the game's own look - its tooltip frame,
// the HUD's health and energy bars and digits, their character's head as the portrait - with their name, level and
// status effects, and a compass needle pointing at them:
// - same place: straight at them, with how many moves away;
// - another world-map area: towards it, with how many areas away ("3a");
// - the same area but another room or dungeon floor: no needle - "in", or the floor they're on ("F2").
// A name in red: enemies are after them. A frame pulsing red: their health is low. Greyed: out cold, dead, or not in
// the world yet; "no word" when their state's stopped coming. Hovering one: their level, combat and ping.
// StoneForge UI on the HUD layer (ModUI.Hud): under the game's windows (inventory, map, dialogue), hidden with its HUD.
// A tab at the screen edge slides them away and back (remembered); the mod's settings turn them off. Each game sends
// the others what the frames need beyond its state (PartyPacket) when it changes.
// (Legacy: scr_mp_party_draw, scr_mp_send_party, o_mp_party_panel, o_mp_party_tab.)
public sealed class PartyFrames
{
    // Layout, in the UI's units.
    internal const double FrameWidth = 160, FrameHeight = 42, Gap = 4, RightMargin = 22, Top = 64;
    internal const double TabWidth = 14, TabHeight = 20;
    // (How far the frames slide away: their width, the margin, and their status icons to their left.)
    private const double Travel = FrameWidth + RightMargin + 100;
    private const int MaxEffects = 12;
    private static readonly TimeSpan SendEvery = TimeSpan.FromSeconds(0.5);
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(3);

    private readonly ModContext _context;
    private readonly Session _session;
    private readonly MpSettings _settings;
    private readonly Func<bool> _inSharedWorld;
    private readonly PartyTab _tab;
    private readonly Dictionary<int, PartyFrame> _frames = new();
    private readonly UIScreen _screen;
    // 0: shown, 1: slid off the right edge.
    private double _slide;
    private string _sent = "";
    private DateTime _nextSend, _sentAt;

    public PartyFrames(ModContext context, Session session, MpSettings settings, Func<bool> inSharedWorld)
    {
        _context = context;
        _session = session;
        _settings = settings;
        _inSharedWorld = inSharedWorld;
        _screen = context.UI.Hud;
        _tab = _screen.Add(new PartyTab(this) { Visible = false });
        session.On<PartyPacket>((from, packet) => from.Party = PartyInfo.Parse(packet.Values));
        // (A newcomer gets ours at once.)
        session.PlayerJoined += _ => _sent = "";
    }

    /// <summary>Whether they're slid away (the tab's arrow).</summary>
    internal bool Hidden => _settings.PartyHidden.Value;

    // Every frame.
    public void Tick()
    {
        bool inGame = Gm.InGame && _inSharedWorld() && _session.Mode != Session.SessionMode.Idle;
        if (inGame)
            SendOurs();
        bool show = inGame && _settings.PartyFrames.Value && _session.Players.Count > 0;
        double target = Hidden ? 1 : 0;
        _slide += (target - _slide) * 0.25;
        if (Math.Abs(_slide - target) < 0.01)
            _slide = target;
        Layout(show);
    }

    public void Clear()
    {
        Layout(false);
        _sent = "";
    }

    internal void Toggle()
    {
        _settings.PartyHidden.Value = !_settings.PartyHidden.Value;
        int sound = Gm.AssetGetIndex("snd_checkbox_on");
        if (sound >= 0)
            Game.CallBuiltin("audio_play_sound", sound, 4, false);
    }

    // A frame for each of the others (in slot order), slid as far as the tab says; none while they're not shown.
    private void Layout(bool show)
    {
        var players = show ? _session.Players.OrderBy(p => p.Slot).ToList() : new List<RemotePlayer>();
        foreach (int slot in _frames.Keys.Where(slot => players.All(p => p.Slot != slot)).ToList())
        {
            _screen.Remove(_frames[slot]);
            _frames.Remove(slot);
        }
        double x = Draw.Width - FrameWidth - RightMargin + Math.Round(_slide * Travel), y = Top;
        foreach (var player in players)
        {
            if (!_frames.TryGetValue(player.Slot, out var frame) || frame.Player != player)
            {
                if (frame != null)
                    _screen.Remove(frame);
                _frames[player.Slot] = frame = _screen.Add(new PartyFrame(player));
            }
            frame.X = x;
            frame.Y = y;
            frame.Visible = _slide < 1;
            y += FrameHeight + Gap;
        }
        _tab.Visible = show;
        _tab.X = Draw.Width - 6 - TabWidth;
        _tab.Y = Top + (FrameHeight - TabHeight) / 2;
    }

    // ---- ours, for the others ----

    private void SendOurs()
    {
        DateTime now = DateTime.UtcNow;
        if (now < _nextSend)
            return;
        _nextSend = now + SendEvery;
        if (Ours() is not { } info)
            return;
        string text = info.ToString();
        if (text == _sent && now - _sentAt < Heartbeat)
            return;
        _sent = text;
        _sentAt = now;
        _session.Send(new PartyPacket(text));
    }

    // What our frame shows on the others' screens beyond our state; null with no player.
    private PartyInfo? Ours()
    {
        Instance player = OurPlayer.Instance;
        if (player.IsNone)
            return null;
        try
        {
            int level = (int)Game.CallScript("scr_atr", default, "LVL").AsReal;
            GmValue head = Game.CallScript("scr_atr", default, "Head");
            float Cap(string name) => player.Get(name) is { Kind: GmKind.Real } cap ? (float)cap.AsReal : 100;
            bool combat = Game.CallScript("scr_getAgredMobsCount", default, true).AsReal > 0;
            return new PartyInfo(level, head.Kind == GmKind.String ? head.AsString : "", Cap("Health_Threshold"),
                Cap("Max_Energy_Threshold"), combat, Effects(player));
        }
        catch (Exception e)
        {
            _context.Log($"Party frame: couldn't read our player ({e.Message})");
            return null;
        }
    }

    // The status effects we're under that show an icon, by object name: harmful ones first, at most MaxEffects.
    private static string[] Effects(Instance player)
    {
        GmValue buffs = player.Get("buffs");
        if (buffs.Kind != GmKind.Real || !Game.CallBuiltin("ds_exists", buffs, 2).AsBool)
            return Array.Empty<string>();
        int invisible = Gm.AssetGetIndex("o_invisible_buff"), debuff = Gm.AssetGetIndex("o_debuff");
        var bad = new List<string>();
        var good = new List<string>();
        int count = Game.CallBuiltin("ds_list_size", buffs).AsInt;
        for (int i = 0; i < count; i++)
        {
            GmValue effect = Game.CallBuiltin("ds_list_find_value", buffs, i);
            if (!Game.CallBuiltin("instance_exists", effect).AsBool)
                continue;
            int obj = Game.CallBuiltin("variable_instance_get", effect, "object_index").AsInt;
            if ((invisible >= 0 && Gm.ObjectIsAncestor(obj, invisible)) || Game.CallBuiltin("object_get_sprite", obj).AsInt < 0)
                continue;
            (debuff >= 0 && Gm.ObjectIsAncestor(obj, debuff) ? bad : good).Add(Gm.ObjectGetName(obj));
        }
        return bad.Concat(good).Take(MaxEffects).ToArray();
    }
}
