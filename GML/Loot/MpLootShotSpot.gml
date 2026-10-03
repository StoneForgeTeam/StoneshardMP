/// @stoneforge return string
/// @stoneforge param arrow GmValue
// An arrow or bolt landing (o_arrow's user event 1, which drops its ammo by its target with scr_loot_drop): "x,y",
// where the ammo lands, if our player shot it - loot turning up there now is our drop (MpLootFollowerStep) - else "".
function MpLootShotSpot(arrow)
{
    if (!instance_exists(arrow) || !is_player(arrow.owner))
        return "";
    return string(arrow.target_x) + "," + string(arrow.target_y);
}
