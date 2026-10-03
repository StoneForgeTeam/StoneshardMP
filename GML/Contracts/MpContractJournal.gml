/// @stoneforge return void
/// @stoneforge param index int
/// @stoneforge param contract GmValue
/// @stoneforge param wasTaken bool
// After another game changed a handed-out contract (its index and map; whether it was taken before): list it in the
// journal while it's taken, and keep the diary (the task shown as current) in step, as the game does on the side that
// made the change - scr_contract_take lists it and switches the diary to it, scr_contract_target_number_change
// refreshes the diary if it's showing it, scr_contract_delete unlists it. One that failed (the host's contract clock
// ran out: MpContractClockHour) also gets our part of the failure: listed as failed, the morale hit, the stat, its
// quest items gone - the world's part (reputation, the village, the dungeon) came with the host's own calls. (Legacy:
// scr_mp_contract_journal.)
function MpContractJournal(index, contract, wasTaken)
{
    if (!variable_global_exists("journalDataMap"))
        return;
    var _tasks = ds_map_find_value(global.journalDataMap, "contractsList");
    if (is_undefined(_tasks) || !ds_exists(_tasks, ds_type_list))
        return;
    var _taken = ds_map_find_value(contract, "isTaken") == true && ds_map_find_value(contract, "isActive") == true;
    if (!_taken)
    {
        scr_journalTaskDelete(_tasks, index);
        if (wasTaken && ds_map_find_value(contract, "isComplete") == -1)
        {
            var _failed = ds_map_find_value(global.journalDataMap, "tasksFailedList");
            if (!is_undefined(_failed) && ds_exists(_failed, ds_type_list) && ds_list_find_index(_failed, index) < 0)
            {
                scr_journalTaskAdd(_failed, index);
                scr_characterStatsUpdateAdd("contractsFailed", 1);
                scr_psy_change("MoraleSituational", -10, "contract_fail");
                scr_contract_quest_items_delete(contract, false, false);
            }
        }
        return;
    }
    scr_journalTaskAdd(_tasks, index);
    if (!wasTaken)
        scr_journalDiaryUpdate(contract, false, true);
    else
    {
        var _showing = false;
        with (o_diary)
        {
            if (map == contract)
                _showing = true;
        }
        if (_showing)
            scr_journalDiaryUpdate(contract);
    }
}
