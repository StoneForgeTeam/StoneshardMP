using System;
using System.IO;
using System.Linq;
using StoneForge;
using StoneshardMP.Features.Areas;
using StoneshardMP.Features.Clock;
using StoneshardMP.Features.Players;
using StoneshardMP.Net;

namespace StoneshardMP.Features.Debug;

// Diagnostics: Ctrl+Shift+D writes what's around the player (Nearby) with this game's role, place and clock to
// %LOCALAPPDATA%\StoneShard\stoneshardmp-dump-<role>-<process>.txt - one file per game, so two games on one PC can be
// compared side by side.
public sealed class DebugDump
{
    // (Every instance within this many cells of the player.)
    private const int Radius = 12;

    private readonly ModContext _context;
    private readonly Session _session;
    private readonly AreaUnits _areaUnits;

    public DebugDump(ModContext context, Session session, AreaUnits areaUnits)
    {
        _context = context;
        _session = session;
        _areaUnits = areaUnits;
    }

    public void Tick()
    {
        if (!(Keyboard.Down(Keyboard.Control) && Keyboard.Down(Keyboard.Shift) && Keyboard.Pressed('D')) || !Gm.InGame)
            return;
        try
        {
            string role = _session.Mode switch
            {
                Session.SessionMode.Host => "host",
                Session.SessionMode.Client => $"client{_session.Slot}",
                _ => "solo",
            };
            var state = OurPlayer.State();
            var items = GroundItems.All();
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StoneShard");
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, $"stoneshardmp-dump-{role}-{Environment.ProcessId}.txt");
            File.WriteAllText(file, $"{DateTime.Now:HH:mm:ss} {role} place={state?.Place} cell={state?.CellX},{state?.CellY} clock={GameClock.Snapshot()}"
                + $" busy={Game.IsBusy}\n{Input()}\nground items: {items.Count} ({items.Count(i => i.Instance.IsCulled)} off screen)\n"
                + Timings()
                + string.Join("\n", Nearby().OrderBy(line => line, StringComparer.Ordinal)) + "\n");
            _context.Log("Dump written: " + file);
            DecodeCheck(folder);
        }
        catch (Exception e)
        {
            _context.Log("Dump failed: " + e.Message);
        }
    }

    // Every instance within Radius cells of the player - object, cell, sprite and frame, visible, depth, and for units
    // their state and animation flags, and whether they're the host's (AreaUnits) - one line each.
    private string[] Nearby()
    {
        if (OurPlayer.Instance is not { IsNone: false } player)
            return Array.Empty<string>();
        double px = player.Get("x").AsReal, py = player.Get("y").AsReal;
        int unit = Gm.AssetGetIndex("o_unit");
        // (-3: GameMaker's all.)
        return Instances.All(-3).Select(instance =>
        {
            double x = instance.Get("x").AsReal, y = instance.Get("y").AsReal;
            if (Math.Sqrt(Math.Pow(x - px, 2) + Math.Pow(y - py, 2)) > Radius * Cell.Size)
                return null;
            int obj = instance.Get("object_index").AsInt;
            string line = $"{Gm.ObjectGetName(obj)} @{Cell.At(x, y)} spr={SpriteName(instance.Get("sprite_index"))}"
                + $"#{Math.Floor(instance.Get("image_index").AsReal)} vis={instance.Get("visible").AsBool} depth={instance.Get("depth")}";
            if (Gm.ObjectIsAncestor(obj, unit))
                line += $" state={Text(instance.Get("state"))} is_life={Text(instance.Get("is_life"))} spr_render={SpriteName(instance.Get("spr"))}"
                    + $" ai={Text(instance.Get("ai_is_on"))} hosts={_areaUnits.IsHosts(instance)}";
            return line;
        }).OfType<string>().ToArray();
    }

    // The profiler's last second, every part of every mod (the overlay shows ten a mod), while it's on (Ctrl+Shift+P):
    // average and worst ms per frame, and runs per frame. Per run: the average over the runs.
    private static string Timings()
    {
        if (!Profiler.Visible || Profiler.Timings.Count == 0)
            return "";
        var lines = Profiler.Timings.Select(t => $"  {t.Mod} {(t.Section ? "- " : "")}{t.Name}: {t.Average:0.000} ms/frame, worst {t.Worst:0.000} ms,"
            + $" {t.CallsPerFrame:0.###} runs/frame, {(t.CallsPerFrame > 0 ? t.Average / t.CallsPerFrame : 0):0.000} ms/run");
        return $"profiler ({Profiler.Fps:0} fps, worst frame {Profiler.WorstFrameMs:0} ms):\n{string.Join("\n", lines)}\n";
    }

    // A host's world that didn't read here (JoinManager kept it): the game's json_decode tried on it whole, without
    // System.Text.Json's escaping, and section by section - which part the game can't read, to the log.
    private void DecodeCheck(string folder)
    {
        string file = Path.Combine(folder, "stoneshardmp-unreadable-world.json");
        if (!File.Exists(file))
            return;
        string json = File.ReadAllText(file);
        string Try(string text)
        {
            GmValue made = Game.CallBuiltin("json_decode", text);
            if (made.AsDsMap is { } map)
            {
                int count = map.Count;
                map.Destroy();
                return $"a map of {count}";
            }
            return $"not a map ({made.Kind} {made})";
        }
        _context.Log($"Decode check of the kept world ({json.Length} characters): whole - {Try(json)}");
        if (System.Text.Json.Nodes.JsonNode.Parse(json) is not System.Text.Json.Nodes.JsonObject world)
            return;
        var relaxed = new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        _context.Log($"Decode check: unescaped - {Try(world.ToJsonString(relaxed))}");
        foreach (var (name, section) in world)
            _context.Log($"Decode check: {name} - {Try(new System.Text.Json.Nodes.JsonObject { [name] = section?.DeepClone() }.ToJsonString())}");
    }

    // What decides whether the player can act and the game shows its cursor and path: scr_is_cutscene's three
    // conditions (the cutscene controller, the UI hidden, the screen faded), a room change, a dialogue, the player's own
    // locks, and the world clock's input phase.
    private static string Input()
    {
        string Of(GameObjectId obj, string variable)
        {
            var all = Instances.All(obj);
            return all.Count == 0 ? "none" : string.Join(",", all.Select(i => $"{variable}={Text(i.Get(variable))}"));
        }
        Instance player = OurPlayer.Instance;
        return $"input: cutscene={Game.IsCutscene} cutscene_controller[{Of(GameObjectId.o_cutscene_controller, "cutscene_on")}]"
            + $" gui_no_click[{Of(GameObjectId.o_gui_no_click, "show_ui")}] black_overlay={Instances.All(GameObjectId.o_black_overlay).Count}"
            + $" room_changer={Rooms.IsChanging} dialogue={Gm.InstanceExists(GameObjectId.o_dialogue)}"
            + $" lock_movement={Text(player.Get("lock_movement"))} is_moving={Text(player.Get("is_moving"))}"
            + $" alarm1={player.Alarm[1]} alarm4={player.Alarm[4]} input_phase={Text(Game.Global["mp_world_input_phase"])}"
            // (StoneForge's hold on the game's input: its typing flag - hotkeys and key-bound clicks off - and the
            // invisible blocker it puts over mod UI under the mouse.)
            + $" stonemod_typing={Text(Game.Global["stonemod_typing"])} blocker[{Blocker()}]"
            + $" mouse_gui={Text(Game.Global["guiMouseX"])},{Text(Game.Global["guiMouseY"])}";
    }

    private static string Blocker()
    {
        int obj = Gm.AssetGetIndex("o_stonemod_blocker");
        if (obj < 0)
            return "no object";
        var all = Instances.All(obj);
        return all.Count == 0 ? "none" : string.Join(",", all.Select(b =>
            $"{Text(b.Get("x"))},{Text(b.Get("y"))} x{Text(b.Get("image_xscale"))} y{Text(b.Get("image_yscale"))}"));
    }

    private static string Text(GmValue value) => value.IsUndefined ? "-" : value.AsString;

    private static string SpriteName(GmValue sprite)
        => sprite.Kind == GmKind.Real && Draw.SpriteName(sprite.AsInt) is { Length: > 0 } name ? name : "-";
}
