using System;
using StoneForge;
using StoneshardMP.Effects;
using StoneshardMP.Ghosts;
using StoneshardMP.Net;
using StoneshardMP.UI;

namespace StoneshardMP;

// StoneshardMP on StoneForge: co-op over LiteNetLib. A trusted mod (mod.json "trusted": true) - it needs the
// network, and brings LiteNetLib (lib\). Being ported feature by feature from the GML version (MSL), milestone by
// milestone; so far: the session - hosting, joining, players coming and going (Net\Session) - and the other
// players' ghosts (Ghosts\GhostManager).
public sealed class MultiplayerMod : IStoneMod, ITickable
{
    private ModContext _context = null!;
    private Session _session = null!;
    private GhostManager _ghosts = null!;
    private EffectManager _effects = null!;
    private AreaUnits _areaUnits = null!;
    private MultiplayerMenu _menu = null!;
    private WorldClock _clock = null!;
    private DebugDump _dump = null!;
    private JoinManager _join = null!;

    public void Load(ModContext context)
    {
        _context = context;
        var settings = new MpSettings(context.Settings);
        _session = new Session(context.Manifest.Version, context.Log);
        _ghosts = new GhostManager(context, _session, () => settings.ShowNames.Value);
        _effects = new EffectManager(context, _session);
        _areaUnits = new AreaUnits(_session);
        // One completed action is one world turn for everyone: a client's moves turn the host's world (its units,
        // streamed back by AreaUnits), the host's own turn the clients' clocks.
        _clock = new WorldClock(_session);
        _dump = new DebugDump(context, _session);
        // The main menu's Multiplayer screens (host, join - its dialog -, the game's Play buttons meanwhile) and the
        // Players & Settings window.
        var window = context.UI.MainMenu.Add(new MultiplayerWindow(_session, settings, SteamName));
        var join = context.UI.MainMenu.Add(new JoinDialog(_session, settings, () => window.PlayerName));
        // The host keeps everyone's save: a client joins the host's world, or makes a character for it.
        _join = new JoinManager(context, _session, () => window.PlayerName);
        _menu = new MultiplayerMenu(context, _session, settings, () => window.PlayerName, join, window, () => _join.Status);
        context.Log($"StoneshardMP {context.Manifest.Version} (protocol {Session.Protocol}), LiteNetLib networking");
    }

    // Switched off (or reloaded): out of any session, the port freed.
    public void Unload()
    {
        _session.Stop("");
        _ghosts.Clear();
        _effects.Clear();
        _areaUnits.Clear();
        _clock.Clear();
        _join.Clear();
    }

    public void Tick(double deltaTime)
    {
        _session.Poll();
        if (Game.Running)
        {
            _menu.Tick();
            _join.Tick();
            _ghosts.Tick();
            _effects.Tick();
            _areaUnits.Tick();
            _clock.Tick();
            _dump.Tick();
        }
    }

    // The player's Steam name ("Player" without Steam).
    private static string SteamName()
    {
        try
        {
            string name = Game.CallBuiltinUnrestricted("steam_get_persona_name", default, default).AsString;
            return name.Length > 0 ? name : "Player";
        }
        catch (Exception)
        {
            return "Player";
        }
    }
}
