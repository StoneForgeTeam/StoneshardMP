using System;
using StoneForge;
using StoneshardMP.Features.Areas;
using StoneshardMP.Net;

// An item put in an enemy's own inventory (scr_enemy_inventory_add) - an arrow or bolt stuck in it, dropped as it dies.
[assembly: HookScript(nameof(Scripts.scr_enemy_inventory_add))]

namespace StoneshardMP.Features.Combat;

// A follower's arrows and bolts stuck in the owner's units drop when those die: an arrow that sticks (o_arrow's hit, its
// user event 0) goes into the unit's own inventory (scr_enemy_inventory_add), which the unit drops with its corpse - in
// the follower's game, on its copy, which never dies there: the owner's unit does, without them (#63). Now it's put in
// the owner's unit too. (Only our own arrow's: the game also fills units' inventories itself, in every game alike.)
public sealed class StuckArrows
{
    private readonly ModContext _context;
    private readonly Session _session;
    private readonly AreaUnits _areaUnits;
    private readonly AreaOwnership _ownership;
    private readonly Func<bool> _inSharedWorld;
    // (Inside our own arrow's hit.)
    private int _ourArrow;

    public StuckArrows(ModContext context, Session session, AreaUnits areaUnits, AreaOwnership ownership, Func<bool> inSharedWorld)
    {
        _context = context;
        _session = session;
        _areaUnits = areaUnits;
        _ownership = ownership;
        _inSharedWorld = inSharedWorld;
        session.On<StuckArrowPacket>(Receive);
        context.OnCode("gml_Object_o_arrow_Other_10",
            before: (arrow, _) =>
            {
                if (_ourArrow > 0 || !arrow.IsNone && CombatSync.IsOursDeep(arrow))
                    _ourArrow++;
                return false;
            },
            after: (_, _) => _ourArrow = Math.Max(0, _ourArrow - 1));
        Scripts.scr_enemy_inventory_add.Before(context, call =>
        {
            if (_ourArrow > 0 && _ownership.Role == AreaRole.Follower && _inSharedWorld() && call.Args.Length >= 2
                && _areaUnits.OwnerIdOf(Instance.Of(call.Args[0])) is { } unitId
                && call.Args[1].Kind == GmKind.Real && call.Args[1].AsInt >= 0)
            {
                int count = call.Args.Length > 2 && call.Args[2].Kind == GmKind.Real ? call.Args[2].AsInt : 1;
                _session.Send(new StuckArrowPacket(unitId, Gm.ObjectGetName(call.Args[1].AsInt), count), _ownership.Owner);
            }
            return false;
        });
    }

    private void Receive(RemotePlayer from, StuckArrowPacket packet)
    {
        if (_ownership.Role != AreaRole.Owner || !Gm.InGame || packet.Count <= 0)
            return;
        Instance unit = _areaUnits.UnitOf(packet.UnitId);
        int item = Game.CallBuiltin("asset_get_index", packet.Item).AsInt;
        if (unit.IsNone || !unit.Exists || item < 0 || !Game.CallBuiltin("object_exists", item).AsBool)
            return;
        Game.CallScript("scr_enemy_inventory_add", default, unit, item, packet.Count);
        _context.Log($"{from.Name}'s {packet.Item} stuck in unit {packet.UnitId}: dropped with it");
    }
}
