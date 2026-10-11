using System;
using System.Collections.Generic;
using System.Linq;
using LiteNetLib;
using LiteNetLib.Utils;
using StoneForge;
using StoneshardMP.Features.Players;
using StoneshardMP.Net;

namespace StoneshardMP.Features.Effects;

// Streams the visible, sprite-only side of the local player's effects. The source objects stay entirely local:
// receiving games make an Effect (no parent, no game logic) and refresh its draw properties until it ends.
public sealed class EffectManager
{
    private const int MaxEffects = 64;
    private const int RemoteLifetimeFrames = 60;
    private static readonly string[] Roots =
    {
        "c_hit_parent", "o_spells", "o_shell", "o_spellbirth_ext", "c_buff_anim", "o_onUnitEffectSprite",
    };

    private readonly ModContext _context;
    private readonly Session _session;
    private readonly Effect _effect;
    // Current local source IDs. A missing source gets an EffectEnd packet on the following frame.
    private readonly HashSet<int> _sent = new();
    // (sender slot, sender's instance ID) -> our harmless echo.
    private readonly Dictionary<(int Slot, int Id), Instance> _remote = new();

    public EffectManager(ModContext context, Session session)
    {
        _context = context;
        _session = session;
        _effect = new Effect(this);
        context.Objects.Add(_effect);
        session.On<EffectPacket>(Receive);
        session.On<EffectEndPacket>(End);
        session.PlayerLeft += player => Forget(player.Slot);
    }

    public void Tick()
    {
        if (!_session.Connected || !Gm.InGame)
        {
            _sent.Clear();
            return;
        }

        var current = new HashSet<int>();
        foreach (string rootName in Roots)
        {
            int root = Gm.AssetGetIndex(rootName);
            if (root < 0)
                continue;
            foreach (Instance source in Instances.All(root))
            {
                if (current.Count >= MaxEffects)
                    break;
                if (!source.Exists || !VisibleSprite(source))
                    continue;
                // On-unit effects belonging to another player's object already have their own locally received visual.
                if (rootName == "o_onUnitEffectSprite" && !source["ownerIsPlayer"].AsBool)
                    continue;
                current.Add(source["id"].AsInt);
                Send(source);
            }
        }
        foreach (int ended in _sent.Except(current).ToArray())
            _session.Send(new EffectEndPacket(ended), delivery: DeliveryMethod.Sequenced);
        _sent.Clear();
        _sent.UnionWith(current);
    }

    // Effect's Step: a received end can be lost, so an echo also expires soon after its last update.
    internal void Step(Instance self)
    {
        int life = self["mp_life"].AsInt - 1;
        self["mp_life"] = life;
        if (life <= 0)
            self.Destroy();
    }

    public void Clear()
    {
        _sent.Clear();
        foreach (var echo in _remote.Values)
            if (echo.Exists)
                echo.Destroy();
        _remote.Clear();
    }

    private static bool VisibleSprite(Instance source)
    {
        int sprite = source["sprite_index"].AsInt;
        return source["visible"].AsBool && Draw.SpriteExists(sprite);
    }

    private void Send(Instance source)
    {
        _session.Send(new EffectPacket(source["id"].AsInt, source["sprite_index"].AsInt,
            (float)source["x"].AsReal, (float)source["y"].AsReal, source["depth"].AsInt,
            (float)source["image_xscale"].AsReal, (float)source["image_yscale"].AsReal,
            (float)source["image_angle"].AsReal, (float)source["image_index"].AsReal,
            (float)source["image_alpha"].AsReal, source["image_blend"].AsInt), delivery: DeliveryMethod.Sequenced);
    }

    private void Receive(RemotePlayer from, EffectPacket r)
    {
        int id = r.InstanceId;
        int sprite = r.Sprite;
        float x = r.X, y = r.Y;
        int depth = r.Depth;
        float scaleX = r.ScaleX, scaleY = r.ScaleY, angle = r.Angle, frame = r.Frame, alpha = r.Alpha;
        int blend = r.Blend;
        var key = (from.Slot, id);
        // Effects belong to a room just like their player. Do not create (or leave) an echo in a different
        // place when one player goes through a door, changes an overworld cell, or changes dungeon floor.
        PlayerState? mine = OurPlayer.State();
        if (mine == null || from.State?.Place != mine.Place)
        {
            if (_remote.Remove(key, out var elsewhere) && elsewhere.Exists)
                elsewhere.Destroy();
            return;
        }
        if (!Draw.SpriteExists(sprite))
            return;
        if (!_remote.TryGetValue(key, out var echo) || !echo.Exists)
        {
            if (_effect.Index < 0)
                return;
            echo = _effect.Create(x, y, depth).Persist();
            _remote[key] = echo;
        }
        echo["x"] = x; echo["y"] = y; echo["depth"] = depth;
        echo["sprite_index"] = sprite;
        echo["image_xscale"] = scaleX; echo["image_yscale"] = scaleY;
        echo["image_angle"] = angle; echo["image_index"] = frame;
        echo["image_alpha"] = alpha; echo["image_blend"] = blend;
        echo["mp_life"] = RemoteLifetimeFrames;
    }

    private void End(RemotePlayer from, EffectEndPacket r)
    {
        var key = (from.Slot, r.InstanceId);
        if (_remote.Remove(key, out var echo) && echo.Exists)
            echo.Destroy();
    }

    private void Forget(int slot)
    {
        foreach (var key in _remote.Keys.Where(key => key.Slot == slot).ToArray())
            if (_remote.Remove(key, out var echo) && echo.Exists)
                echo.Destroy();
    }
}
