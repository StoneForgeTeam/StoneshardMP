using System;
using System.Linq;
using StoneForge;
using StoneshardMP.Features.Areas;

namespace StoneshardMP.Features.Dev;

// Dev tools: drawn over the world (in the GUI pass, at each thing's place on screen):
// - Units: each of the place's units with its sync id - green: the owner's real one (us owning), blue: a copy bound to
//   the owner's (us following), grey: one of ours the owner's roster doesn't account for.
// - The cell under the mouse, with its coordinates.
// - The last desync check's findings, for a while: red where ours is, orange where theirs is, joined by a line.
public sealed class DevOverlay
{
    private const long FindingsMs = 20000;
    private static readonly int Real = Draw.Rgb(110, 220, 110), Copy = Draw.Rgb(110, 170, 240), Loose = Draw.Rgb(150, 150, 150),
        Ours = Draw.Rgb(230, 70, 60), Theirs = Draw.Rgb(240, 160, 50), CellColour = Draw.Rgb(240, 230, 140);

    private readonly AreaOwnership _ownership;
    private readonly AreaUnits _areaUnits;
    private readonly DesyncCheck _desync;

    public DevOverlay(AreaOwnership ownership, AreaUnits areaUnits, DesyncCheck desync)
    {
        _ownership = ownership;
        _areaUnits = areaUnits;
        _desync = desync;
    }

    public bool ShowUnits { get; set; } = true;
    public bool ShowCell { get; set; } = true;
    public bool ShowFindings { get; set; } = true;

    // Where a room position is on the GUI (the current camera's view over the window).
    private static bool ToGui(double x, double y, out double gx, out double gy)
    {
        gx = gy = 0;
        GmValue camera = Game.Global["cameraCurrent"];
        if (camera.IsUndefined)
            return false;
        double vx = Game.CallBuiltin("camera_get_view_x", camera).AsReal, vy = Game.CallBuiltin("camera_get_view_y", camera).AsReal;
        double vw = Game.CallBuiltin("camera_get_view_width", camera).AsReal, vh = Game.CallBuiltin("camera_get_view_height", camera).AsReal;
        if (vw <= 0 || vh <= 0)
            return false;
        gx = (x - vx) * Draw.Width / vw;
        gy = (y - vy) * Draw.Height / vh;
        return gx > -40 && gy > -40 && gx < Draw.Width + 40 && gy < Draw.Height + 40;
    }

    private static void CellBox(Cell cell, int colour, string? label = null)
    {
        if (!ToGui(cell.Corner.X, cell.Corner.Y, out double x1, out double y1) || !ToGui(cell.Corner.X + Cell.Size, cell.Corner.Y + Cell.Size, out double x2, out double y2))
            return;
        Draw.Rectangle(x1, y1, x2, y2, colour, 0.9, outline: true);
        if (label != null)
            Draw.Text(x1 + 1, y1 - 9, label, colour);
    }

    public void Render()
    {
        if (!Gm.InGame)
            return;
        if (ShowUnits && _ownership.Role != AreaRole.Alone)
        {
            var known = _ownership.Role == AreaRole.Owner ? _areaUnits.Synced : _areaUnits.Bound;
            var marked = new System.Collections.Generic.HashSet<Instance>();
            foreach (var (id, unit) in known)
            {
                if (!unit.Exists || !ToGui(unit.Get("x").AsReal, unit.Get("y").AsReal, out double gx, out double gy))
                    continue;
                marked.Add(unit.Persist());
                StoneForge.Draw.Text(gx, gy - 30, id.ToString(), _ownership.Role == AreaRole.Owner ? Real : Copy, StoneForge.Draw.AlignCenter);
            }
            if (_ownership.Role == AreaRole.Follower)
                foreach (Instance unit in Instances.All(GameObjectId.o_enemy).Where(u => !marked.Contains(u.Persist())))
                    if (ToGui(unit.Get("x").AsReal, unit.Get("y").AsReal, out double gx, out double gy))
                        StoneForge.Draw.Text(gx, gy - 30, "?", Loose, StoneForge.Draw.AlignCenter);
        }
        if (ShowCell && !Mouse.OverUI)
            CellBox(Mouse.Cell, CellColour, Mouse.Cell.ToString());
        if (ShowFindings && Environment.TickCount64 - _desync.FoundAt < FindingsMs)
            foreach (var finding in _desync.Findings)
            {
                if (finding.Ours is { } ours)
                    CellBox(ours, Ours);
                if (finding.Theirs is { } theirs)
                    CellBox(theirs, Theirs);
                if (finding.Ours is { } a && finding.Theirs is { } b && a != b
                    && ToGui(a.Center.X, a.Center.Y, out double ax, out double ay) && ToGui(b.Center.X, b.Center.Y, out double bx, out double by))
                    StoneForge.Draw.Line(ax, ay, bx, by, Theirs);
            }
    }
}
