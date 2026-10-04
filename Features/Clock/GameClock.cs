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
    /// and day effects still happen; a bigger or backwards one is set exactly (Time.Set). The seconds - all that differs
    /// on most ticks - are written as they are: setting the time also works out the time of day again, every tick.</summary>
    public static void Apply(int seconds, int minutes, int hours, int days, int months)
    {
        if (!Time.Available || Game.Global["timeDataMap"].AsDsMap is not { } clock)
            return;
        GameTime host;
        try { host = new GameTime(months, days, hours, minutes, seconds); }
        catch (ArgumentOutOfRangeException) { return; }
        GameTime now = Time.Now;
        long gap = host.Timestamp - now.Timestamp;
        if (gap > 0 && gap <= GameTime.MinutesPerDay && Gm.InGame)
            Time.Advance((int)gap);
        else if (gap != 0)
        {
            Time.Set(host);
            return;
        }
        if (now.Seconds != seconds || gap != 0)
            clock["seconds"] = seconds;
    }

    /// <summary>Whether a world turn can go in now: a playable world, with no scene change, cutscene or turn of our own
    /// under way (the player's alarms 1 and 4 run its turn).</summary>
    public static bool TickReady() => TickReady(null);

    // (With a context: each part timed for the profiler.)
    private static bool TickReady(ModContext? context)
    {
        bool Part(string name, Func<bool> part) => context == null ? part() : Profiler.Measure(context, "clock: ready? " + name, part);
        Instance player = Player();
        if (!Part("world", () => !player.IsNone && Gm.InstanceExists(GameObjectId.o_controller) && !Gm.InstanceExists(GameObjectId.o_black_overlay)))
            return false;
        return Part("cutscene", () => !Game.IsCutscene)
            && Part("locked", () => !player.Get("lock_movement").AsBool)
            && Part("player's turn (alarms)", () => Math.Max(player.Alarm[4], player.Alarm[1]) <= 0);
    }

    /// <summary>One idle world turn, as a walked tile gives: the world's upkeep and time (scr_global_turn), the skills'
    /// cooldown alarms, then the units' turns (the player's alarm 4). Only in a quiet, playable world (TickReady). Each
    /// part timed for the profiler: the game's own turn, as costly as any other.</summary>
    public static bool Tick(ModContext context)
    {
        if (!Profiler.Measure(context, "clock: turn ready?", () => TickReady(context)))
            return false;
        Instance player = Player();
        Profiler.Measure(context, "clock: world turn (scr_global_turn)", StoneForge.Turns.PassWorld);
        // (Skills can be cast again: o_abilities' alarm 10 sets global.skill_can_cast - every skill the same global, and
        // none of o_skill's children does more - so one skill's alarm does what every skill's did. Each alarm set is a
        // slow call into the game, and there are hundreds of skills.)
        Profiler.Measure(context, "clock: skills' cooldowns", () =>
        {
            Instance skill = Instances.All(GameObjectId.o_skill).FirstOrDefault();
            if (!skill.IsNone)
                skill.Alarm[10] = 1;
        });
        // (GameMaker's ev_alarm, alarm 4.)
        Profiler.Measure(context, "clock: units' turns (alarm 4)", StoneForge.Turns.RunUnits);
        return true;
    }

    /// <summary>How many turns the game has completed (o_controller's count: an action, told from an idle world tick);
    /// -1 with no game.</summary>
    public static int Turns() => Gm.InstanceExists(GameObjectId.o_player) ? Time.Turns : -1;

    private static Instance Player() => Instances.All(GameObjectId.o_player).FirstOrDefault();
}
