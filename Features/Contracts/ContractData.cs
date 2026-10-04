using System.Collections.Generic;
using System.Linq;
using StoneForge;

namespace StoneshardMP.Features.Contracts;

// The game side of shared contracts (ContractSync): the contract lists as JSON, a contract copied into ours in place
// with its journal entry, and the host's deadline clock. (Legacy: scr_mp_contract_*.)
internal static class ContractData
{
    /// <summary>A contract list - 0: every kind of contract (global.contractsDatabaseList), 1: the ones handed out
    /// (global.contractsDataList; a contract's index there is its id everywhere: a dungeon's contract_map) - or
    /// null.</summary>
    public static DsList? List(int list) => Game.Global[list == 0 ? "contractsDatabaseList" : "contractsDataList"].AsDsList;

    /// <summary>Every contract in a list as it is now, each its own JSON, by index ("" for a slot that isn't one); null
    /// with no such list.</summary>
    public static List<string>? Export(int list)
    {
        if (List(list) is not { } contracts)
            return null;
        int count = contracts.Count;
        var all = new List<string>(count);
        for (int i = 0; i < count; i++)
            all.Add(contracts.GetMap(i) is { } map ? map.ToJson() : "");
        return all;
    }

    /// <summary>A contract from another game (its list, index and JSON; full: the host's full copy for a client just
    /// in its world) copied into ours in place (DsMap.AssignFrom), so the game's own references to it - the journal, the
    /// diary, a dungeon, and the lists nested in it, such as the diary's targets - see it. Deadlines (Contract_Deadline)
    /// are counted by the host's clock only (ClockHour): a client takes the host's; the host keeps its own for a contract
    /// it already had taken, and takes the taker's for a newly taken one. An offer's expiry (Contract_Expiration) ticks
    /// in every game, so ours is kept, except in the full copy. A handed-out contract also goes into our journal and diary
    /// (Journal). Returns the contract's JSON as it now is here (what we have counts as synced: not sent back), "" if it
    /// couldn't be applied.</summary>
    public static string Apply(int list, int index, string json, bool full, bool host)
    {
        if (List(list) is not { } contracts || index < 0 || DsMap.FromJson(json) is not { } source)
            return "";
        try
        {
            while (contracts.Count <= index)
                contracts.AddMap(DsMap.Create());
            if (contracts.GetMap(index) is not { } contract)
                return "";
            bool wasTaken = IsTrue(contract["isTaken"]);
            if (!full)
            {
                if (host && wasTaken && contract.Has("Contract_Deadline"))
                    source["Contract_Deadline"] = contract["Contract_Deadline"];
                if (contract.Has("Contract_Expiration"))
                    source["Contract_Expiration"] = contract["Contract_Expiration"];
            }
            contract.AssignFrom(source);
            if (list == 1)
                Journal(index, contract, wasTaken);
            return contract.ToJson();
        }
        finally
        {
            source.Destroy();
        }
    }

    // After another game changed a handed-out contract: listed in the journal while it's taken, and the diary (the task
    // shown as current) kept in step, as the game does on the side that made the change - scr_contract_take lists it and
    // switches the diary to it, scr_contract_target_number_change refreshes the diary if it's showing it,
    // scr_contract_delete unlists it. One that failed (the host's contract clock ran out: ClockHour) also gets our part
    // of the failure: listed as failed, the morale hit, the stat, its quest items gone - the world's part (reputation,
    // the village, the dungeon) came with the host's own calls.
    private static void Journal(int index, DsMap contract, bool wasTaken)
    {
        if (StoneForge.Journal.Contracts is not { } tasks)
            return;
        bool taken = IsTrue(contract["isTaken"]) && IsTrue(contract["isActive"]);
        if (!taken)
        {
            StoneForge.Journal.RemoveTask(tasks, index);
            if (wasTaken && contract["isComplete"].AsReal == -1 && StoneForge.Journal.Failed is { } failed
                && !StoneForge.Journal.Lists(failed, index))
            {
                StoneForge.Journal.AddTask(failed, index);
                StoneForge.Player.AddStat("contractsFailed");
                StoneForge.Player.ChangePsyche("MoraleSituational", -10, "contract_fail");
                StoneForge.Contracts.DeleteQuestItems(contract);
            }
            return;
        }
        StoneForge.Journal.AddTask(tasks, index);
        if (!wasTaken)
            StoneForge.Journal.ShowInDiary(contract, asNew: true);
        else if (StoneForge.Journal.DiaryShows(contract))
            StoneForge.Journal.ShowInDiary(contract);
    }

    /// <summary>The host's hourly contract clock, in place of the game's (o_time_controller user event 3) while playing
    /// together: every taken contract's deadline goes down an hour - by the contract list, not the world map's dungeon
    /// list as the game does - unless a player of this world is on its dungeon's world-map cell (the game's own pause,
    /// for anyone: us, or a tile in occupied, "x_y," each), and it fails at 0 as the game's does. What happened, for the
    /// log ("" if nothing).</summary>
    public static string ClockHour(string occupied)
    {
        if (List(1) is not { } contracts)
            return "";
        string here = WorldMap.PlayerCell is var (x, y) ? $"{x}_{y}" : "";
        string line = "";
        for (int i = 0; i < contracts.Count; i++)
        {
            // (GameMaker truthiness: above 0.5 - a failed contract's isComplete is -1, false.)
            if (contracts.GetMap(i) is not { } contract || !contract["isTaken"].AsBool || contract["isComplete"].AsBool)
                continue;
            GmValue deadline = contract["Contract_Deadline"];
            if (deadline.Kind != GmKind.Real)
                continue;
            // Its dungeon's cell, "x/y" -> "x_y".
            string dungeon = contract["Dungeon_Coordinate"] is { Kind: GmKind.String } at && at.AsString.Contains('/')
                ? at.AsString.Replace('/', '_') : "";
            if (dungeon.Length > 0 && (dungeon == here || ("," + occupied).Contains("," + dungeon + ",")))
            {
                line += $" #{i} {deadline}h (paused: a player is at its dungeon)";
                continue;
            }
            double left = deadline.AsReal - 1;
            contract["Contract_Deadline"] = left;
            line += $" #{i} {(GmValue)left}h";
            if (left <= 0)
            {
                line += " FAILED";
                StoneForge.Contracts.Delete(contract);
            }
        }
        return line;
    }

    // GameMaker's "== true": a true, or the number 1.
    private static bool IsTrue(GmValue value) => value.Kind is GmKind.Bool or GmKind.Real && value.AsReal == 1;
}
