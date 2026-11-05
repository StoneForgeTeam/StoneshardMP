using System;
using StoneForge;
using StoneshardMP.Features.Players;
using StoneshardMP.Net;

namespace StoneshardMP.Features.Music;

// A lute's notes are heard by everyone there: each note a player plays (o_music_button, pressed - by a click or its key)
// is played in the others' games too, from that player's character, with the game's floating music notes over them as
// their own game shows. The music fades out while another player plays, as it does while our own lute is open
// (o_music_menu), and comes back 10 seconds after their last note. (The MSL version's NOTE: #65.)
public sealed class LuteSync
{
    // (The game's music: its audio group 1, at global.gain_music - faded out and back as the lute's menu does it.)
    private const int MusicGroup = 1;
    private const int QuietFor = 10_000;
    private readonly Session _session;
    private readonly PlayerManager _players;
    private readonly Func<bool> _inSharedWorld;
    // Music faded out for another's lute until then (Environment.TickCount64; 0: not), and whether our own lute was open
    // last frame - closing it fades the music back in, and it's faded out again while another still plays.
    private long _quietUntil;
    private bool _menuWasOpen;
    // (A note button not pressed as its pressed event starts: pressed by its end, the note was played.)
    private bool _wasPressed;

    public LuteSync(ModContext context, Session session, PlayerManager players, Func<bool> inSharedWorld)
    {
        _session = session;
        _players = players;
        _inSharedWorld = inSharedWorld;
        session.On<LuteNotePacket>(Receive);
        context.OnCode("gml_Object_o_music_button_Other_25",
            before: (button, _) =>
            {
                _wasPressed = button.IsNone || button.Get("pressed").AsBool;
                return false;
            },
            after: (button, _) =>
            {
                if (_wasPressed || button.IsNone || !button.Exists || !button.Get("pressed").AsBool)
                    return;
                if (_session.Connected && _inSharedWorld() && button.Get("click_snd") is { Kind: GmKind.Real } sound
                    && sound.AsInt >= 0)
                    _session.Send(new LuteNotePacket(sound.AsInt));
            });
    }

    public void Tick()
    {
        bool menu = Gm.InstanceExists(GameObjectId.o_music_menu);
        if (_quietUntil > 0)
        {
            if (!menu && _menuWasOpen)
                Game.CallBuiltin("audio_group_set_gain", MusicGroup, 0, 700);
            if (Environment.TickCount64 >= _quietUntil || !_session.Connected)
            {
                _quietUntil = 0;
                if (!menu)
                    Game.CallBuiltin("audio_group_set_gain", MusicGroup, Game.Global["gain_music"], 2000);
            }
        }
        _menuWasOpen = menu;
    }

    private void Receive(RemotePlayer from, LuteNotePacket packet)
    {
        if (!_inSharedWorld() || !Gm.InGame || _players.UnitOf(from.Slot) is not { IsNone: false } player
            || packet.Sound < 0 || !Game.CallBuiltin("audio_exists", packet.Sound).AsBool)
            return;
        double x = player.Get("x").AsReal, y = player.Get("y").AsReal;
        Game.CallBuiltin("audio_play_sound_at", packet.Sound, x, y, 0, 52, 390, 1, false, 2);
        Gm.Create<GameInstance>(x + Random.Shared.Next(-15, 16), y - Random.Shared.Next(15, 21),
            player.Get("depth").AsReal - 1, GameObjectId.o_musicicon);
        if (_quietUntil == 0 && !Gm.InstanceExists(GameObjectId.o_music_menu))
            Game.CallBuiltin("audio_group_set_gain", MusicGroup, 0, 700);
        _quietUntil = Environment.TickCount64 + QuietFor;
    }
}
