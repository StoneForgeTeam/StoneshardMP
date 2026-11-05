using System;
using System.Linq;
using StoneForge;

namespace StoneshardMP.Features.Music;

// Battle music while anyone here fights: the game asks whether to play it only at certain moments - a world turn, an
// enemy deciding to attack in its own game - and another player's fight, or the owner's units' (copies here, which
// decide nothing), never asked it. Twice a second: a fight started (an enemy after us - one is enough, the game waits
// for two - or a round on here for someone's combat) and the music hasn't followed, or it ended and the music we turned
// on is still playing, the music controller is asked now (its user event 1, its own check). (The MSL version's
// scr_mp_music_step.)
public sealed class BattleMusic
{
    private const int Every = 30;
    private readonly Func<bool> _inSharedWorld;
    private readonly Func<bool> _roundsInCombat;
    private int _frame;
    // (Whether we last said it's a fight: the music turned on for it is turned off only by us.)
    private bool _forced;

    public BattleMusic(Func<bool> inSharedWorld, Func<bool> roundsInCombat)
    {
        _inSharedWorld = inSharedWorld;
        _roundsInCombat = roundsInCombat;
    }

    public void Tick()
    {
        if (++_frame % Every != 0)
            return;
        if (!_inSharedWorld() || !Gm.InGame || !Gm.InstanceExists(GameObjectId.o_player)
            || Instances.All(GameObjectId.o_music_controller).FirstOrDefault() is not { IsNone: false } music)
        {
            _forced = false;
            return;
        }
        bool fight = Game.CallScript("scr_getAgredMobsCount", default, true).AsReal > 0 || _roundsInCombat();
        bool playing = music.Get("is_battle").AsBool;
        if (fight && !playing || !fight && playing && _forced && Game.CallScript("scr_getAgredMobsCount", default).AsReal == 0)
            Game.CallBuiltinAs("event_user", music, music, 1);
        _forced = fight;
    }
}
