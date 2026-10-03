using System;
using System.IO;
using System.Linq;
using StoneForge;
using StoneshardMP.Features.Clock;
using StoneshardMP.Features.Players;
using StoneshardMP.Net;

namespace StoneshardMP.Features.Debug;

// Diagnostics: Ctrl+Shift+D writes what's around the player (MpDebugNearby) with this game's role, place and clock to
// %LOCALAPPDATA%\StoneShard\stoneshardmp-dump-<role>-<process>.txt - one file per game, so two games on one PC can be
// compared side by side.
public sealed class DebugDump
{
    private readonly ModContext _context;
    private readonly Session _session;

    public DebugDump(ModContext context, Session session)
    {
        _context = context;
        _session = session;
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
            var lines = Gml.MpDebugNearby(12).Split('\n', StringSplitOptions.RemoveEmptyEntries).OrderBy(l => l, StringComparer.Ordinal);
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StoneShard");
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, $"stoneshardmp-dump-{role}-{Environment.ProcessId}.txt");
            File.WriteAllText(file, $"{DateTime.Now:HH:mm:ss} {role} place={state?.Place} cell={state?.CellX},{state?.CellY} clock={GameClock.Snapshot()}\n"
                + CullingCheck() + "\n" + string.Join("\n", lines) + "\n");
            _context.Log("Dump written: " + file);
        }
        catch (Exception e)
        {
            _context.Log("Dump failed: " + e.Message);
        }
    }

    // StoneForge's off-screen instances against the GML they're to replace: the room's ground loot by
    // Instances.All(o_loot, includeCulled) - culled ones read through their pointer - and by MpLootAll (which also
    // leaves out loot in flight and persistent loot). Every MpLootAll id should be in StoneForge's list.
    private static string CullingCheck()
    {
        try
        {
            var all = Instances.All(GameObjectId.o_loot, includeCulled: true);
            var ids = all.Select(i => i.Get("id").AsInstance).ToHashSet();
            int culled = all.Count(i => i.IsCulled);
            int readable = all.Count(i => i.IsCulled && i.Get("object_index").AsInt > 0);
            var gml = Gml.MpLootAll().AsArray;
            int gmlCount = gml?.Length ?? -1;
            int missing = 0;
            for (int i = 0; i < gmlCount; i++)
                if (!ids.Contains(gml![i].AsInstance))
                    missing++;
            return $"culling check: StoneForge {all.Count} loot ({culled} culled, {readable} of them read) | GML {gmlCount} | GML ids missing from StoneForge's: {missing}";
        }
        catch (Exception e)
        {
            return "culling check failed: " + e;
        }
    }
}
