using System;
using System.Collections.Generic;
using StoneForge;
using StoneshardMP.Features.Players;
using StoneshardMP.Net;

namespace StoneshardMP.Features.Rounds;

// The turn order while we're in a shared round (TurnRounds), under our status effects, centred: a carousel of portraits -
// whose turn it is in the centre, framed; who's next to its right and who just went to its left, smaller and fading -
// turning right as the turn moves on (whose turn is always the round's as it is now; only the turning is eased). The round is every player in it in slot order (the same in every game), then the
// enemies; their portrait is the nearest enemy after us. Below, in the game's small UI font: YOUR TURN / <NAME>'S TURN
// / ENEMIES' TURN, and why play is turn-based. On the HUD layer, under the game's windows. (Legacy: scr_mp_turnorder_draw.)
internal sealed class TurnOrder : UIElement
{
    private const double BoxWidth = 40, BoxHeight = 46, Gap = 50;
    private static readonly int Dark = Draw.Rgb(17, 16, 26), Gray = Draw.Rgb(128, 128, 128), Lime = Draw.Rgb(0, 255, 0),
        Orange = Draw.Rgb(255, 160, 64), Yellow = Draw.Rgb(255, 255, 0), WhyColour = Draw.Rgb(200, 196, 186);

    private readonly TurnRounds _rounds;
    private readonly Session _session;
    private readonly Func<int> _playerObject;
    // The carousel: how many portraits, and where it's turned to (eased towards whose turn it is).
    private int _count;
    private double _pos;
    // Assets by name, looked up once (-1: none).
    private readonly Dictionary<string, int> _assets = new();

    public TurnOrder(TurnRounds rounds, Session session, Func<int> playerObject)
    {
        _rounds = rounds;
        _session = session;
        _playerObject = playerObject;
        HitTest = false;
    }

    protected override void OnDraw(double x, double y)
    {
        if (!_rounds.InRound)
        {
            _count = 0;
            return;
        }
        var seats = _rounds.Seats;
        int count = seats.Count + 1, enemies = count - 1;
        int want = enemies;
        for (int i = 0; i < seats.Count; i++)
            if (!seats[i].Acted)
            {
                want = i;
                break;
            }
        // (Whose turn it is now - shown at once: stepping through each turn for a moment fell behind fast rounds, and
        // showed a turn that had gone.)
        int current = want;
        if (_count != count)
        {
            _count = count;
            _pos = current;
        }

        string title;
        int titleColour;
        if (current == enemies)
            (title, titleColour) = ("ENEMIES' TURN", Orange);
        else if (seats[current].Slot == _session.Slot)
            (title, titleColour) = ("YOUR TURN", Lime);
        else
            (title, titleColour) = (_rounds.NameOf(seats[current].Slot).ToUpperInvariant() + "'S TURN", Yellow);

        // The carousel turns towards the current one (forward, the short way round).
        double diff = ((current - _pos) % count + count) % count;
        if (diff > count / 2.0)
            diff -= count;
        _pos += diff * 0.18;
        if (Math.Abs(diff) < 0.01)
            _pos = current;
        _pos = (_pos % count + count) % count;

        double cx = Draw.Width / 2, top = Top(), midY = top + BoxHeight / 2;
        int enemyIcon = EnemyIcon();
        // Far to near, so the centre one sits on top.
        for (int pass = 2; pass >= 0; pass--)
            for (int i = 0; i < count; i++)
            {
                double off = ((i - _pos) % count + count) % count;
                if (off > count / 2.0)
                    off -= count;
                // (Two to the right - who comes next - and one to the left, who just went.)
                if (off < -1.5 || off > 2.5)
                    continue;
                double ad = Math.Abs(off);
                if ((pass == 0 && ad >= 0.5) || (pass == 1 && (ad < 0.5 || ad >= 1.5)) || (pass == 2 && ad < 1.5))
                    continue;
                double scale = Math.Max(0.55, 1 - 0.22 * ad), alpha = Math.Clamp(1 - 0.35 * ad, 0.25, 1);
                double w = Math.Round(BoxWidth * scale), h = Math.Round(BoxHeight * scale);
                double bx = Math.Round(cx + off * Gap - w / 2), by = Math.Round(midY - h / 2);
                Draw.Frame(bx, by, w, h, alpha);
                Draw.Rectangle(bx + 3, by + 3, bx + w - 4, by + h - 4, Dark, alpha);
                int sprite = i == enemies ? enemyIcon : Head(seats[i].Slot);
                if (i == enemies && !Draw.SpriteExists(sprite))
                    sprite = Asset("s_loot_skull");
                if (Draw.SpriteExists(sprite))
                {
                    // (Fitted into the inset, centred on its drawn box.)
                    double sw = Draw.SpriteWidth(sprite), sh = Draw.SpriteHeight(sprite);
                    double fit = Math.Min((w - 8) / Math.Max(1, sw), (h - 8) / Math.Max(1, sh));
                    if (i != enemies)
                        fit = Math.Min(fit, scale);
                    var origin = Draw.SpriteOrigin(sprite);
                    double ox = (origin.X - sw / 2) * fit, oy = (origin.Y - sh / 2) * fit;
                    Draw.SpriteExt(sprite, 0, bx + w / 2 + ox, by + h / 2 + oy, fit, fit, 0, i == current ? Draw.White : Gray, alpha);
                }
            }
        // A marker over the centre one; whose turn, and why.
        Draw.Rectangle(cx - BoxWidth / 2, top - 3, cx + BoxWidth / 2 - 1, top - 2, titleColour);
        double ty = top + BoxHeight + 4;
        Text(cx, ty, title, titleColour);
        if (Why() is { Length: > 0 } why)
            Text(cx, ty + 11, why, WhyColour);
    }

    // Why play is turn-based: our own reason first, else the first other player's in the round.
    private string Why()
    {
        if (_rounds.Reason != TurnReason.None)
            return TurnReasons.Say(_rounds.Reason, null);
        foreach (var seat in _rounds.Seats)
            if (seat.Slot != _session.Slot && (TurnReason)seat.Reason != TurnReason.None)
                return TurnReasons.Say((TurnReason)seat.Reason, _rounds.NameOf(seat.Slot));
        return "";
    }

    // Under our status effects (the lowest of their icons in the top third of the screen) - at least a row down.
    private double Top()
    {
        double top = 40;
        Instance origin = Instance.Of(Game.Global["guiBaseContainerVisible"]);
        Instance player = OurPlayer.Instance;
        int states = Asset("c_abstract_states");
        if (origin.IsNone || player.IsNone || states < 0)
            return top;
        double originY = origin.Get("y").AsReal, limit = Draw.Height / 3;
        foreach (Instance state in Instances.All(states))
        {
            if (!state.Get("visible").AsBool || !UnitIs(state.Get("target"), player))
                continue;
            int sprite = state.Get("sprite_index").AsInt;
            if (!Draw.SpriteExists(sprite))
                continue;
            double y0 = state.Get("y").AsReal - originY;
            double bottom = y0 - Draw.SpriteOrigin(sprite).Y * state.Get("image_yscale").AsReal
                + Draw.SpriteHeight(sprite) * state.Get("image_yscale").AsReal;
            if (y0 < limit && bottom + 8 > top)
                top = bottom + 8;
        }
        return top;
    }

    // The nearest enemy after us: its portrait, or its sprite (-1: none).
    private int EnemyIcon()
    {
        Instance player = OurPlayer.Instance;
        if (player.IsNone)
            return -1;
        double px = player.Get("x").AsReal, py = player.Get("y").AsReal, best = double.MaxValue;
        int icon = -1, playerObject = _playerObject();
        foreach (Instance unit in Instances.All(GameObjectId.o_enemy))
        {
            if (unit.Get("object_index").AsInt == playerObject || !unit.Get("visible").AsBool
                || !StoneForge.Player.IsHuntedBy(unit))
                continue;
            double distance = Math.Sqrt(Math.Pow(unit.Get("x").AsReal - px, 2) + Math.Pow(unit.Get("y").AsReal - py, 2));
            if (distance >= best)
                continue;
            int avatar = unit.Get("avatar") is { Kind: GmKind.Real } a ? a.AsInt : -1;
            int sprite = Draw.SpriteExists(avatar) ? avatar : unit.Get("sprite_index").AsInt;
            if (Draw.SpriteExists(sprite))
            {
                best = distance;
                icon = sprite;
            }
        }
        return icon;
    }

    // A player's head, as the party frames show it: ours from the game, another's from what they sent.
    private int Head(int slot)
    {
        string head = "";
        if (slot == _session.Slot)
            head = StoneForge.Player.Attribute("Head") is { Kind: GmKind.String } h ? h.AsString : "";
        else
            foreach (var player in _session.Players)
                if (player.Slot == slot)
                    head = player.Party?.Head ?? "";
        return head.Length > 0 ? Asset(head + "_normal") : -1;
    }

    private static bool UnitIs(GmValue value, Instance unit) => Instance.Of(value) is var other && !other.IsNone
        && other.Persist().Equals(unit.Persist());

    private static void Text(double x, double y, string text, int colour)
        => Draw.Text(x, y, text, colour, Draw.AlignCenter, Draw.AlignTop, GameFont.Default);

    private int Asset(string name)
    {
        if (!_assets.TryGetValue(name, out int index))
            _assets[name] = index = Gm.AssetGetIndex(name);
        return index;
    }
}
