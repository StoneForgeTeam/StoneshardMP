using System;
using StoneForge;
using StoneshardMP.Features.World;

namespace StoneshardMP.Features.Saves;

// What we know can go wrong, put right before it's saved - so a save never keeps it.
// As the game's about to write a save (StoneForge's SaveData.OnSaving), the items held by containers, fires' pots and corpses are checked: an
// entry that isn't one of this game's items (another game's number for one - ItemLists) is taken out, from those in the
// room and from the saved state of every place there's one of. In the game it crashed the cooking window, or loaded as
// the wrong things; saved, it would have stayed so. What was taken out is logged.
public sealed class SaveCheck
{
    private static readonly string[] ContainerLists = { "loot_list" };
    private static readonly string[] CorpseLists = { "loot_list_meat", "loot_list_add" };

    private readonly ModContext _context;

    public SaveCheck(ModContext context)
    {
        _context = context;
        SaveData.OnSaving(context, _ => Run());
    }

    private void Run()
    {
        try
        {
            int live = 0;
            // (Only what keeps items in its loot_list - containers, fires' pots: a loot_list of something else may be
            // something else.)
            foreach (GameObjectId kind in new[] { GameObjectId.c_container, GameObjectId.o_campfire_parent })
                foreach (Instance stuff in Instances.All(kind, includeCulled: true))
                    live += Clean(stuff, ContainerLists);
            foreach (Instance corpse in Instances.All(GameObjectId.o_abstract_corpse, includeCulled: true))
                live += Clean(corpse, CorpseLists);
            int stored = 0, places = 0;
            if (Locations.Available)
                foreach (string tag in Locations.Tags)
                {
                    if (Locations.Get(tag) is not { } location)
                        continue;
                    foreach (GmValue roomKey in location.Rooms)
                    {
                        if (location.Room(roomKey) is not { } room)
                            continue;
                        foreach (GmValue presetKey in room.Presets)
                        {
                            if (room.Preset(presetKey)?.Export() is not { EntitiesJson: { } json } state)
                                continue;
                            // (Written again as the game reads it, if it needs to be - PlaceRepair - then cleaned.)
                            var (readable, unreadable) = ItemLists.Readable(json);
                            var (cleaned, removed) = ItemLists.Clean(readable);
                            if (removed == 0 && !unreadable)
                                continue;
                            if (Locations.Store(state with { EntitiesJson = cleaned }))
                            {
                                stored += removed;
                                places++;
                            }
                        }
                    }
                }
            if (live + stored > 0)
                _context.Log($"Save check: took out {live} broken item(s) here and {stored} in {places} other place(s) before saving");
        }
        catch (Exception e)
        {
            // (The check never stops the save.)
            _context.Log($"Save check couldn't finish: {e.Message}");
        }
    }

    // (An instance's own variables can't be read off screen: it's woken for the moment, then put back.)
    private static int Clean(Instance thing, string[] lists)
    {
        bool culled = thing.IsCulled;
        if (culled)
            Game.CallBuiltin("instance_activate_object", thing);
        try
        {
            int removed = 0;
            foreach (string name in lists)
                if (thing.Get(name) is { Kind: GmKind.Real } list)
                    removed += ItemLists.CleanList(list);
            return removed;
        }
        finally
        {
            if (culled)
                Game.CallBuiltin("instance_deactivate_object", thing);
        }
    }
}
