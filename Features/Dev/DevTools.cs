using System;
using System.Collections.Generic;
using System.Linq;
using StoneForge;
using StoneshardMP.Features.Areas;
using StoneshardMP.Features.Players;
using StoneshardMP.Net;
using Line = StoneshardMP.Features.Dev.DevPanel.Line;
using DevAction = StoneshardMP.Features.Dev.DevPanel.Action;

namespace StoneshardMP.Features.Dev;

// StoneshardMP's dev tools - only for the mod's contributors (mod.json's "contributors", StoneForge's IsContributor):
// Ctrl+Shift+M opens a window at the right of the screen, on the menu and in the game. Its tabs:
// - Session: who's connected, their versions, pings, places, how fresh their state is; our slot and status.
// - Area: the place we're in - its owner, who follows, and each feature's state (what it's holding, waiting for).
// - Inspect: the unit under the mouse (or one pinned) - its object, cell, health, AI state, sync ids - and the ground
//   effects and items on its cell.
// - Net: what's sent and received - totals, the rate, the biggest packet types each way, the latest packets - and a
//   simulated delay on what we send, to try a slow connection.
// - Desync: compare our units with the other games' here (DesyncCheck), differences listed and marked on the map.
// - Log: what the dev tools and the desync check noted (DevLog).
// - Tools: the world overlay's parts (sync ids over units, the cell under the mouse, desync marks), the debug dump,
//   starting the place's syncing over, teleporting (to the mouse, to a player), passing a turn.
// Every tab has Copy: its lines to the clipboard, to paste somewhere.
public sealed class DevTools
{
    private static readonly string[] TabNames = { "Session", "Area", "Inspect", "Net", "Desync", "Log", "Tools" };
    private const int SessionTab = 0, AreaTab = 1, InspectTab = 2, NetTab = 3, DesyncTab = 4, LogTab = 5, ToolsTab = 6;
    private const int DelayStep = 50, DelayMax = 1000;
    private static readonly int Text = Draw.Rgb(225, 220, 205), Head = Draw.Rgb(214, 186, 120), Dim = Draw.Rgb(140, 132, 120),
        Good = Draw.Rgb(120, 200, 120), Warn = Draw.Rgb(220, 190, 90), Bad = Draw.Rgb(230, 90, 70);

    private readonly ModContext _context;
    private readonly DevHooks _hooks;
    private readonly DevPanel _panel;
    private readonly DesyncCheck _desync;
    private readonly DevOverlay _overlay;
    private bool? _contributor;
    private Instance _pinned;

    public DevTools(ModContext context, DevHooks hooks)
    {
        _context = context;
        _hooks = hooks;
        _desync = new DesyncCheck(hooks.Session, hooks.Ownership, hooks.AreaUnits);
        _overlay = new DevOverlay(hooks.Ownership, hooks.AreaUnits, _desync);
        _panel = context.UI.Always.Add(new DevPanel(TabNames, Lines, Actions));
        DevLog.Echo = context.Log;
        context.DrawGui += () =>
        {
            if (_panel.Visible)
                _overlay.Render();
        };
    }

    private bool Contributor => _contributor ??= _context.IsContributor;

    public void Tick()
    {
        if (!(Keyboard.Down(Keyboard.Control) && Keyboard.Down(Keyboard.Shift) && Keyboard.Pressed('M')))
            return;
        // (Asked again each time while it says no: Steam may not have been ready.)
        if (_contributor == false)
            _contributor = null;
        if (!Contributor)
            return;
        _panel.Visible = !_panel.Visible;
        _panel.Open(_panel.Tab);
    }

    // ---- tabs ----

    private List<Line> Lines(int tab) => tab switch
    {
        SessionTab => SessionLines(),
        AreaTab => AreaLines(),
        InspectTab => InspectLines(),
        NetTab => NetLines(),
        DesyncTab => DesyncLines(),
        LogTab => DevLog.Recent.Select(l => new Line(l, Text)).DefaultIfEmpty(new Line("Nothing yet.", Dim)).ToList(),
        _ => ToolsLines(),
    };

    private List<DevAction> Actions(int tab)
    {
        var actions = new List<DevAction>();
        switch (tab)
        {
            case AreaTab:
                actions.Add(new("Resync area", Resync, "Drop what every feature holds for this place: each asks the owner again"));
                break;
            case InspectTab:
                actions.Add(new(_pinned.IsNone ? "Pin" : "Unpin", () => _pinned = _pinned.IsNone ? Mouse.Unit.Persist() : default,
                    "Keep showing the unit under the mouse now"));
                actions.Add(new("Teleport here", TeleportToMouse, "Move our player to the cell under the mouse (it must be free)"));
                break;
            case NetTab:
                var session = _hooks.Session;
                actions.Add(new($"Delay -{DelayStep}", () => SetDelay(session.SimulatedDelayMs - DelayStep)));
                actions.Add(new($"Delay +{DelayStep}", () => SetDelay(session.SimulatedDelayMs + DelayStep),
                    "Hold back everything we send this long - ask the others to set it too for a delay both ways"));
                actions.Add(new(NetStats.LogChatty ? "Hide chatty" : "Show chatty", () => NetStats.LogChatty = !NetStats.LogChatty,
                    "List player states, pings and unit rosters among the latest packets too"));
                actions.Add(new("Reset stats", NetStats.Reset));
                break;
            case DesyncTab:
                actions.Add(new("Run check", _desync.Run, "Ask the others here for their units and compare"));
                actions.Add(new(Toggle("Marks", _overlay.ShowFindings), () => _overlay.ShowFindings = !_overlay.ShowFindings,
                    "Mark the differences on the map (while this window's open)"));
                break;
            case LogTab:
                actions.Add(new("Clear", DevLog.Clear));
                break;
            case ToolsTab:
                actions.Add(new(Toggle("Sync ids", _overlay.ShowUnits), () => _overlay.ShowUnits = !_overlay.ShowUnits,
                    "Over each unit: its sync id - green the owner's, blue a bound copy, grey a copy the owner didn't send"));
                actions.Add(new(Toggle("Cell", _overlay.ShowCell), () => _overlay.ShowCell = !_overlay.ShowCell, "The cell under the mouse"));
                actions.Add(new("Write dump", () => { _hooks.Dump(); DevLog.Add("Dump written (see the log for where)"); },
                    "What's around us, to a file per game (Ctrl+Shift+D)"));
                actions.Add(new("Resync area", Resync, "Drop what every feature holds for this place: each asks the owner again"));
                actions.Add(new("Teleport here", TeleportToMouse, "Move our player to the cell under the mouse (it must be free)"));
                actions.Add(new("Pass a turn", () => { Turns.PassWorld(); DevLog.Add("Passed a world turn"); }, "One idle world turn, as a walked tile gives"));
                foreach (var player in _hooks.Session.Players.OrderBy(p => p.Slot))
                    actions.Add(new($"To {Clip(player.Name, 10)}", () => TeleportTo(player), $"Teleport next to {player.Name} (same place only)"));
                break;
        }
        actions.Add(new("Copy", Copy, "This tab's lines to the clipboard"));
        return actions;
    }

    private static string Toggle(string name, bool on) => $"{name}: {(on ? "on" : "off")}";

    private static string Clip(string text, int length) => text.Length > length ? text[..(length - 1)] + "." : text;

    private List<Line> SessionLines()
    {
        var session = _hooks.Session;
        var lines = new List<Line>
        {
            new($"Mode {session.Mode}, slot {session.Slot}, protocol {Session.Protocol}, StoneshardMP {_context.Manifest.Version}", Head),
            new(session.Status, Text),
            new($"In the shared world: {_hooks.InSharedWorld()}   delay sim: {session.SimulatedDelayMs} ms", Text),
        };
        var ours = OurPlayer.State();
        lines.Add(new($"Us: {ours?.Place ?? "(no place)"} cell {ours?.CellX},{ours?.CellY}", Text));
        lines.Add(new("", Text));
        lines.Add(new($"Players ({session.Players.Count})", Head));
        long now = Environment.TickCount64;
        foreach (var player in session.Players.OrderBy(p => p.Slot))
        {
            var state = player.State;
            long age = player.StateAt > 0 ? now - player.StateAt : -1;
            bool samePlace = state?.Place != null && state.Place == ours?.Place;
            lines.Add(new($"#{player.Slot} {player.Name}  v{player.Version}  ping {player.Ping} ms  state {(age < 0 ? "none" : age + " ms ago")}",
                player.Version != _context.Manifest.Version ? Warn : age > 2000 ? Bad : Text));
            lines.Add(new($"    {state?.Place ?? "(no place)"} cell {state?.CellX},{state?.CellY}{(samePlace ? "  - here" : "")}", samePlace ? Good : Dim));
        }
        return lines;
    }

    private List<Line> AreaLines()
    {
        var ownership = _hooks.Ownership;
        var lines = new List<Line>
        {
            new($"Place: {ownership.Place ?? "(none)"}", Head),
            new($"Role: {ownership.Role}   owner: #{ownership.Owner}   followers: {string.Join(", ", ownership.Others.Select(s => "#" + s))}", Text),
            new("", Text),
        };
        foreach (var (name, state) in _hooks.Features)
        {
            string text;
            try { text = state(); }
            catch (Exception e) { text = "failed: " + e.Message; }
            lines.Add(new($"{name}: {text}", Text));
        }
        return lines;
    }

    private List<Line> InspectLines()
    {
        var lines = new List<Line>();
        if (!Gm.InGame)
            return new() { new("Not in the game.", Dim) };
        Instance unit = _pinned.Exists ? _pinned : Mouse.Unit;
        Cell cell = unit.Exists ? Cell.At(unit.Get("x").AsReal, unit.Get("y").AsReal) : Mouse.Cell;
        lines.Add(new($"Cell {cell}{(_pinned.Exists ? "  (pinned)" : "")}", Head));
        if (unit.Exists)
        {
            string name = Gm.ObjectGetName(unit.Get("object_index").AsInt);
            lines.Add(new($"{name}{(unit.Get("mp_slot").IsUndefined ? "" : $"  - player #{unit.Get("mp_slot").AsInt}'s stand-in")}", Text));
            var areaUnits = _hooks.AreaUnits;
            string ids = _hooks.Ownership.Role switch
            {
                AreaRole.Owner => $"sync id {areaUnits.SyncIdOf(unit)?.ToString() ?? "none"}",
                AreaRole.Follower => areaUnits.OwnerIdOf(unit) is { } id ? $"bound to the owner's {id}" : "not bound to any of the owner's",
                _ => "not shared",
            };
            lines.Add(new(ids, _hooks.Ownership.Role == AreaRole.Follower && areaUnits.OwnerIdOf(unit) == null ? Warn : Text));
            lines.Add(new($"HP {Value(unit, "HP")}/{Value(unit, "max_hp")}  state {Value(unit, "state")}  AI {Value(unit, "ai_is_on")}  neutral {Value(unit, "is_neutral")}", Text));
            lines.Add(new($"faction {Value(unit, "faction_key")}  target {Describe(Instance.Of(unit.Get("target")))}  last attacker {Describe(Instance.Of(unit.Get("last_attacker")))}", Text));
            lines.Add(new($"x {unit.Get("x").AsReal:0} y {unit.Get("y").AsReal:0}  culled {unit.IsCulled}  attack count {Value(unit, "attack_count")}/{Value(unit, "attack_tolerance")}", Dim));
        }
        else
            lines.Add(new("No unit here.", Dim));
        lines.Add(new("", Text));
        lines.Add(new("Ground effects and marks", Head));
        foreach (Instance mark in Instances.All(GameObjectId.c_tile_mark, includeCulled: true))
        {
            if (!mark.Exists || Cell.At(mark.Get("x").AsReal, mark.Get("y").AsReal) != cell)
                continue;
            lines.Add(new($"{Gm.ObjectGetName(mark.Get("object_index").AsInt)}  duration {Value(mark, "duration")}  ending {Value(mark, "is_execute")}  {Value(mark, "roomEntityType")}", Text));
        }
        lines.Add(new("Items on the ground", Head));
        foreach (var item in GroundItems.All().Where(i => Cell.At(i.X, i.Y) == cell))
            lines.Add(new($"{item.ObjectName}{(item.IsStatic ? " (placed with the location)" : "")}", Text));
        return lines;
    }

    private static string Value(Instance instance, string name)
    {
        GmValue value = instance.Get(name);
        return value.Kind switch
        {
            GmKind.Real => value.AsReal.ToString("0.##"),
            GmKind.String => value.AsString,
            _ when value.IsUndefined => "-",
            _ => value.ToString() ?? "?",
        };
    }

    private static string Describe(Instance instance)
        => instance.Exists ? Gm.ObjectGetName(instance.Get("object_index").AsInt) : "none";

    private List<Line> NetLines()
    {
        var lines = new List<Line>
        {
            new($"Sent {NetStats.PacketsOut} packets, {Kb(NetStats.BytesOut)}; received {NetStats.PacketsIn}, {Kb(NetStats.BytesIn)}", Head),
            new($"Last second: {Kb(NetStats.RateOut)}/s out, {Kb(NetStats.RateIn)}/s in   delay sim: {_hooks.Session.SimulatedDelayMs} ms",
                _hooks.Session.SimulatedDelayMs > 0 ? Warn : Text),
            new("", Text),
            new("Most sent (bytes)", Head),
        };
        lines.AddRange(NetStats.Top(NetStats.Out, 6).Select(t => new Line($"  {t.Type.Replace("Packet", "")}: {t.Counter.Count} x, {Kb(t.Counter.Bytes)}", Text)));
        lines.Add(new("Most received (bytes)", Head));
        lines.AddRange(NetStats.Top(NetStats.In, 6).Select(t => new Line($"  {t.Type.Replace("Packet", "")}: {t.Counter.Count} x, {Kb(t.Counter.Bytes)}", Text)));
        lines.Add(new($"Latest{(NetStats.LogChatty ? "" : " (not the chatty ones)")}", Head));
        lines.AddRange(NetStats.Recent.Take(60).Select(l => new Line("  " + l, Dim)));
        return lines;
    }

    private static string Kb(long bytes) => bytes < 1024 ? $"{bytes} B" : bytes < 1024 * 1024 ? $"{bytes / 1024.0:0.#} KB" : $"{bytes / 1048576.0:0.##} MB";

    private List<Line> DesyncLines()
    {
        var lines = new List<Line> { new(_desync.Status, Head) };
        if (_desync.Findings.Count == 0)
            lines.Add(new("No differences listed.", Dim));
        lines.AddRange(_desync.Findings.Select(f => new Line(f.Text, Bad)));
        lines.Add(new("", Text));
        lines.Add(new("Marks: red where ours is, orange where theirs is (20 s, while this window is open).", Dim));
        return lines;
    }

    private List<Line> ToolsLines() => new()
    {
        new("World overlay (while this window is open)", Head),
        new($"  {Toggle("Sync ids over units", _overlay.ShowUnits)}   {Toggle("cell under the mouse", _overlay.ShowCell)}   {Toggle("desync marks", _overlay.ShowFindings)}", Text),
        new("", Text),
        new("Write dump: what's around us to %LOCALAPPDATA%\\StoneShard\\stoneshardmp-dump-<role>-<pid>.txt", Text),
        new("Resync area: every feature drops what it holds for this place and asks again.", Text),
        new("Teleport here: to the cell under the mouse. To <name>: next to that player, same place.", Text),
        new("Pass a turn: one idle world turn.", Text),
        new("", Text),
        new("Hotkeys: Ctrl+Shift+M this window, Ctrl+Shift+D the dump, Ctrl+Shift+P StoneForge's profiler.", Dim),
    };

    // ---- actions ----

    private void Copy()
    {
        string text = $"[{TabNames[_panel.Tab]}] {DateTime.Now:HH:mm:ss}\n" + string.Join("\n", Lines(_panel.Tab).Select(l => l.Text));
        Game.CallBuiltin("clipboard_set_text", text);
        DevLog.Add($"Copied the {TabNames[_panel.Tab]} tab");
    }

    private void Resync()
    {
        _hooks.Resync();
        DevLog.Add($"Resynced {_hooks.Ownership.Place} ({_hooks.Ownership.Role})");
    }

    private void SetDelay(int ms)
    {
        _hooks.Session.SimulatedDelayMs = Math.Clamp(ms, 0, DelayMax);
        DevLog.Add($"Simulated delay: {_hooks.Session.SimulatedDelayMs} ms on what we send");
    }

    private void TeleportToMouse() => Teleport(Mouse.Cell);

    private void TeleportTo(RemotePlayer player)
    {
        var theirs = player.State;
        if (theirs?.Place == null || theirs.Place != OurPlayer.Place)
        {
            DevLog.Add($"{player.Name} isn't here");
            return;
        }
        var at = new Cell(theirs.CellX, theirs.CellY);
        foreach (Cell cell in at.Neighbours)
            if (Free(cell))
            {
                Teleport(cell);
                return;
            }
        DevLog.Add($"No free cell next to {player.Name}");
    }

    private static bool Free(Cell cell) => cell.X >= 0 && cell.Y >= 0 && Units.At(cell).IsNone
        && Game.CallScript("scr_is_ground", default, cell.Center.X, cell.Center.Y).AsBool;

    private static void Teleport(Cell cell)
    {
        Instance player = StoneForge.Player.Instance;
        if (player.IsNone || !Gm.InGame)
            return;
        if (!Free(cell))
        {
            DevLog.Add($"Can't teleport to {cell}: not a free floor cell");
            return;
        }
        Game.CallScript("scr_invisible_teleport", player, cell.Center.X, cell.Center.Y);
        DevLog.Add($"Teleported to {cell}");
    }
}
