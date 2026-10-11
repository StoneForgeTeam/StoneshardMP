using System;
using System.Collections.Generic;
using System.Linq;
using StoneForge;
using StoneshardMP.Features.Areas;
using StoneshardMP.Features.Players;
using StoneshardMP.Net;

namespace StoneshardMP.Features.Dev;

// Dev tools: are the games in a place seeing the same units? Run asks the others there (the owner asks its followers; a
// follower asks the owner) for theirs, by the owner's sync id, and compares each with ours: one missing on either side,
// on another cell, with other health, or bound to another kind of unit. The differences are listed in the Desync tab and
// marked on their cells (DevOverlay) for a while. Anyone in the place answers, contributor or not: it's only a list.
public sealed class DesyncCheck
{
    public sealed record Finding(string Text, Cell? Ours, Cell? Theirs);

    private const double HealthTolerance = 0.5;
    private readonly Session _session;
    private readonly AreaOwnership _ownership;
    private readonly AreaUnits _areaUnits;
    private int _nonce;
    private readonly List<Finding> _findings = new();

    public DesyncCheck(Session session, AreaOwnership ownership, AreaUnits areaUnits)
    {
        _session = session;
        _ownership = ownership;
        _areaUnits = areaUnits;
        session.On<UnitsViewRequestPacket>(ReceiveRequest);
        session.On<UnitsViewPacket>(ReceiveView);
    }

    public IReadOnlyList<Finding> Findings => _findings;
    public string Status { get; private set; } = "Not run yet.";
    public long FoundAt { get; private set; }

    public void Run()
    {
        _findings.Clear();
        FoundAt = Environment.TickCount64;
        if (!_session.Connected || _ownership.Place is not { } place || _ownership.Role == AreaRole.Alone)
        {
            Status = "Nothing to compare: no one else is here.";
            return;
        }
        _nonce++;
        var targets = _ownership.Role == AreaRole.Owner ? _ownership.Others.ToList() : new List<int> { _ownership.Owner };
        foreach (int slot in targets)
            _session.Send(new UnitsViewRequestPacket(place, _nonce), slot);
        Status = $"Asked {string.Join(", ", targets.Select(s => "#" + s))} at {DateTime.Now:HH:mm:ss}...";
    }

    // Ours, by sync id: the owner's units, or our copies of them.
    private Dictionary<long, (Instance Unit, UnitView View)> Ours()
    {
        var list = _ownership.Role == AreaRole.Owner ? _areaUnits.Synced : _areaUnits.Bound;
        var result = new Dictionary<long, (Instance, UnitView)>();
        foreach (var (id, unit) in list)
        {
            if (!unit.Exists)
                continue;
            result[id] = (unit, new UnitView(id, Gm.ObjectGetName(unit.Get("object_index").AsInt), (short)Math.Floor(unit.Get("x").AsReal / 26),
                (short)Math.Floor(unit.Get("y").AsReal / 26), (float)(unit.Get("HP") is { Kind: GmKind.Real } hp ? hp.AsReal : 0)));
        }
        return result;
    }

    private void ReceiveRequest(RemotePlayer from, UnitsViewRequestPacket request)
    {
        if (request.Place != _ownership.Place)
            return;
        _session.Send(new UnitsViewPacket(request.Place, request.Nonce, Ours().Values.Select(v => v.View).ToArray()), from.Slot);
    }

    private void ReceiveView(RemotePlayer from, UnitsViewPacket view)
    {
        if (view.Nonce != _nonce || view.Place != _ownership.Place)
            return;
        var ours = Ours();
        var theirs = view.Units.GroupBy(u => u.SyncId).ToDictionary(g => g.Key, g => g.First());
        string who = $"#{from.Slot} {from.Name}";
        int before = _findings.Count;
        foreach (var (id, (_, mine)) in ours)
        {
            Cell here = new(mine.CellX, mine.CellY);
            if (!theirs.TryGetValue(id, out var other))
            {
                _findings.Add(new($"{Short(mine.Object)} {id} at {here}: missing for {who}", here, null));
                continue;
            }
            Cell there = new(other.CellX, other.CellY);
            if (other.Object != mine.Object)
                _findings.Add(new($"{id}: ours is {Short(mine.Object)}, {who}'s is {Short(other.Object)}", here, there));
            if (here != there)
                _findings.Add(new($"{Short(mine.Object)} {id}: ours at {here}, {who}'s at {there}", here, there));
            if (Math.Abs(other.Health - mine.Health) > HealthTolerance)
                _findings.Add(new($"{Short(mine.Object)} {id}: health ours {mine.Health:0.#}, {who}'s {other.Health:0.#}", here, null));
        }
        foreach (var (id, other) in theirs)
            if (!ours.ContainsKey(id))
                _findings.Add(new($"{Short(other.Object)} {id} at {new Cell(other.CellX, other.CellY)}: only {who} has it", null,
                    new Cell(other.CellX, other.CellY)));
        int found = _findings.Count - before;
        FoundAt = Environment.TickCount64;
        Status = $"{who}: {view.Units.Length} units, ours {ours.Count} - "
            + (found == 0 ? "all agree" : $"{found} difference{(found == 1 ? "" : "s")}") + $" ({DateTime.Now:HH:mm:ss})";
        foreach (var finding in _findings.Skip(before))
            DevLog.Add("desync: " + finding.Text);
    }

    private static string Short(string obj) => obj.StartsWith("o_") ? obj[2..] : obj;
}
