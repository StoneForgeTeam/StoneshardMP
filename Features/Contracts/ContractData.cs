using System.Collections.Generic;
using StoneForge;

namespace StoneshardMP.Features.Contracts;

// The game side of shared contracts (ContractSync): the contract lists as JSON, a contract copied into ours in place
// with its journal entry, and the host's deadline clock. (Legacy: scr_mp_contract_*.)
internal static class ContractData
{
    /// <summary>A contract list - 0: every kind of contract (global.contractsDatabaseList), 1: the ones handed out
    /// (global.contractsDataList; a contract's index there is its id everywhere: a dungeon's contract_map) - or
    /// undefined.</summary>
    public static GmValue List(int list)
    {
        GmValue found = Game.Global[list == 0 ? "contractsDatabaseList" : "contractsDataList"];
        return Ds.IsList(found) ? found : GmValue.Undefined;
    }

    /// <summary>Every contract in a list as it is now, each its own JSON, by index ("" for a slot that isn't one); null
    /// with no such list.</summary>
    public static List<string>? Export(int list)
    {
        GmValue contracts = List(list);
        if (contracts.IsUndefined)
            return null;
        int count = Ds.Count(contracts);
        var all = new List<string>(count);
        for (int i = 0; i < count; i++)
        {
            GmValue map = Ds.At(contracts, i);
            all.Add(Ds.IsMap(map) ? Ds.ToJson(map) : "");
        }
        return all;
    }

    /// <summary>A contract from another game (its list, index and JSON; full: the host's full copy for a client just
    /// in its world) copied into ours in place (Assign), so the game's own references to it - the journal, the diary, a
    /// dungeon - see it. Deadlines (Contract_Deadline) are counted by the host's clock only (ClockHour): a client takes
    /// the host's; the host keeps its own for a contract it already had taken, and takes the taker's for a newly taken
    /// one. An offer's expiry (Contract_Expiration) ticks in every game, so ours is kept, except in the full copy. A
    /// handed-out contract also goes into our journal and diary (Journal). Returns the contract's JSON as it now is here
    /// (what we have counts as synced: not sent back), "" if it couldn't be applied.</summary>
    public static string Apply(int list, int index, string json, bool full, bool host)
    {
        GmValue contracts = List(list);
        if (contracts.IsUndefined || index < 0)
            return "";
        GmValue source = Ds.FromJson(json);
        if (!Ds.IsMap(source))
            return "";
        while (Ds.Count(contracts) <= index)
        {
            Game.CallBuiltin("ds_list_add", contracts, Game.CallBuiltin("ds_map_create"));
            Game.CallBuiltin("ds_list_mark_as_map", contracts, Ds.Count(contracts) - 1);
        }
        GmValue contract = Ds.At(contracts, index);
        bool wasTaken = IsTrue(Ds.Get(contract, "isTaken"));
        if (!full)
        {
            if (host && wasTaken && Ds.Has(contract, "Contract_Deadline"))
                Ds.Set(source, "Contract_Deadline", Ds.Get(contract, "Contract_Deadline"));
            if (Ds.Has(contract, "Contract_Expiration"))
                Ds.Set(source, "Contract_Expiration", Ds.Get(contract, "Contract_Expiration"));
        }
        Assign(contract, source);
        Ds.Destroy(source);
        if (list == 1)
            Journal(index, contract, wasTaken);
        return Ds.ToJson(contract);
    }

    // After another game changed a handed-out contract: listed in the journal while it's taken, and the diary (the task
    // shown as current) kept in step, as the game does on the side that made the change - scr_contract_take lists it and
    // switches the diary to it, scr_contract_target_number_change refreshes the diary if it's showing it,
    // scr_contract_delete unlists it. One that failed (the host's contract clock ran out: ClockHour) also gets our part
    // of the failure: listed as failed, the morale hit, the stat, its quest items gone - the world's part (reputation,
    // the village, the dungeon) came with the host's own calls.
    private static void Journal(int index, GmValue contract, bool wasTaken)
    {
        GmValue journal = Game.Global["journalDataMap"];
        if (!Ds.IsMap(journal))
            return;
        GmValue tasks = Ds.Get(journal, "contractsList");
        if (!Ds.IsList(tasks))
            return;
        bool taken = IsTrue(Ds.Get(contract, "isTaken")) && IsTrue(Ds.Get(contract, "isActive"));
        if (!taken)
        {
            Game.CallScript("scr_journalTaskDelete", default, tasks, index);
            if (wasTaken && Ds.Get(contract, "isComplete").AsReal == -1)
            {
                GmValue failed = Ds.Get(journal, "tasksFailedList");
                if (Ds.IsList(failed) && Game.CallBuiltin("ds_list_find_index", failed, index).AsInt < 0)
                {
                    Game.CallScript("scr_journalTaskAdd", default, failed, index);
                    Game.CallScript("scr_characterStatsUpdateAdd", default, "contractsFailed", 1);
                    Game.CallScript("scr_psy_change", default, "MoraleSituational", -10, "contract_fail");
                    Game.CallScript("scr_contract_quest_items_delete", default, contract, false, false);
                }
            }
            return;
        }
        Game.CallScript("scr_journalTaskAdd", default, tasks, index);
        if (!wasTaken)
            Game.CallScript("scr_journalDiaryUpdate", default, contract, false, true);
        else if (InGame.All(GameObjectId.o_diary).Exists(diary => diary.Get("map").AsReal == contract.AsReal))
            Game.CallScript("scr_journalDiaryUpdate", default, contract);
    }

    /// <summary>The host's hourly contract clock, in place of the game's (o_time_controller user event 3) while playing
    /// together: every taken contract's deadline goes down an hour - by the contract list, not the world map's dungeon
    /// list as the game does - unless a player of this world is on its dungeon's world-map cell (the game's own pause,
    /// for anyone: us, or a tile in occupied, "x_y," each), and it fails at 0 as the game's does. What happened, for the
    /// log ("" if nothing).</summary>
    public static string ClockHour(string occupied)
    {
        GmValue contracts = List(1);
        if (contracts.IsUndefined)
            return "";
        string here = GmJson.Text(Game.Global["playerGridX"]) + "_" + GmJson.Text(Game.Global["playerGridY"]);
        string line = "";
        for (int i = 0; i < Ds.Count(contracts); i++)
        {
            GmValue contract = Ds.At(contracts, i);
            // (GameMaker truthiness: above 0.5 - a failed contract's isComplete is -1, false.)
            if (!Ds.IsMap(contract) || !Ds.Get(contract, "isTaken").AsBool || Ds.Get(contract, "isComplete").AsBool)
                continue;
            GmValue deadline = Ds.Get(contract, "Contract_Deadline");
            if (deadline.Kind != GmKind.Real)
                continue;
            // Its dungeon's cell, "x/y" -> "x_y".
            string dungeon = Ds.Get(contract, "Dungeon_Coordinate") is { Kind: GmKind.String } at && at.AsString.Contains('/')
                ? at.AsString.Replace('/', '_') : "";
            if (dungeon.Length > 0 && (dungeon == here || ("," + occupied).Contains("," + dungeon + ",")))
            {
                line += $" #{i} {GmJson.Text(deadline)}h (paused: a player is at its dungeon)";
                continue;
            }
            double left = deadline.AsReal - 1;
            Ds.Replace(contract, "Contract_Deadline", left);
            line += $" #{i} {GmJson.Text(left)}h";
            if (left <= 0)
            {
                line += " FAILED";
                Game.CallScript("scr_contract_delete", InGame.First(GameObjectId.o_time_controller), contract, false);
            }
        }
        return line;
    }

    // Make contract map dst a copy of src (from json_decode), in place, so everything holding dst sees the new contents.
    // Keys src lacks are removed; nested lists and maps are updated in place (Fill), so references to them (the diary's
    // contract targets) stay good.
    private static void Assign(GmValue dst, GmValue src)
    {
        foreach (GmValue key in Ds.Keys(dst))
            if (!Ds.Has(src, key))
                Ds.Delete(dst, key);
        foreach (GmValue key in Ds.Keys(src))
        {
            GmValue value = Ds.Get(src, key);
            if (Ds.KeyIsList(src, key))
            {
                if (Ds.Has(dst, key) && Ds.KeyIsList(dst, key))
                    Fill(Ds.Get(dst, key), value);
                else
                {
                    Ds.Delete(dst, key);
                    GmValue list = Game.CallBuiltin("ds_list_create");
                    Fill(list, value);
                    Game.CallBuiltin("ds_map_add_list", dst, key, list);
                }
            }
            else if (Ds.KeyIsMap(src, key))
            {
                if (Ds.Has(dst, key) && Ds.KeyIsMap(dst, key))
                    Game.CallBuiltin("ds_map_copy", Ds.Get(dst, key), value);
                else
                {
                    Ds.Delete(dst, key);
                    GmValue map = Game.CallBuiltin("ds_map_create");
                    Game.CallBuiltin("ds_map_copy", map, value);
                    Game.CallBuiltin("ds_map_add_map", dst, key, map);
                }
            }
            else
            {
                if (Ds.Has(dst, key) && (Ds.KeyIsList(dst, key) || Ds.KeyIsMap(dst, key)))
                    Ds.Delete(dst, key);
                Ds.Set(dst, key, value);
            }
        }
    }

    // Make ds_list dst a copy of ds_list src (from json_decode), in place - its nested lists and maps too, element by
    // element, so whatever holds one of them still holds the same, now updated, one. New nested ones are made and
    // marked; extra elements dropped from the end. One level down (a contract's Targets is a list of lists of plain
    // values); deeper is copied as is.
    private static void Fill(GmValue dst, GmValue src)
    {
        int count = Ds.Count(src);
        for (int i = 0; i < count; i++)
        {
            GmValue value = Ds.At(src, i);
            bool have = i < Ds.Count(dst);
            bool isList = Game.CallBuiltin("ds_list_is_list", src, i).AsBool;
            bool isMap = !isList && Game.CallBuiltin("ds_list_is_map", src, i).AsBool;
            if (isList || isMap)
            {
                if (have && Game.CallBuiltin(isList ? "ds_list_is_list" : "ds_list_is_map", dst, i).AsBool)
                {
                    Game.CallBuiltin(isList ? "ds_list_copy" : "ds_map_copy", Ds.At(dst, i), value);
                    continue;
                }
                GmValue nested = Game.CallBuiltin(isList ? "ds_list_create" : "ds_map_create");
                Game.CallBuiltin(isList ? "ds_list_copy" : "ds_map_copy", nested, value);
                if (have)
                    Game.CallBuiltin("ds_list_replace", dst, i, nested);
                else
                    Game.CallBuiltin("ds_list_add", dst, nested);
                Game.CallBuiltin(isList ? "ds_list_mark_as_list" : "ds_list_mark_as_map", dst, i);
            }
            else if (have)
                Game.CallBuiltin("ds_list_replace", dst, i, value);
            else
                Game.CallBuiltin("ds_list_add", dst, value);
        }
        while (Ds.Count(dst) > count)
            Game.CallBuiltin("ds_list_delete", dst, Ds.Count(dst) - 1);
    }

    // GameMaker's "== true": a true, or the number 1.
    private static bool IsTrue(GmValue value) => value.Kind is GmKind.Bool or GmKind.Real && value.AsReal == 1;
}
