using System;
using System.IO;
using System.Linq;
using StoneForge;
using StoneshardMP.Net;

namespace StoneshardMP;

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
            var state = PlayerState.Parse(Gml.MpPlayerState());
            var lines = Gml.MpDebugNearby(12).Split('\n', StringSplitOptions.RemoveEmptyEntries).OrderBy(l => l, StringComparer.Ordinal);
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StoneShard");
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, $"stoneshardmp-dump-{role}-{Environment.ProcessId}.txt");
            File.WriteAllText(file, $"{DateTime.Now:HH:mm:ss} {role} place={state?.Place} cell={state?.CellX},{state?.CellY} clock={Gml.MpWorldClock()}\n"
                + string.Join("\n", lines) + "\n");
            _context.Log("Dump written: " + file);
        }
        catch (Exception e)
        {
            _context.Log("Dump failed: " + e.Message);
        }
    }
}
