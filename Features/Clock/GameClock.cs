using System;
using StoneForge;

namespace StoneshardMP.Features.Clock;

// The game's clock and turn count (WorldClock): read, sent, and set to the host's.
internal static class GameClock
{
    private static readonly string[] Parts = { "seconds", "minutes", "hours", "days", "months" };

    /// <summary>A portable clock snapshot: seconds, minutes, hours, days, months ("|" between); "" with no game.</summary>
    public static string Snapshot()
    {
        GmValue time = Game.Global["timeDataMap"];
        if (!Ds.IsMap(time))
            return "";
        return string.Join("|", System.Array.ConvertAll(Parts, part => GmJson.Text(Ds.Get(time, part))));
    }

    /// <summary>Our clock made the host's: a small forward gap through the game's own elapsed-time processing, so its
    /// minute, hour and day effects still happen; a bigger or backwards one set exactly.</summary>
    public static void Apply(int seconds, int minutes, int hours, int days, int months)
    {
        GmValue time = Game.Global["timeDataMap"];
        if (!Ds.IsMap(time))
            return;
        double host = minutes + hours * 60 + days * 1440 + months * 43200;
        double gap = host - Game.CallScript("scr_timeGetTimestamp", default).AsReal;
        if (gap > 0 && gap <= 1440)
            Game.CallScript("scr_timePartsUpdate", default, gap);
        else if (gap != 0)
            Game.CallScript("scr_timeSet", default, seconds, minutes, hours, days, months);
        Ds.Set(time, "seconds", seconds);
    }

    /// <summary>Whether a world turn can go in now: a playable world, with no scene change, cutscene or turn of our own
    /// under way (the player's alarms 1 and 4 run its turn).</summary>
    public static bool TickReady()
    {
        Instance player = InGame.Player;
        if (player.IsNone || !InGame.Exists(GameObjectId.o_controller) || InGame.Exists(GameObjectId.o_black_overlay))
            return false;
        // (As the player: scr_is_cutscene reads object_index.)
        return !Game.CallScript("scr_is_cutscene", player).AsBool && !player.Get("lock_movement").AsBool
            && Math.Max(player.Alarm[4], player.Alarm[1]) <= 0;
    }

    /// <summary>One idle world turn, as a walked tile gives: the world's upkeep and time (scr_global_turn), the skills'
    /// cooldown alarms, then the units' turns (the player's alarm 4). Only in a quiet, playable world (TickReady).</summary>
    public static bool Tick()
    {
        if (!TickReady())
            return false;
        Instance player = InGame.Player;
        Game.CallScript("scr_global_turn", player);
        foreach (Instance skill in InGame.All(GameObjectId.o_skill))
            skill.Alarm[10] = 1;
        // (GameMaker's ev_alarm, alarm 4.)
        Game.CallBuiltinAs("event_perform", player, player, 2, 4);
        return true;
    }

    /// <summary>How many turns the game has completed (o_controller's count: an action, told from an idle world tick);
    /// -1 with no game.</summary>
    public static int Turns()
    {
        if (!InGame.Exists(GameObjectId.o_player) || InGame.First(GameObjectId.o_controller) is not { IsNone: false } controller)
            return -1;
        return controller.Get("turns").AsInt;
    }
}
