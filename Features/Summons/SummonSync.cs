using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using StoneForge;
using StoneshardMP.Features.Players;
using StoneshardMP.Net;
using StoneshardMP.Net.Packets;

namespace StoneshardMP.Features.Summons;

// Summons where players are together - an Astral Phantasm, a Mana Crystal: whoever cast one owns it.
// - Its caster's game runs it, as the game made it: its turn picks targets and scales its damage from its owner - the
//   caster's own character - and it's friendly because its owner is the local player. (In anyone else's game none of
//   that could hold: its owner there is a stand-in.) AreaUnits leaves a player's summons alone - not sent as the area's
//   units, not removed from a follower's game.
// - The caster sends its summons in the place (SummonsPacket, as they change and every second); everyone else shows a
//   stand-in for each - the player stand-in's object, passive, on the Player side, in its cell (marked mp_summon). It's
//   drawn as the summon looks: its own sprite if it's one of the game's (a Mana Crystal), or - an Astral Phantasm, a
//   duplicate of its caster's composited sprite, which exists only in the caster's game - its caster's look as their
//   stand-in draws it, in the phantasm's arcane violet, floating.
// - Its hits on the owner's units go to the owner as its caster's do (CombatSync: any attack on a follower's copy of the
//   owner's unit is the follower's); the owner's units attacking its stand-in are played out in the caster's game,
//   against the real summon (SummonAttackedPacket).
public sealed class SummonSync
{
    private const int Interval = 6;
    private const long ResendMs = 1000, StaleMs = 3000;
    private static readonly int Arcane = Draw.Rgb(170, 130, 255);

    private readonly Session _session;
    private readonly PlayerManager _players;
    private readonly Func<bool> _inSharedWorld;
    // Others': each caster's summons where they are (their place, the list, when it came), and our stand-ins for them.
    private readonly Dictionary<int, (string Place, SummonState[] Summons, long At)> _remote = new();
    private readonly Dictionary<(int Slot, long Id), Instance> _proxies = new();
    // Ours: what we last sent, when.
    private string _sent = "";
    private long _sentAt;
    private int _frame;

    public SummonSync(Session session, PlayerManager players, Func<bool> inSharedWorld)
    {
        _session = session;
        _players = players;
        _inSharedWorld = inSharedWorld;
        session.On<SummonsPacket>(Receive);
        session.PlayerLeft += player => _remote.Remove(player.Slot);
        players.SummonStep = Step;
        players.SummonDraw = DrawProxy;
    }

    public void Clear()
    {
        foreach (Instance proxy in _proxies.Values)
            if (proxy.Exists)
                Units.Remove(proxy);
        _proxies.Clear();
        _remote.Clear();
        _sent = "";
    }

    private bool Ready => _session.Connected && _inSharedWorld() && Gm.InGame;

    /// <summary>Whether a unit is a summon a player owns (its owner is a player's character - ours, in our game).</summary>
    public static bool IsPlayers(Instance unit)
        => unit.Exists && Gm.ObjectIsAncestor(unit.Get("object_index").AsInt, (int)GameObjectId.o_summoned_entity)
            && Instance.Of(unit.Get("owner")) is { IsNone: false } owner && owner.Exists
            && owner.Get("object_index").AsInt == (int)GameObjectId.o_player;

    /// <summary>Whether a unit is one of our player's summons.</summary>
    public static bool IsOurs(Instance unit)
        => IsPlayers(unit) && OurPlayer.Instance is { IsNone: false } player && Instance.Of(unit.Get("owner")).Persist().Equals(player.Persist());

    public void Tick()
    {
        if (!Ready)
        {
            if (!_session.Connected || !Gm.InGame)
                Clear();
            return;
        }
        if (++_frame % Interval != 0)
            return;
        SendOurs();
        PlaceProxies();
    }

    // Ours to everyone, as they change (and every second, for anyone who's come).
    private void SendOurs()
    {
        string place = OurPlayer.Place ?? "";
        var ours = Instances.All(GameObjectId.o_summoned_entity).Where(IsOurs).Select(Describe).ToArray();
        string json = JsonSerializer.Serialize(ours);
        long now = Environment.TickCount64;
        if (json == _sent && now - _sentAt < ResendMs)
            return;
        if (ours.Length == 0 && _sent == "[]" && now - _sentAt < ResendMs * 5)
            return;
        _sent = json;
        _sentAt = now;
        _session.Send(new SummonsPacket(place, JoinCompression.Compress(json)));
    }

    private static SummonState Describe(Instance summon)
    {
        int sprite = summon.Get("sprite_index").AsInt;
        string name = sprite >= 0 ? Draw.SpriteName(sprite) : "";
        // (A sprite made at run time - a phantasm's copy of its caster - isn't one of the game's: drawn from the caster's
        // look elsewhere.)
        bool asset = name.Length > 0 && Gm.AssetGetIndex(name) == sprite && !summon.Get("is_sprite_duplicated").AsBool;
        return new SummonState((long)summon.Get("id").AsReal, Gm.ObjectGetName(summon.Get("object_index").AsInt),
            summon.Get("x").AsReal, summon.Get("y").AsReal, Number(summon, "HP"), Number(summon, "max_hp"),
            summon.Get("image_xscale").AsReal < 0, asset ? name : "", Number(summon, "image_index"),
            summon.Get("image_alpha") is { Kind: GmKind.Real } a ? a.AsReal : 1, summon.Get("name").Kind == GmKind.String ? summon.Get("name").AsString : "");
    }

    private void Receive(RemotePlayer from, SummonsPacket packet)
    {
        var summons = JsonSerializer.Deserialize<SummonState[]>(JoinCompression.Decompress(packet.Data)) ?? Array.Empty<SummonState>();
        _remote[from.Slot] = (packet.Place, summons, Environment.TickCount64);
    }

    // Our stand-ins: one for each summon of anyone here, gone with it.
    private void PlaceProxies()
    {
        string place = OurPlayer.Place ?? "";
        long now = Environment.TickCount64;
        var wanted = new HashSet<(int, long)>();
        foreach (var (slot, (theirPlace, summons, at)) in _remote)
        {
            if (theirPlace != place || now - at > StaleMs || !_session.Players.Any(p => p.Slot == slot))
                continue;
            foreach (var summon in summons)
            {
                var key = (slot, summon.Id);
                wanted.Add(key);
                if (_proxies.TryGetValue(key, out var proxy) && proxy.Exists)
                    continue;
                Cell cell = Cell.At(summon.X, summon.Y);
                Instance made = _players.CreateProxy(cell);
                if (made.IsNone)
                    continue;
                made["mp_slot"] = slot;
                made["mp_summon"] = summon.Id;
                _proxies[key] = made;
            }
        }
        foreach (var (key, proxy) in _proxies.ToList())
        {
            if (wanted.Contains(key) && proxy.Exists)
                continue;
            if (proxy.Exists)
                Units.Remove(proxy);
            _proxies.Remove(key);
        }
    }

    private SummonState? StateOf(Instance proxy)
    {
        int slot = proxy.Get("mp_slot").AsInt;
        long id = (long)proxy.Get("mp_summon").AsReal;
        return _remote.TryGetValue(slot, out var remote) ? remote.Summons.FirstOrDefault(s => s.Id == id) : null;
    }

    // A stand-in's step: in its summon's cell, passive, out of our turns, its health and name as theirs.
    private void Step(Instance proxy)
    {
        if (StateOf(proxy) is not { } summon)
            return;
        Cell cell = Cell.At(summon.X, summon.Y);
        if (Units.CellOf(proxy) != cell && Units.CanTake(proxy, cell))
            Units.Move(proxy, cell, snap: true);
        Instance me = proxy.Persist();
        Units.RemoveFromTurns(listed => listed.Equals(me));
        proxy["ai_is_on"] = false;
        proxy["is_neutral"] = true;
        proxy["is_ignored_by_enemies"] = false;
        proxy["roomEntityIsSavable"] = false;
        proxy["can_drop_loot"] = false;
        proxy["is_full_destroy"] = false;
        proxy["name"] = summon.Name.Length > 0 ? summon.Name : "Summon";
        proxy["desc"] = "Another player's summon.";
        proxy["max_hp"] = Math.Max(1, summon.MaxHealth);
        proxy["HP"] = Math.Max(1, summon.Health);
        proxy["depth"] = -summon.Y;
    }

    // A stand-in drawn as its summon looks: its own sprite, or its caster's look in arcane violet, floating.
    private void DrawProxy(Instance proxy)
    {
        if (StateOf(proxy) is not { } summon)
            return;
        double flip = summon.Flip ? -1 : 1;
        if (summon.Sprite.Length > 0)
        {
            int sprite = Gm.AssetGetIndex(summon.Sprite);
            if (sprite >= 0)
                Draw.SpriteExt(sprite, summon.Frame, summon.X, summon.Y, flip, 1, 0, Draw.White, summon.Alpha);
            return;
        }
        int slot = proxy.Get("mp_slot").AsInt;
        if (_players.LookOf(slot) is not { } look || look < 0)
            return;
        double bob = Math.Sin(Environment.TickCount64 / 400.0) * 1.5 - 3;
        Draw.SpriteExt(look, Environment.TickCount64 / 120 % Math.Max(1, Game.CallBuiltin("sprite_get_number", look).AsInt),
            summon.X, summon.Y + bob, flip, 1, 0, Arcane, 0.7);
    }

    private static double Number(Instance unit, string name) => unit.Get(name) is { Kind: GmKind.Real } v ? v.AsReal : 0;
}
