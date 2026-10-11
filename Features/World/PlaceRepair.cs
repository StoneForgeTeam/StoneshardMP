using System;
using StoneForge;

// A place's saved state decoded as its room loads (the patcher makes it hookable).
[assembly: HookScript(nameof(Scripts.scr_locationRoomPresetGetEntitiesMap))]

namespace StoneshardMP.Features.World;

// A place's saved state that the game can't read, put right as its room loads - so it never crashes the game.
// The game keeps what's happened in a place (what's dead, taken, dropped) as JSON text in its preset
// (entitiesDataMapString), decoded as the room loads (scr_locationRoomPresetGetEntitiesMap) - o_roomEntityLoader then
// reads the map it made. 0.4.1-0.4.4 wrote some places' JSON with .NET's escaping (ItemLists), which the game's decoder
// can't always read: no map, and "Data structure with index does not exist" in o_roomEntityLoader - entering the place,
// in every game that had it, and the host's save kept it.
// - Before it's decoded: written again as the game reads it (ItemLists.Readable), and kept so.
// - After: if there's still no map, an empty one - the place loads as it was built, its changes lost, rather than the
//   game crashing. Logged either way.
public sealed class PlaceRepair
{
    private readonly ModContext _context;

    public PlaceRepair(ModContext context)
    {
        _context = context;
        Scripts.scr_locationRoomPresetGetEntitiesMap.Before(context, call =>
        {
            Repair(call);
            return false;
        });
        Scripts.scr_locationRoomPresetGetEntitiesMap.After(context, call =>
        {
            if (DsMap.From(call.Result) is { Exists: true })
                return;
            call.Result = DsMap.Create().Id;
            _context.Log($"Place {Describe(call)}: its saved state couldn't be read - loaded as it was built instead");
        });
    }

    private void Repair(ScriptCall call)
    {
        try
        {
            if (call.Args.Length < 3 || call.Args[0].Kind != GmKind.String
                || Locations.Get(call.Args[0].AsString)?.Room(call.Args[1])?.Preset(call.Args[2])?.Export() is not { EntitiesJson: { } json } state)
                return;
            var (readable, changed) = ItemLists.Readable(json);
            if (changed && Locations.Store(state with { EntitiesJson = readable }))
                _context.Log($"Place {Describe(call)}: its saved state put right so the game can read it");
        }
        catch (Exception e)
        {
            _context.Log($"Place {Describe(call)}: couldn't check its saved state: {e.Message}");
        }
    }

    private static string Describe(ScriptCall call)
        => call.Args.Length >= 3 ? $"{call.Args[0].AsString} / {call.Args[1].AsString} / {call.Args[2].AsString}" : "?";
}
