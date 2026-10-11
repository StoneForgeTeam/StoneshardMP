using System;
using System.Collections.Generic;
using System.Linq;
using StoneForge;
using StoneshardMP.Features.Areas;
using StoneshardMP.Features.Players;
using StoneshardMP.Net;
using StoneshardMP.Net.Packets;

namespace StoneshardMP.Features.Bombs;

// A player's thrown bombs where players are together - a Smoke Bomb, a Nistrian Flame Flask, a Spider Blood Flask, a
// Deathstinger Jar. A bomb bursts in its thrower's game only (o_throwed_loot's user event 1, where the game's throw lands:
// the other games only fly a copy of the item - LootSync), and what it leaves mostly syncs already: its smoke, fire and
// acid pool are ground effects (GroundEffectSync), its hit and what it puts on its target are the thrower's (CombatSync),
// the fire flask's blast is drawn everywhere (EffectManager). What didn't:
// - The burst itself: the others saw the bottle vanish. The thrower tells them where it burst, and their games show and
//   sound it there as the game does - the smoke's puff, the acid's splash, the fire's sparks, scorch and hole, the
//   glass - all for show: nothing that hurts, burns or spreads (that's the ground effects', from the owner).
// - A Deathstinger Jar's swarm: an o_enemy_spawner that lets out o_hornets a moment later - a unit, and a place's units
//   are its owner's (AreaUnits). Out of a follower's jar it was in the follower's game alone, and taken away there as a
//   unit the owner hadn't got. The follower's spawner is put out and the owner lets the swarm out in the same cell; the
//   follower gets it with the owner's units.
public sealed class BombSync
{
    // (The game's: the spawner a jar makes lets its swarm out after this many frames.)
    private const int SwarmDelay = 15;
    private const double Tile = 26;
    private static readonly GameObjectId[] Bombs =
    {
        GameObjectId.o_loot_smokebomb, GameObjectId.o_loot_bomb_fire, GameObjectId.o_loot_bomb_acid, GameObjectId.o_loot_bomb_bee,
    };

    private readonly Session _session;
    private readonly AreaOwnership _ownership;
    private readonly Func<bool> _inSharedWorld;
    private readonly Random _random = new();
    // The spawners there were as one of our player's throws landed (a jar's is the one that wasn't).
    private HashSet<Instance>? _spawners;
    private int _sent, _shown, _swarms;

    public BombSync(ModContext context, Session session, AreaOwnership ownership, Func<bool> inSharedWorld)
    {
        _session = session;
        _ownership = ownership;
        _inSharedWorld = inSharedWorld;
        session.On<BombLandedPacket>(ReceiveLanded);
        session.On<SwarmReleasedPacket>(ReceiveSwarm);
        context.OnCode("gml_Object_o_throwed_loot_Other_11", before: (self, _) => { Landing(self); return false; },
            after: (self, _) => Landed());
    }

    /// <summary>Dev tools: a line on where it's got to.</summary>
    public string DevSummary => $"bursts sent {_sent}, shown {_shown}, swarms passed on {_swarms}";

    public void Clear() => _spawners = null;

    private bool Ready => _session.Connected && _inSharedWorld() && Gm.InGame && !Rooms.IsChanging
        && _ownership.Place != null && _ownership.Place == OurPlayer.State()?.Place && _ownership.Role != AreaRole.Alone;

    // One of our player's throws landing: a bomb's burst, to the others here.
    private void Landing(Instance shell)
    {
        _spawners = null;
        if (!Ready || shell.IsNone || !Ours(shell) || Instance.Of(shell.Get("loot_object")) is not { IsNone: false } loot || !loot.Exists)
            return;
        int obj = loot.Get("object_index").AsInt;
        if (!Bombs.Any(b => obj == (int)b))
            return;
        var landed = new BombLandedPacket(_ownership.Place!, Gm.ObjectGetName(obj), loot.Get("x").AsReal, loot.Get("y").AsReal);
        foreach (int slot in _ownership.Others)
            _session.Send(landed, slot);
        _sent++;
        if (obj == (int)GameObjectId.o_loot_bomb_bee && _ownership.Role == AreaRole.Follower)
            _spawners = Instances.All(GameObjectId.o_enemy_spawner).Select(s => s.Persist()).ToHashSet();
    }

    // A follower's jar: its spawner put out, and the swarm the owner's to let out.
    private void Landed()
    {
        if (_spawners == null)
            return;
        var before = _spawners;
        _spawners = null;
        if (!Ready || _ownership.Role != AreaRole.Follower)
            return;
        foreach (Instance spawner in Instances.All(GameObjectId.o_enemy_spawner).Where(s => !before.Contains(s.Persist())).ToList())
        {
            if (spawner.Get("unit").AsInt != (int)GameObjectId.o_hornets)
                continue;
            _session.Send(new SwarmReleasedPacket(_ownership.Place!, spawner.Get("x").AsReal, spawner.Get("y").AsReal), _ownership.Owner);
            spawner.Destroy();
            _swarms++;
        }
    }

    private static bool Ours(Instance shell)
        => Instance.Of(shell.Get("owner")) is { IsNone: false } owner && owner.Exists
            && owner.Get("object_index").AsInt == (int)GameObjectId.o_player;

    // Owner: a follower's jar - its swarm let out here, as the game lets one out.
    private void ReceiveSwarm(RemotePlayer from, SwarmReleasedPacket packet)
    {
        if (!Ready || _ownership.Role != AreaRole.Owner || packet.Place != _ownership.Place || !_ownership.Others.Contains(from.Slot))
            return;
        var spawner = Gm.Create<GameInstance>(packet.X, packet.Y, 0, GameObjectId.o_enemy_spawner);
        spawner.Instance.Set("unit", (int)GameObjectId.o_hornets);
        spawner.Alarm[0] = SwarmDelay;
    }

    // Another player's bomb bursting here: shown and sounded, for show only.
    private void ReceiveLanded(RemotePlayer from, BombLandedPacket packet)
    {
        if (!Ready || packet.Place != _ownership.Place || from.State?.Place != packet.Place)
            return;
        double x = packet.X, y = packet.Y;
        switch (packet.Bomb)
        {
            case nameof(GameObjectId.o_loot_smokebomb):
                Puff(x, y);
                Play(Sound.snd_smokebomb_activate, x, y);
                break;
            case nameof(GameObjectId.o_loot_bomb_fire):
                Glass(x, y);
                Play(Sound.snd_oil_explosion, x, y);
                Make(x, y, GameObjectId.o_explosion_hole);
                // (The blast's scorch: its cell's, and most of the cells around - as the game's blast, each a scorch.)
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        if ((dx == 0 && dy == 0) || _random.Next(100) < 60)
                            Scorch(x + dx * Tile, y + dy * Tile);
                break;
            case nameof(GameObjectId.o_loot_bomb_acid):
                Glass(x, y);
                int drops = _random.Next(3, 10);
                for (int i = 0; i < drops; i++)
                {
                    var drop = Make(x + _random.Next(-5, 6), y, GameObjectId.o_acid_particles);
                    drop?.Set("direction", _random.Next(60, 121));
                    drop?.Set("speed", 3 + _random.NextDouble() * 7);
                }
                break;
            case nameof(GameObjectId.o_loot_bomb_bee):
                Glass(x, y);
                break;
            default:
                return;
        }
        _shown++;
    }

    // The smoke's puff (the game's scr_guiAnimation of it, not blocking our player's actions - it isn't their bomb).
    private void Puff(double x, double y)
    {
        if (Make(x, y, GameObjectId.o_gui_animation) is not { } puff)
            return;
        puff.Set("sprite_index", (int)(_random.Next(2) == 0 ? Sprite.s_smokepart01 : Sprite.s_smokepart02));
        puff.Set("charges", 1);
        puff.Set("image_speed", 1);
        puff.Set("draw_with_alpha", false);
        puff.Set("isBlockActions", false);
    }

    // A blast's scorch on a cell, with its sparks (o_explosion_impact's Create, without the fire and the hurt).
    private void Scorch(double x, double y)
    {
        Make(x - 13 + _random.Next(-10, 11), y - 13 + _random.Next(-10, 11), GameObjectId.o_burning_decails);
        int sparks = 2 + _random.Next(3);
        for (int i = 0; i < sparks; i++)
        {
            var spark = Make(x + _random.Next(-3, 4), y, GameObjectId.o_firemagick_paricle);
            spark?.Set("speed", 2 + _random.NextDouble() * 2);
            spark?.Set("direction", 45 + _random.NextDouble() * 100);
        }
    }

    private void Glass(double x, double y)
        => Play(_random.Next(3) switch { 0 => Sound.snd_glass_impact_1, 1 => Sound.snd_glass_impact_2, _ => Sound.snd_glass_impact_3 }, x, y);

    // (The game's scr_audio_play_at: heard from where it is.)
    private static void Play(Sound sound, double x, double y)
        => Game.CallBuiltin("audio_play_sound_at", (int)sound, x, y, 0, 52, 390, 1, false, 4);

    // (Gone at once if it's off the ground - a scorch on a wall, as in the game.)
    private static Instance? Make(double x, double y, GameObjectId obj)
    {
        Instance made = Gm.Create<GameInstance>(x, y, 0, obj).Instance;
        return made.IsNone || !made.Exists ? null : made;
    }
}
