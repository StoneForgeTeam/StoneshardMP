/// @stoneforge return GmValue
/// @stoneforge param inst GmValue
// Area owner: one loot item for followers - {u: sync id, k: matching key (MpLootKey), j: its JSON (MpLootJson),
// f: its throw if it's in the air (MpLootFlight, else -1), t: the drop token a follower gave it ("" if none) - so
// the follower whose drop it is keeps its own instead of getting a second}.
function MpLootEntry(inst)
{
    var _token = ds_map_find_value(global.mp_loot_token_of, MpLootIkey(inst));
    return {
        u: MpLootUid(inst), k: MpLootKey(inst), j: MpLootJson(inst), f: MpLootFlight(inst),
        t: is_undefined(_token) ? "" : _token
    };
}
