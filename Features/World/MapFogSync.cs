using System;
using System.Collections.Generic;
using System.Linq;
using StoneForge;
using StoneshardMP.Net;

// A world-map tile uncovered (scr_globalmapFogUpdate: the tile entered, and the fog around it).
[assembly: HookScript(nameof(Scripts.scr_globalmapFogUpdate))]

namespace StoneshardMP.Features.World;

// The world map's explored land is everyone's: a tile one player uncovers (scr_globalmapFogUpdate - entering it) is
// uncovered in the others' games too, by the game's own call there (the fog around it, mountains, the caravan's paths,
// points of interest - no discovery XP: that's scr_glmap_openLocation's, shared on its own as the tile's isOpen). And
// whenever someone comes into the world, each game sends all it has uncovered, so what each player explored before
// they played together is put together too (#46).
public sealed class MapFogSync
{
    // (Who's in the world looked at this often - and what we've just uncovered sent at most this often.)
    private const int Interval = 30;
    private readonly ModContext _context;
    private readonly Session _session;
    private readonly Func<bool> _inSharedWorld;
    private readonly List<ushort> _uncovered = new();
    private string _present = "";
    private int _frame;
    // (Uncovering what another sent: not sent back.)
    private bool _applying;

    public MapFogSync(ModContext context, Session session, Func<bool> inSharedWorld)
    {
        _context = context;
        _session = session;
        _inSharedWorld = inSharedWorld;
        session.On<MapFogPacket>(Receive);
        Scripts.scr_globalmapFogUpdate.Before(context, call =>
        {
            if (!_applying && Ready && call.Args.Length >= 2 && call.Args[0].Kind == GmKind.Real
                && call.Args[1].Kind == GmKind.Real && Fog(call.Args[0].AsInt, call.Args[1].AsInt) == 0)
            {
                _uncovered.Add((ushort)call.Args[0].AsInt);
                _uncovered.Add((ushort)call.Args[1].AsInt);
            }
            return false;
        });
    }

    private bool Ready => _session.Connected && _inSharedWorld() && Gm.InGame && Gm.InstanceExists(GameObjectId.o_player);

    public void Clear()
    {
        _uncovered.Clear();
        _present = "";
    }

    public void Tick()
    {
        if (++_frame % Interval != 0)
            return;
        if (!Ready)
        {
            _uncovered.Clear();
            return;
        }
        // (Someone's come into the world: all of ours, for them.)
        string present = string.Join(",", _session.Players.Where(p => p.State != null).Select(p => p.Slot).OrderBy(s => s));
        if (present != _present)
        {
            _present = present;
            _uncovered.Clear();
            if (present.Length > 0 && All() is { Length: > 0 } all)
                _session.Send(new MapFogPacket(all));
            return;
        }
        if (_uncovered.Count > 0)
        {
            if (present.Length > 0)
                _session.Send(new MapFogPacket(_uncovered.ToArray()));
            _uncovered.Clear();
        }
    }

    private void Receive(RemotePlayer from, MapFogPacket packet)
    {
        if (!Ready)
            return;
        int uncovered = 0;
        _applying = true;
        try
        {
            for (int i = 0; i + 1 < packet.Cells.Length; i += 2)
            {
                int x = packet.Cells[i], y = packet.Cells[i + 1];
                if (Fog(x, y) != 0)
                    continue;
                Game.CallScript("scr_globalmapFogUpdate", default, x, y);
                uncovered++;
            }
        }
        finally { _applying = false; }
        if (uncovered > 0)
            _context.Log($"{from.Name}'s world map: {uncovered} tile(s) uncovered here");
    }

    // A tile's fog: 0 covered, 1 uncovered (-4: no world map).
    private static double Fog(int x, int y) => Game.CallScript("scr_globalmapFogGet", default, x, y).AsReal;

    // Every tile we've uncovered: the game's own saved form of the grid (scr_globalmapFogSave: a digit a tile, column by
    // column), read in one call.
    private static ushort[] All()
    {
        int width = Game.Global["worldWidth"].AsInt, height = Game.Global["worldHeight"].AsInt;
        if (width <= 0 || height <= 0)
            return Array.Empty<ushort>();
        Game.CallScript("scr_globalmapFogSave", default);
        string fog = Game.Global["globalmapFogString"].AsString;
        var cells = new List<ushort>();
        for (int i = 0; i < fog.Length && i < width * height; i++)
            if (fog[i] == '1')
            {
                cells.Add((ushort)(i / height));
                cells.Add((ushort)(i % height));
            }
        return cells.ToArray();
    }
}
