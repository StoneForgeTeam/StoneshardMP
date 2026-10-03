using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LiteNetLib;
using StoneshardMP.Net.Packets;
using StoneForge;
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
        public int[] Sprites = { -4, -4, -4, -4, -4 };
        public int BuiltVersion = -1;
        public float VisX, VisY;
        public bool Placed;
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
            mine = PlayerState.Parse(Gml.MpPlayerState());
            if (mine != null && _frame % 2 == 0)
                _session.Send(new StatePacket(mine), delivery: DeliveryMethod.Sequenced);
            if (_frame % 30 == 0)
            {
                string look = Gml.MpPlayerLook();
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
            string profile = Gml.MpPlayerProfile();
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
                Game.CallBuiltin("instance_destroy", view.Unit);
                view.Unit = default;
            }
        }
    }

    // A player's Step: follows their state - smoothly for small moves, at once for jumps.
    internal void Step(Instance self)
    {
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
        // while Draw deliberately uses the remote player's smoother visual position instead.
        Gml.MpPlayerUnitMove(self, state.CellX, state.CellY);
        self["name"] = view.Player.Name;
        self["type"] = "Player";
        self["desc"] = "Another player.";
        self["ai_is_on"] = false;
        self["is_neutral"] = true;
        self["is_ignored_by_enemies"] = true;
        self["roomEntityIsSavable"] = false;
        self["can_drop_loot"] = false;
        self["is_full_destroy"] = false;
        self["max_hp"] = ProxyVitalMaximum;
        self["HP"] = ProxyVital(state.Health, state.MaxHealth);
        self["max_mp"] = ProxyVitalMaximum;
        self["MP"] = ProxyVital(state.Energy, state.MaxEnergy);
        ApplyProfile(self, view.Player.Profile);
        self["depth"] = state.Depth;
    }

    // A player's Draw Begin: its sprites built again when their look has changed.
    internal void Build(Instance self)
    {
        if (ViewOf(self) is not { } view || view.BuiltVersion == view.Player.LookVersion || view.Player.Look.Length == 0)
            return;
        view.BuiltVersion = view.Player.LookVersion;
        var s = view.Sprites;
        string built = Gml.MpPlayerBuild(view.Player.Look, s[0], s[1], s[2], s[3], s[4]);
        var parts = built.Split(',');
        if (parts.Length != 5)
        {
            _context.Log($"Couldn't build {view.Player.Name}'s look");
            return;
        }
        for (int i = 0; i < 5; i++)
            s[i] = int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int sprite) ? sprite : -4;
    }

    // A player's Draw: their shadow, their look, a name tag.
    internal void Draw(Instance self)
    {
        if (ViewOf(self) is not { Player.State: { } state } view || !state.Visible)
            return;
        int sprite = view.Sprites[Math.Clamp((int)state.Row, 0, 4)];
        if (!SpriteExists(sprite))
            sprite = view.Sprites[0];
        if (!SpriteExists(sprite))
            return;
        if (state.ShadowAlpha > 0 && SpriteExists(state.ShadowSprite))
            Game.CallBuiltin("draw_sprite_ext", state.ShadowSprite, 0, view.VisX + (state.ShadowX - state.X), view.VisY + (state.ShadowY - state.Y),
                state.ShadowScaleX, state.ShadowScaleY, 0, Black, state.ShadowAlpha);
        Game.CallBuiltin("draw_sprite_ext", sprite, state.Frame, view.VisX, view.VisY, state.ScaleX, state.ScaleY, state.Angle, White, state.Alpha);
        if (!_showNames())
            return;
        int halign = Game.CallBuiltin("draw_get_halign").AsInt, valign = Game.CallBuiltin("draw_get_valign").AsInt;
        int colour = Game.CallBuiltin("draw_get_colour").AsInt;
        Game.CallBuiltin("draw_set_halign", 1);
        Game.CallBuiltin("draw_set_valign", 2);
        Game.CallBuiltin("draw_set_colour", Black);
        Game.CallBuiltin("draw_text", view.VisX + 1, view.VisY - 35, view.Player.Name);
        Game.CallBuiltin("draw_set_colour", Aqua);
        Game.CallBuiltin("draw_text", view.VisX, view.VisY - 36, view.Player.Name);
        Game.CallBuiltin("draw_set_colour", colour);
        Game.CallBuiltin("draw_set_halign", halign);
        Game.CallBuiltin("draw_set_valign", valign);
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
            Game.CallBuiltin("instance_destroy", view.Unit);
        foreach (int sprite in view.Sprites)
            if (SpriteExists(sprite))
                Game.CallBuiltin("sprite_delete", sprite);
    }

    private View? ViewOf(Instance self)
    {
        int slot = self["mp_slot"].AsInt;
        return self["mp_slot"].IsUndefined ? null : _views.GetValueOrDefault(slot);
    }

    private static bool SpriteExists(int sprite) => sprite >= 0 && Game.CallBuiltin("sprite_exists", sprite).AsBool;

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
