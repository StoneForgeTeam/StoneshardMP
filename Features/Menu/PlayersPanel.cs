using System;
using System.Collections.Generic;
using System.Linq;
using StoneForge;
using StoneshardMP.Features.Join;
using StoneshardMP.Net;

namespace StoneshardMP.Features.Menu;

// The lobby, on the main menu while we're hosting or in someone's game: a card for each player - us first, then everyone
// else by slot - in the middle of the screen (clear of the game's title and menu on the right).
// - A card: the portrait of the character they'll play - its avatar, as the game's save menu shows it - and their name
//   (the host and us marked), and under it that character - its name, level and class - or a new character, the world
//   slot it is (WorldSlots), and their ping (the host's to each client; a client's to the host). Who that is comes from the
//   host (SlotRoster): from the save picked to play, on the menu.
// - The host picks which slot's character each player plays - a dropdown of the world's slots, each with its character:
//   a client's while they're not in the world yet (another slot trades with whoever has it; slot 0, the host's own
//   character, is the host taking theirs), its own on the menu, for the save it loads next (WorldSlots.Pick) - and kicks
//   a client: Kick, then Sure? within a few seconds (the client's told it was removed).
// - The host's card first, then ours, then the others by slot.
// - Over the cards: the save the host picked (on the menu), and, alone as the host, how to be joined.
// (The settings it once had beside it - name, join address, port, name tags - are on StoneshardMP's page in the Mods
// window; the join address is also asked by Join Game.)
public sealed class PlayersPanel : UIElement
{
    private const double PanelWidth = 330, Pad = 10, TitleHeight = 22, SaveLineHeight = 14, CardHeight = 64, CardGap = 6;
    private const double ButtonWidth = 52, ButtonHeight = 20, PickWidth = 104;
    // (The avatar: the save menu's portrait, 37x50, in a frame.)
    private const double PortraitWidth = 41, PortraitHeight = 54;
    // (How long Kick waits for its Sure?.)
    private const long ConfirmMs = 3000;
    private static readonly int Gold = Draw.Rgb(214, 186, 120), Grey = Draw.Rgb(140, 132, 120), Light = Draw.Rgb(225, 220, 205),
        CardBack = Draw.Rgb(24, 22, 33), CardEdge = Draw.Rgb(60, 54, 70), Inset = Draw.Rgb(12, 11, 18), You = Draw.Rgb(90, 74, 40),
        PingGood = Draw.Rgb(120, 200, 120), PingFair = Draw.Rgb(220, 190, 90), PingBad = Draw.Rgb(220, 90, 70);

    private readonly Session _session;
    private readonly MpSettings _settings;
    private readonly Func<string> _playerName;
    private readonly WorldSlots _slots;
    private readonly SlotRoster _roster;
    private readonly HostLobby _lobby;
    // Host on the main menu: the save picked, as a line under the title (worked out as it changes).
    private string? _pickedLine;
    private SaveFile? _pickedFor;
    private bool _pickedRead;
    // Host: each client's kick button, and when it was asked Sure? (0: not); and each player's choice of slot.
    private readonly Dictionary<int, (UIButton Button, long ArmedAt)> _kicks = new();
    private readonly Dictionary<int, UIDropdown> _picks = new();
    // A pick that couldn't be made, and why, shown over the cards a few seconds.
    private string? _notice;
    private long _noticeUntil;
    private const long NoticeMs = 5000;
    // Avatars by sprite name, looked up once (-1: none).
    private readonly Dictionary<string, int> _avatars = new();
    private bool _stale = true;

    public PlayersPanel(Session session, MpSettings settings, Func<string> playerName, WorldSlots slots, SlotRoster roster, HostLobby lobby)
    {
        _session = session;
        _settings = settings;
        _playerName = playerName;
        _slots = slots;
        _roster = roster;
        _lobby = lobby;
        // (The middle of the screen, a little left: clear of the game's title and menu on the right.)
        Anchor = UIAnchor.Center;
        X = -70;
        Y = 0;
        Width = PanelWidth;
        HitTest = false;
        Visible = false;
        // (Someone came or went, or we started or stopped: the buttons made again.)
        _session.Changed += () => _stale = true;
    }

    // The host first, then us, then the others by slot.
    private List<(int Slot, string Name, RemotePlayer? Player)> Rows()
    {
        var rows = new List<(int Slot, string Name, RemotePlayer? Player)> { (_session.Slot, _playerName(), null) };
        rows.AddRange(_session.Players.Select(p => (p.Slot, p.Name, (RemotePlayer?)p)));
        return rows.OrderBy(r => r.Slot == 0 ? 0 : r.Player == null ? 1 : 2).ThenBy(r => r.Slot).ToList();
    }

    // Alone as the host: a line saying how to be joined.
    private bool Waiting => _session.Mode == Session.SessionMode.Host && _session.Players.Count == 0;

    private bool Noticing => _notice != null && Environment.TickCount64 < _noticeUntil;
    private double CardsTop => Pad + TitleHeight + (_pickedLine != null ? SaveLineHeight + 4 : 0) + (Noticing ? 28 : 0);
    private double CardTop(int row) => CardsTop + row * (CardHeight + CardGap);

    protected override void OnUpdate(double deltaTime)
    {
        var rows = Rows();
        UpdatePickedLine();
        Height = CardsTop + rows.Count * (CardHeight + CardGap) - CardGap + (Waiting ? 22 : 0) + Pad;
        if (_stale)
            MakeButtons(rows);
        // (A client's slot only while they're not in the world yet - and ours on the main menu: it's for the save we
        // load next. Each slot named by its character, and each choice as it is now.)
        var options = SlotOptions();
        foreach (var (slot, pick) in _picks)
        {
            pick.Enabled = slot == _session.Slot ? Gm.InMainMenu : _slots.CanSwap(slot);
            if (pick.IsOpen)
                continue;
            if (!pick.Options.SequenceEqual(options))
            {
                pick.Options.Clear();
                pick.Options.AddRange(options);
            }
            pick.SelectedIndex = _slots.Plays(slot);
        }
        // (A Sure? not taken up: back to Kick.)
        long now = Environment.TickCount64;
        foreach (var (slot, kick) in _kicks.ToList())
            if (kick.ArmedAt != 0 && now - kick.ArmedAt > ConfirmMs)
            {
                kick.Button.Text = "Kick";
                _kicks[slot] = (kick.Button, 0);
            }
    }

    // The world's slots, each with its character: "0: Jonna, Lv 1", "1: new character".
    private List<string> SlotOptions()
    {
        var options = new List<string>();
        for (int world = 0; world < Math.Max(1, _session.Limit); world++)
        {
            var character = _roster.CharacterOf(world);
            options.Add(!_roster.Known ? $"Slot {world}"
                : character == null ? $"{world}: new character"
                : $"{world}: {character.Name}" + (character.Level > 0 ? $", Lv {character.Level}" : ""));
        }
        return options;
    }

    // Host: the choice of slot (and, on a client's card, Kick) at the card's right.
    private void MakeButtons(List<(int Slot, string Name, RemotePlayer? Player)> rows)
    {
        _stale = false;
        foreach (var (button, _) in _kicks.Values)
            Remove(button);
        _kicks.Clear();
        foreach (var pick in _picks.Values)
            Remove(pick);
        _picks.Clear();
        if (_session.Mode != Session.SessionMode.Host)
            return;
        double right = Width - Pad - 6;
        for (int i = 0; i < rows.Count; i++)
        {
            var (slot, name, player) = rows[i];
            double top = CardTop(i);
            var pick = Add(new UIDropdown(SlotOptions(), right - PickWidth, top + 8, PickWidth, _slots.Plays(slot))
            {
                Tooltip = player == null
                    ? "Which slot's character you play: the next save you load trades yours with it (and your stashes), so "
                        + "whoever plays that slot gets yours. On the main menu, before you load."
                    : $"Which slot's character {name} plays - and its stash - trading places with whoever has it (slot 0: "
                        + "yours, and you take theirs). Only before they're in the world.",
            });
            pick.Changed += index => Pick(slot, name, player == null, index);
            _picks[slot] = pick;
            if (player == null)
                continue;
            var kick = Add(new UIButton("Kick", right - ButtonWidth, top + CardHeight - 8 - ButtonHeight, ButtonWidth, ButtonHeight)
            {
                Tooltip = $"Remove {name} from the game",
            });
            kick.Clicked += _ => Kick(slot, name);
            _kicks[slot] = (kick, 0);
        }
    }

    // A player's choice of slot. Trading characters with the host's (slot 0, or the host picking another) needs a
    // character on the other side: the host can't be left with none - the save it loads has to have one. So a slot with
    // no character yet can't be traded with the host; anything else goes to WorldSlots.
    private void Pick(int slot, string name, bool ours, int target)
    {
        if (_roster.Known)
        {
            int traded = ours ? target : target == 0 ? _slots.Of(slot) : -1;
            if (traded > 0 && _roster.CharacterOf(traded) == null)
            {
                Notice(ours
                    ? $"Slot {traded} has no character yet: you'd have none to play. Pick a slot with a character."
                    : $"{name}'s slot has no character yet to trade for yours: you'd have none to play. Have them make one first.");
                return;
            }
        }
        if (!_slots.Pick(slot, target))
            Notice(ours ? "You can only change your slot on the main menu, before you load."
                : $"{name} is already in the world: their slot can't change now.");
    }

    private void Notice(string text)
    {
        _notice = text;
        _noticeUntil = Environment.TickCount64 + NoticeMs;
    }

    // Kick, then Sure? to do it.
    private void Kick(int slot, string name)
    {
        if (!_kicks.TryGetValue(slot, out var kick))
            return;
        if (kick.ArmedAt == 0)
        {
            kick.Button.Text = "Sure?";
            _kicks[slot] = (kick.Button, Environment.TickCount64);
            return;
        }
        _session.Kick(slot);
    }

    private void UpdatePickedLine()
    {
        if (_session.Mode != Session.SessionMode.Host || !Gm.InMainMenu)
        {
            if (_pickedLine != null)
                _stale = true;
            _pickedLine = null;
            return;
        }
        var picked = _lobby.Picked;
        if (_pickedRead && Equals(picked, _pickedFor))
            return;
        _pickedRead = true;
        _pickedFor = picked;
        _stale = true;
        _pickedLine = picked == null ? "Pick a save: Continue or Load Game, then Play"
            : "Save: " + (picked.Info?.CharacterName ?? picked.Slot.Name) + " - choose slots, then Play";
    }

    private int Avatar(string name)
    {
        if (!_avatars.TryGetValue(name, out int sprite))
            _avatars[name] = sprite = Gm.AssetGetIndex(name);
        return sprite;
    }

    protected override void OnDraw(double x, double y)
    {
        Draw.Frame(x, y, Width, Height);
        var rows = Rows();
        string limit = _session.Mode == Session.SessionMode.Host ? $"/{_session.Limit}" : "";
        Draw.Text(x + Pad, y + Pad + 2, _session.Mode == Session.SessionMode.Host ? "Your game" : "In the game", Gold);
        Draw.Text(x + Width - Pad, y + Pad + 2, $"{rows.Count}{limit} players", Grey, Draw.AlignRight);
        if (_pickedLine != null)
            Draw.Text(x + Pad, y + Pad + TitleHeight, Clip(_pickedLine, Width - Pad * 2), Grey);
        if (Noticing)
            Draw.TextWrapped(x + Pad, y + CardsTop - 28, _notice!, Width - Pad * 2, PingFair);
        for (int i = 0; i < rows.Count; i++)
            DrawCard(x + Pad, y + CardTop(i), Width - Pad * 2, rows[i]);
        if (Waiting)
            Draw.Text(x + Width / 2, y + CardTop(rows.Count) + 4, $"Waiting for players - UDP port {_settings.Port.Value:0}", Grey,
                Draw.AlignCenter, Draw.AlignTop);
    }

    // One player's card: their character's portrait, their name, that character, its slot, their ping.
    private void DrawCard(double x, double y, double width, (int Slot, string Name, RemotePlayer? Player) row)
    {
        var (slot, name, player) = row;
        bool ours = player == null;
        Draw.Rectangle(x, y, x + width, y + CardHeight, CardBack);
        Draw.Rectangle(x, y, x + width, y + CardHeight, ours ? You : CardEdge, outline: true);

        // The portrait: the character's avatar, as the save menu shows it; a new character's is the game's default, dim.
        SlotRow? slotRow = _roster.Of(slot);
        SlotCharacter? character = slotRow?.Character;
        double px = x + 5, py = y + (CardHeight - PortraitHeight) / 2;
        Draw.Rectangle(px, py, px + PortraitWidth, py + PortraitHeight, Inset);
        Draw.Rectangle(px, py, px + PortraitWidth, py + PortraitHeight, CardEdge, outline: true);
        int avatar = Avatar(character?.Avatar ?? "s_Default");
        if (avatar >= 0)
            Draw.SpriteExt(avatar, 0, px + 2, py + 2, alpha: character != null ? 1 : 0.35);

        // Their name, and what they are here.
        double tx = px + PortraitWidth + 8, textRight = x + width - (_picks.ContainsKey(slot) ? PickWidth + 12 : 6);
        string shown = Clip(name, textRight - tx - 60);
        Draw.Text(tx, y + 7, shown, ours ? Gold : Light);
        var tags = new List<string>();
        if (slot == 0)
            tags.Add("host");
        if (ours)
            tags.Add("you");
        if (tags.Count > 0)
            Draw.Text(tx + Draw.TextWidth(shown) + 6, y + 7, string.Join(", ", tags), Grey);

        // Who they'll play.
        // (Nothing to say yet - no save picked: which character is the save's to say.)
        string who = slotRow == null || !slotRow.Known ? "Character: once a save is picked" : character == null ? "New character"
            : character.Level > 0 ? $"{character.Name} - level {character.Level}" : character.Name;
        if (who.Length > 0)
            Draw.Text(tx, y + 24, Clip(who, textRight - tx), character != null ? Light : Grey);
        if (character is { Class.Length: > 0 })
            Draw.Text(tx, y + 37, Clip(character.Class, textRight - tx), Grey);

        // Their world slot, and their ping.
        if (slotRow != null)
            Draw.Text(tx, y + CardHeight - 13, $"slot {slotRow.World}", Grey);
        // (In line with Kick, to its left - where Kick would be, on a client's screen.)
        if (player is { Ping: >= 0 } p)
            Draw.Text(x + width - 6 - (_kicks.ContainsKey(slot) ? ButtonWidth + 8 : 0), y + CardHeight - 8 - ButtonHeight / 2,
                $"{p.Ping} ms", p.Ping < 80 ? PingGood : p.Ping < 200 ? PingFair : PingBad, Draw.AlignRight, Draw.AlignMiddle);
    }

    private static string Clip(string text, double width)
    {
        while (text.Length > 4 && Draw.TextWidth(text) > width)
            text = text[..^2] + ".";
        return text;
    }
}
