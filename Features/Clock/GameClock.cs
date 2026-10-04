using System;
using System.Linq;
using StoneForge;

namespace StoneshardMP.Features.Clock;

// The game's clock and turn count (WorldClock): read, sent, and set to the host's.
internal static class GameClock
{
    /// <summary>A portable clock snapshot: seconds, minutes, hours, days, months ("|" between); "" with no game.</summary>
    public static string Snapshot()
    {
        if (!Time.Available)
            return "";
        GameTime now = Time.Now;
        return string.Join("|", now.Seconds, now.Minutes, now.Hours, now.Days, now.Months);
    }

    /// <summary>Our clock made the host's: a small forward gap passes as play does (Time.Advance), so its minute, hour
    /// and day effects still happen; then the clock is set to the host's exactly (its seconds, or a bigger or backwards
    /// gap).</summary>
    public static void Apply(int seconds, int minutes, int hours, int days, int months)
    {
        if (!Time.Available)
            return;
        GameTime host;
        try { host = new GameTime(months, days, hours, minutes, seconds); }
        catch (ArgumentOutOfRangeException) { return; }
        long gap = host.Timestamp - Time.Timestamp;
        if (gap > 0 && gap <= GameTime.MinutesPerDay && Gm.InGame)
            Time.Advance((int)gap);
        if (Time.Now != host)
            Time.Set(host);
    }

    /// <summary>Whether a world turn can go in now: a playable world, with no scene change, cutscene or turn of our own
    /// under way (the player's alarms 1 and 4 run its turn).</summary>
    public static bool TickReady()
    {
        Instance player = Player();
        if (player.IsNone || !Gm.InstanceExists(GameObjectId.o_controller) || Gm.InstanceExists(GameObjectId.o_black_overlay))
            return false;
        return !Game.IsCutscene && !player.Get("lock_movement").AsBool && Math.Max(player.Alarm[4], player.Alarm[1]) <= 0;
    }

    /// <summary>One idle world turn, as a walked tile gives: the world's upkeep and time (scr_global_turn), the skills'
    /// cooldown alarms, then the units' turns (the player's alarm 4). Only in a quiet, playable world (TickReady).</summary>
    public static bool Tick()
    {
        if (!TickReady())
            return false;
        Instance player = Player();
        Game.CallScript("scr_global_turn", player);
        foreach (Instance skill in Instances.All(GameObjectId.o_skill))
            skill.Alarm[10] = 1;
        // (GameMaker's ev_alarm, alarm 4.)
        Game.CallBuiltinAs("event_perform", player, player, 2, 4);
        return true;
    }

    /// <summary>How many turns the game has completed (o_controller's count: an action, told from an idle world tick);
    /// -1 with no game.</summary>
    public static int Turns() => Gm.InstanceExists(GameObjectId.o_player) ? Time.Turns : -1;

    private static Instance Player() => Instances.All(GameObjectId.o_player).FirstOrDefault();
}
