using System;
using System.Collections.Generic;
using StoneForge;
using StoneshardMP.Features.Players;
using StoneshardMP.Net;

namespace StoneshardMP.Features.Party;

// One player's frame (see PartyFrames).
internal sealed class PartyFrame : UIElement
{
    // (Their state older than this: they've gone quiet.)
    private const long QuietMs = 5000;
    internal static readonly int Gold = Draw.Rgb(214, 186, 120), Grey = Draw.Rgb(140, 132, 120), Dark = Draw.Rgb(17, 16, 26),
        CombatRed = Draw.Rgb(230, 110, 80), Coma = Draw.Rgb(200, 90, 200), NeedleBack = Draw.Rgb(120, 90, 50),
        LowRed = Draw.Rgb(210, 50, 40), Dimmed = Draw.Rgb(64, 64, 64);
    // Assets by name (heads, bars, effect icons), looked up once: -1 for none.
    private static readonly Dictionary<string, int> Assets = new();

    public PartyFrame(RemotePlayer player)
    {
        Player = player;
        Width = PartyFrames.FrameWidth;
        Height = PartyFrames.FrameHeight;
    }

    public RemotePlayer Player { get; }

    protected override void OnUpdate(double deltaTime)
    {
        // Hovered: their level, whether they're fighting, and their ping (the host's to them, or ours to the host).
        var lines = new List<string> { Player.Name };
        if (Player.Party is { Level: > 0 } party)
            lines.Add($"Level {party.Level}" + (party.InCombat ? " - in combat" : ""));
        if (Player.Ping >= 0)
            lines.Add($"Ping {Player.Ping} ms");
        Tooltip = string.Join("\n", lines);
    }

    protected override void OnDraw(double x, double y)
    {
        PlayerState? state = Player.State;
        PartyInfo? party = Player.Party;
        bool inWorld = state != null;
        bool quiet = inWorld && Environment.TickCount64 - Player.StateAt > QuietMs;
        bool dead = inWorld && state!.Health <= 0;
        bool coma = inWorld && party?.Unconscious == true;
        double maxHealth = 1, healthPart = 0;
        if (inWorld)
        {
            maxHealth = Math.Max(1, Math.Round(state!.MaxHealth * (party?.HealthCap ?? 100) / 100));
            healthPart = Math.Clamp(state.Health / maxHealth, 0, 1);
        }

        Draw.Frame(x, y, Width, Height);
        // (Low health, still standing: the frame pulses red.)
        if (inWorld && !dead && !quiet && healthPart < 0.3)
        {
            double pulse = 0.35 + 0.35 * Math.Sin(Environment.TickCount64 / 160.0);
            Draw.Rectangle(x + 1, y + 1, x + Width - 2, y + Height - 2, LowRed, pulse, outline: true);
            Draw.Rectangle(x + 2, y + 2, x + Width - 3, y + Height - 3, LowRed, pulse * 0.6, outline: true);
        }

        // Portrait: their head, in a dark inset.
        Draw.Rectangle(x + 4, y + 4, x + 35, y + Height - 5, Dark);
        if (party is { Head.Length: > 0 } && Asset(party.Head + "_normal") is >= 0 and int head)
            Draw.SpriteExt(head, 0, x + 20, y + 38, colour: dead || coma || !inWorld || quiet ? Dimmed : Draw.White);

        // Name and level (or out cold).
        string name = Player.Name.Length > 14 ? Player.Name[..13] + "." : Player.Name;
        Text(x + 40, y + 5, name, party?.InCombat == true && inWorld ? CombatRed : Gold, Draw.AlignLeft, Draw.AlignTop, GameFont.Default);
        if (coma)
            Text(x + Width - 30, y + 5, "Unconscious", Coma, Draw.AlignRight, Draw.AlignTop, GameFont.Default);
        else if (party is { Level: > 0 })
            Text(x + Width - 30, y + 5, "Lv " + party.Level, Grey, Draw.AlignRight, Draw.AlignTop, GameFont.Default);

        // Their status effects: the game's icons, half size, in rows of 6 to the left of the frame.
        if (inWorld && party != null)
            for (int i = 0; i < party.Effects.Length; i++)
                if (EffectIcon(party.Effects[i]) is >= 0 and int icon)
                    Draw.SpriteExt(icon, 0, x - 9 - i % 6 * 15, y + 12 + i / 6 * 15, 0.5, 0.5);

        double barWidth = Width - 40 - 30;
        if (inWorld)
        {
            double maxEnergy = Math.Max(1, Math.Round(state!.MaxEnergy * (party?.EnergyCap ?? 100) / 100));
            Bar(x + 40, y + 18, barWidth, healthPart, "s_holdbar", "s_healthbar", $"{Math.Round(state.Health)}/{maxHealth}");
            Bar(x + 40, y + 29, barWidth, state.Energy / maxEnergy, "s_holdbar_mana", "s_hp_n", $"{Math.Round(state.Energy)}/{maxEnergy}");
            if (quiet)
                Text(x + 40 + barWidth / 2, y + 26, "no word", Grey, Draw.AlignCenter, Draw.AlignMiddle, GameFont.Default);
        }
        else
            Text(x + 40, y + 22, "not in the world yet", Grey, Draw.AlignLeft, Draw.AlignTop, GameFont.Default);

        DrawCompass(x + Width - 15, y + 25, y + Height - 3, inWorld && !quiet ? state : null, OurPlayer.State());
    }

    // The needle and its label: which way they are, and how far (see PartyFrames).
    private static void DrawCompass(double cx, double cy, double labelY, PlayerState? theirs, PlayerState? mine)
    {
        Draw.Circle(cx, cy, 10, Dark);
        Draw.Circle(cx, cy, 10, Grey, outline: true);
        if (theirs == null || mine == null)
            return;
        var (myInside, myArea) = Split(mine.Place);
        var (theirInside, theirArea) = Split(theirs.Place);
        double? direction = null;
        string label;
        if (myInside == theirInside && myArea == theirArea)
        {
            Cell step = theirs.Cell - mine.Cell;
            int moves = mine.Cell.DistanceTo(theirs.Cell);
            if (moves > 0)
                direction = Direction(step.X, step.Y);
            label = moves.ToString();
        }
        else if (myArea is { } from && theirArea is { } to && from != to)
        {
            direction = Direction(to.X - from.X, to.Y - from.Y);
            label = Math.Max(Math.Abs(to.X - from.X), Math.Abs(to.Y - from.Y)) + "a";
        }
        else
        {
            int floor = FloorOf(theirInside);
            label = floor > 0 ? "F" + floor : "in";
        }
        if (direction is { } angle)
        {
            Point At(double length, double degrees)
                => new(cx + length * Math.Cos(degrees * Math.PI / 180), cy - length * Math.Sin(degrees * Math.PI / 180));
            var tip = At(8, angle);
            var left = At(4, angle + 140);
            var right = At(4, angle - 140);
            var tail = At(5, angle + 180);
            Draw.Triangle(tip.X, tip.Y, left.X, left.Y, right.X, right.Y, Gold);
            Draw.Triangle(tail.X, tail.Y, left.X, left.Y, right.X, right.Y, NeedleBack);
        }
        else
            Draw.Circle(cx, cy, 2, Gold);
        Text(cx, labelY, label, Grey, Draw.AlignCenter, Draw.AlignBottom, GameFont.Digits);
    }

    // GameMaker's point_direction for a step (dx, dy): degrees, anticlockwise from right, y down.
    private static double Direction(double dx, double dy) => Math.Atan2(-dy, dx) * 180 / Math.PI;

    // A place (WorldMap.Place: "room#f2@12_7") as the room and floor, and the world-map area (none in the prologue).
    private static (string Inside, WorldTile? Area) Split(string place)
    {
        int at = place.LastIndexOf('@');
        if (at < 0)
            return (place, null);
        string[] cell = place[(at + 1)..].Split('_');
        return cell.Length == 2 && int.TryParse(cell[0], out int x) && int.TryParse(cell[1], out int y)
            ? (place[..at], new WorldTile(x, y))
            : (place, null);
    }

    private static int FloorOf(string inside)
    {
        int at = inside.LastIndexOf("#f", StringComparison.Ordinal);
        return at >= 0 && int.TryParse(inside[(at + 2)..], out int floor) ? floor : 0;
    }

    // One bar, drawn as the HUD draws its own (scr_healbar): the empty bar, the filled part up to `part`, and
    // "value/max" in the digit font on top - squeezed to `width`.
    private static void Bar(double x, double y, double width, double part, string back, string fill, string text)
    {
        int backSprite = Asset(back), fillSprite = Asset(fill);
        if (backSprite < 0 || fillSprite < 0)
            return;
        double backWidth = Draw.SpriteWidth(backSprite), fillWidth = Draw.SpriteWidth(fillSprite), height = Draw.SpriteHeight(fillSprite);
        Draw.SpritePart(backSprite, 0, 0, 0, backWidth, height, x, y, width / backWidth);
        double filled = Math.Clamp(Math.Round(fillWidth * Math.Clamp(part, 0, 1)), 0, fillWidth);
        if (filled > 0)
            Draw.SpritePart(fillSprite, 0, 0, 0, filled, height, x, y, width / fillWidth);
        Text(x + width / 2, y + height / 2, text, Draw.White, Draw.AlignCenter, Draw.AlignMiddle, GameFont.Digits);
    }

    // The game's text in one of its fonts (a global's name), at its usual half size, with its shadow.
    private static void Text(double x, double y, string text, int colour, int halign, int valign, GameFont font)
        => Draw.Text(x, y, text, colour, halign, valign, font);

    private static int EffectIcon(string effect)
    {
        string key = "icon:" + effect;
        if (!Assets.TryGetValue(key, out int icon))
            Assets[key] = icon = UnitEffects.IconOf(effect);
        return icon;
    }

    private static int Asset(string name)
    {
        if (!Assets.TryGetValue(name, out int index))
            Assets[name] = index = Gm.AssetGetIndex(name);
        return index;
    }
}
