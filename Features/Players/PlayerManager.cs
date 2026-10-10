using System;
using System.Collections.Generic;
using System.Linq;
using LiteNetLib;
using StoneshardMP.Net.Packets;
using StoneForge;
using StoneshardMP.Features.Areas;
using StoneshardMP.Net;

namespace StoneshardMP.Features.Players;

// The other players on our screen: our state sent every other frame and our look when it changes (and to newcomers); each
// player in the same place as us gets a Player object, made again after a room change, gone when they leave or go
// elsewhere. Ctrl+Shift+G: the mirror test - our own player copied two cells to the right, for trying it with one game.
public sealed class PlayerManager
{
    // A player's look and smoothing, kept by player (a room change makes the instance again, not the look).
    private sealed class View
    {
        public required RemotePlayer Player;
        public Instance Unit;
        // (Built from their look: CharacterLook.)
        public CharacterSprites? Sprites;
        public int BuiltVersion = -1;
        public float VisX, VisY;
        public bool Placed;
        // The light on their stand-in (an on-unit light effect), and which light it is (PlayerLight).
        public Instance LightEffect;
        public PlayerLight Light;
    }

    // (A player's object goes when nothing's been heard of where they are for this long.)
    private const long StaleMs = 3000;
    // The player object's local hit pool stays huge because receiving a remote player's state is not yet combat authority.
    // Its displayed percentage is still exact: the inspection UI works from current / maximum.
    private const double ProxyVitalMaximum = 1000000000;
    private const int MirrorSlot = 99;
    private const int White = 0xFFFFFF, Black = 0, Aqua = 0xFFFF00;

    private readonly ModContext _context;
    private readonly Session _session;
    private readonly Func<bool> _showNames;
    private readonly Func<bool> _inSharedWorld;
    private readonly Player _object;
    // (The game's speech cloud sprite, looked up once.)
    private int _cloud = -1;
    private readonly Dictionary<int, View> _views = new();
    private RemotePlayer? _mirror;
    private int _frame;
    private string _sentLook = "";
    private string _sentProfile = "";
    private bool _wasInGame;

    public PlayerManager(ModContext context, Session session, Func<bool> showNames, Func<bool> inSharedWorld)
    {
        _context = context;
        _session = session;
        _showNames = showNames;
        _inSharedWorld = inSharedWorld;
        _object = new Player(this);
        context.Objects.Add(_object);
        session.On<StatePacket>( (from, r) =>
        {
            from.State = r.State;
            from.StateAt = Environment.TickCount64;
        });
        session.On<LookPacket>( (from, r) =>
        {
            from.Look = r.Json;
            from.LookVersion++;
        });
        session.On<ProfilePacket>( (from, r) => from.Profile = PlayerProfile.Parse(r.Values));
        // (A newcomer gets our look at once; a player who's gone takes their object.)
        session.PlayerJoined += _ => { _sentLook = ""; _sentProfile = ""; };
        session.PlayerLeft += player => Forget(player.Slot);
    }

    /// <summary>The object other players' units are (o_stoneshardmp__player; -1 before the game has it).</summary>
    public int ObjectIndex => _object.Index;

    /// <summary>What a player (by slot) is talking or trading with here - none if it's not one we know - or null if they
    /// aren't (TalkSync): a speech cloud over them, and over it.</summary>
    public Func<int, Instance?>? TalkingTo { get; set; }

    /// <summary>Whether we run the place we're in (AreaOwnership): our units are the real ones, ours to move off a cell
    /// another player's on.</summary>
    public Func<bool> OwnsArea { get; set; } = () => false;

    /// <summary>A summon's stand-in - the same object, marked mp_summon (SummonSync) - stepped and drawn by SummonSync.</summary>
    public Action<Instance>? SummonStep { get; set; }
    public Action<Instance>? SummonDraw { get; set; }

    /// <summary>A new stand-in in a cell, for a summon (SummonSync marks it); none before the game has our object.</summary>
    public Instance CreateProxy(Cell cell)
        => _object.Index < 0 ? default : _object.Create(cell.X * 26 + 13, cell.Y * 26 + 13, -(cell.Y * 26 + 13)).Persist();

    /// <summary>A player's character sprite as their stand-in shows it now (-1: none) - what an Astral Phantasm of theirs,
    /// a copy of it, is drawn from.</summary>
    public int? LookOf(int slot)
    {
        if (!_views.TryGetValue(slot, out var view) || view.Sprites is not { IsDisposed: false } sprites || sprites.All.Count == 0)
            return null;
        int row = view.Player.State is { } state ? Math.Clamp((int)state.Row, 0, sprites.All.Count - 1) : 0;
        return sprites.All[row];
    }

    private static bool IsSummon(Instance self) => !self["mp_summon"].IsUndefined;

    // Every frame (the mod's Tick).
    public void Tick()
    {
        _frame++;
        if (Keyboard.Down(Keyboard.Control) && Keyboard.Down(Keyboard.Shift) && Keyboard.Pressed('G'))
            ToggleMirror();
        // (A client not yet in the host's world - making its character - is out of the game to everyone.)
        bool inGame = Gm.InGame && _inSharedWorld();
        PlayerState? mine = null;
        if (inGame)
        {
            mine = OurPlayer.State();
            // (Just in - joined, or a world (re)loaded with other gear: our look goes first, every time, so the others
            // don't draw us where we are now in what we wore before.)
            bool entering = !_wasInGame;
            if (entering)
                _sentLook = "";
            if (entering || _frame % 30 == 0)
            {
                string look = OurPlayer.Look();
                if (look.Length > 0 && look != _sentLook)
                {
                    _sentLook = look;
                    _session.Send(new LookPacket(look));
                    if (_mirror != null)
                    {
                        _mirror.Look = look;
                        _mirror.LookVersion++;
                    }
                }
            }
            if (mine != null && (entering || _frame % 2 == 0))
                _session.Send(new StatePacket(mine), delivery: DeliveryMethod.Sequenced);
            string profile = OurPlayer.Profile();
            if (profile.Length > 0 && (_frame % 30 == 0 || profile != _sentProfile))
            {
                _sentProfile = profile;
                _session.Send(new ProfilePacket(profile));
                if (_mirror != null)
                    _mirror.Profile = PlayerProfile.Parse(profile);
            }
            if (_mirror != null && mine != null)
            {
                _mirror.State = mine.Shifted(52);
                _mirror.StateAt = Environment.TickCount64;
            }
        }
        else if (_wasInGame || _frame % 60 == 0)
            // Out of the game: the others' copies of us go.
            _session.Send(new StatePacket(null), delivery: DeliveryMethod.Sequenced);
        _wasInGame = inGame;
        Place(mine);
    }

    // Each player in our place has a live Player object; nobody else has one.
    private void Place(PlayerState? mine)
    {
        var players = _session.Players.ToList();
        if (_mirror != null)
            players.Add(_mirror);
        long now = Environment.TickCount64;
        foreach (var player in players)
        {
            if (!_views.TryGetValue(player.Slot, out var view))
                _views[player.Slot] = view = new View { Player = player };
            var state = player.State;
            bool here = mine != null && state != null && state.Place == mine.Place && now - player.StateAt < StaleMs;
            bool exists = !view.Unit.IsNone && view.Unit.Exists;
            if (here && !exists && _object.Index >= 0)
            {
                // A dummy unit starts centred in the remote player's occupied cell. Its own x/y then remain the
                // unit position; VisX/VisY below stay independent, for the player's smooth drawn movement.
                view.Unit = _object.Create(state!.CellX * 26 + 13, state.CellY * 26 + 13, state.Depth).Persist();
                view.Unit["mp_slot"] = player.Slot;
                view.Placed = false;
            }
            else if (!here && exists)
            {
                // Taken out quietly (legacy: scr_mp_unit_remove): out of the grids and the turn list, the effects on it
                // with it, and no Destroy event. Left in the turn list, the units' turn - held in a round - reads a
                // unit that's gone, and the game crashes.
                Units.Remove(view.Unit);
                view.Unit = default;
            }
        }
    }

    // A player's Step: follows their state - smoothly for small moves, at once for jumps.
    internal void Step(Instance self)
    {
        if (IsSummon(self))
        {
            SummonStep?.Invoke(self);
            return;
        }
        if (ViewOf(self) is not { Player.State: { } state } view)
            return;
        if (!view.Placed || Math.Abs(view.VisX - state.X) + Math.Abs(view.VisY - state.Y) > 104)
        {
            view.VisX = state.X;
            view.VisY = state.Y;
            view.Placed = true;
        }
        else
        {
            view.VisX += (state.X - view.VisX) * 0.5f;
            view.VisY += (state.Y - view.VisY) * 0.5f;
        }
        // Keep the real unit in the occupancy grids too. That lets the game recognize a player as a character,
        // while Draw deliberately uses the remote player's smoother visual position instead. Only onto a free cell (or
        // its own): our player, or an area unit, may be there for a moment - taking it would overwrite that unit in the
        // position grid, and the game crashes on it. It catches up once the cell's free. Snapped there, never walked: a
        // walking unit clears the cell it leaves as it starts, whoever's come onto it since (it's drawn from VisX / VisY).
        if (Units.CanTake(self, state.Cell))
            Units.Move(self, state.Cell, snap: true);
        // Running the place, and one of our units is on the cell they're on: they were there first (their game moved
        // them, then told us; our turn moved the unit there meanwhile) - it steps aside, and our next roster tells them.
        else if (OwnsArea())
            MakeWay(state.Cell);
        // No turns: it has no AI, and its player's game runs them (o_enemy's Create put it in our player's list of units
        // to run each turn; between turns only - never while the turn walks the list).
        Instance me = self.Persist();
        Units.RemoveFromTurns(listed => listed.Equals(me));
        self["name"] = view.Player.Name;
        self["type"] = "Player";
        self["desc"] = "Another player.";
        self["ai_is_on"] = false;
        self["is_neutral"] = true;
        // (Enemies go for it as for the player - CombatSync: what they do to it goes to its player's game.)
        self["is_ignored_by_enemies"] = false;
        self["roomEntityIsSavable"] = false;
        self["can_drop_loot"] = false;
        self["is_full_destroy"] = false;
        self["max_hp"] = ProxyVitalMaximum;
        self["HP"] = ProxyVital(state.Health, state.MaxHealth);
        self["max_mp"] = ProxyVitalMaximum;
        self["MP"] = ProxyVital(state.Energy, state.MaxEnergy);
        ApplyProfile(self, view.Player.Profile);
        // The light they carry, lighting what's around them here as it does in their game.
        PlayerLight.Apply(self, ref view.LightEffect, ref view.Light, view.Player.Party?.Light ?? default);
        self["depth"] = state.Depth;
    }

    // A player's Draw Begin: its sprites built again when their look has changed.
    internal void Build(Instance self)
    {
        if (IsSummon(self))
            return;
        if (ViewOf(self) is not { } view || view.BuiltVersion == view.Player.LookVersion || view.Player.Look.Length == 0)
            return;
        view.BuiltVersion = view.Player.LookVersion;
        // (By the game's own compositor, as our player's are; the old ones go once the new ones are made.)
        if (CharacterLook.FromJson(view.Player.Look)?.Build() is not { } built)
        {
            _context.Log($"Couldn't build {view.Player.Name}'s look");
            return;
        }
        view.Sprites?.Dispose();
        view.Sprites = built;
    }

    // A player's Draw: their shadow, their look, a name tag.
    internal void Draw(Instance self)
    {
        if (IsSummon(self))
        {
            SummonDraw?.Invoke(self);
            return;
        }
        if (ViewOf(self) is not { Player.State: { } state, Sprites: { IsDisposed: false } sprites } view || !state.Visible)
            return;
        // (The row their game draws them from: o_player's pick of its five.)
        int sprite = sprites.All[Math.Clamp((int)state.Row, 0, sprites.All.Count - 1)];
        if (!StoneForge.Draw.SpriteExists(sprite))
            sprite = sprites.Normal;
        if (!StoneForge.Draw.SpriteExists(sprite))
            return;
        if (state.ShadowAlpha > 0 && StoneForge.Draw.SpriteExists(state.ShadowSprite))
            StoneForge.Draw.SpriteExt(state.ShadowSprite, 0, view.VisX + (state.ShadowX - state.X), view.VisY + (state.ShadowY - state.Y),
                state.ShadowScaleX, state.ShadowScaleY, 0, Black, state.ShadowAlpha);
        StoneForge.Draw.SpriteExt(sprite, state.Frame, view.VisX, view.VisY, state.ScaleX, state.ScaleY, state.Angle, White, state.Alpha);
        // Their name over them, in the world's font, with a shadow.
        if (_showNames())
            StoneForge.Draw.PlainText(view.VisX, view.VisY - 36, view.Player.Name, Aqua, StoneForge.Draw.AlignCenter, StoneForge.Draw.AlignBottom);
        // Talking or trading: the game's speech cloud bobbing over them (over their name), and over their NPC.
        if (TalkingTo?.Invoke(view.Player.Slot) is { } npc)
        {
            if (_cloud < 0)
                _cloud = Gm.AssetGetIndex("s_dialogue_cloud");
            int frame = (int)(Environment.TickCount64 / 140 % 6);
            double bob = Math.Round(Math.Sin(Environment.TickCount64 / 300.0) * 1.5);
            StoneForge.Draw.SpriteExt(_cloud, frame, view.VisX - 7, view.VisY - 50 + bob);
            if (npc.Exists)
                StoneForge.Draw.SpriteExt(_cloud, frame, npc.Get("x").AsReal - 7, npc.Get("bbox_top").AsReal - 2 + bob);
        }
    }

    // One of our area's units (not a player's stand-in, not our own player) off a cell: onto a free one beside it, nearest
    // first. (A big unit - more than one cell - stays: its cells move as one.)
    private void MakeWay(Cell cell)
    {
        Instance unit = Units.At(cell);
        if (unit.IsNone || !unit.Exists || unit.Get("is_poly_cell").AsBool)
            return;
        int obj = unit.Get("object_index").AsInt;
        if (obj == _object.Index || !Gm.ObjectIsAncestor(obj, (int)GameObjectId.o_enemy))
            return;
        foreach (Cell aside in cell.Neighbours)
        {
            if (aside.X < 0 || aside.Y < 0 || !Units.CanTake(unit, aside))
                continue;
            Units.Move(unit, aside, snap: true);
            _context.Log($"Moved a {Gm.ObjectGetName(obj)} off {cell}, another player's cell, to {aside}");
            return;
        }
    }

    // Everything gone (the mod switched off): the players and their sprites.
    public void Clear()
    {
        foreach (int slot in _views.Keys.ToList())
            Forget(slot);
        _mirror = null;
    }

    private void ToggleMirror()
    {
        if (_mirror == null)
        {
            _mirror = new RemotePlayer(MirrorSlot, "Mirror", _context.Manifest.Version);
            _sentLook = "";
            _context.Log("Mirror test on: a copy of you two cells to the right (Ctrl+Shift+G again: off)");
        }
        else
        {
            _mirror = null;
            Forget(MirrorSlot);
            _context.Log("Mirror test off");
        }
    }

    private void Forget(int slot)
    {
        if (!_views.Remove(slot, out var view))
            return;
        if (!Game.Running)
            return;
        if (!view.Unit.IsNone && view.Unit.Exists)
            Units.Remove(view.Unit);
        view.Sprites?.Dispose();
    }

    /// <summary>The object standing for a player in our game (none if they've none here now).</summary>
    public Instance UnitOf(int slot)
        => _views.TryGetValue(slot, out var view) && !view.Unit.IsNone && view.Unit.Exists ? view.Unit : default;

    private View? ViewOf(Instance self)
    {
        int slot = self["mp_slot"].AsInt;
        return self["mp_slot"].IsUndefined ? null : _views.GetValueOrDefault(slot);
    }

    private static double ProxyVital(float current, float maximum)
    {
        double fraction = maximum > 0 ? Math.Clamp(current / maximum, 0, 1) : 1;
        // An actual zero would make o_enemy run its death event locally. One against this maximum displays as 0%.
        return Math.Max(1, Math.Round(ProxyVitalMaximum * fraction));
    }

    private static void ApplyProfile(Instance unit, PlayerProfile? profile)
    {
        for (int i = 0; i < PlayerProfile.ResistanceNames.Length; i++)
            unit[PlayerProfile.ResistanceNames[i]] = profile?.Values[i] ?? 0;
    }
}
