/// @stoneforge return bool
/// @stoneforge param key string
// Whether a dungeon value (the world-map tile's "dungeon" map) is shared between games in the same world - its layout
// (floor seeds, saved floor graphs, special floors), its state (boss alive, open, reset timer, the trapgate's cage,
// mob levels) and what it is. Not its contract (whether it has one, the contract's index in this game's own list,
// its NPC and boss, complete): contracts aren't shared yet, and an index means nothing in another game.
function MpDungeonKeyShared(key)
{
    switch (key)
    {
        case "has_contract":
        case "contract_map":
        case "contract_script":
        case "contract_modification":
        case "NPC_Type":
        case "NPC_Name":
        case "Boss_Type":
        case "isComplete":
            return false;
    }
    return true;
}
