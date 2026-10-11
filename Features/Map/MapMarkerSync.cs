using System;
using System.Linq;
using StoneForge;
using StoneshardMP.Net;

namespace StoneshardMP.Features.Map;

// Markers on the world map, shared (legacy: scr_mp_marks_step, scr_mp_marks_apply): everyone in the world has one set.
// The player placing one, or taking one off (StoneForge's MapMarkers.OnPlaced / OnRemoved), sends them all
// (MapMarkersPacket); the others' become those, an open map's on the spot. A client's world came from the host's save,
// markers and all. Two players changing them at once: the last to arrive wins.
public sealed class MapMarkerSync
{
    private readonly ModContext _context;
    private readonly Session _session;
    private readonly Func<bool> _inSharedWorld;
    // (A change this frame, sent once at its end - placing one over another is both.)
    private bool _changed;

    public MapMarkerSync(ModContext context, Session session, Func<bool> inSharedWorld)
    {
        _context = context;
        _session = session;
        _inSharedWorld = inSharedWorld;
        session.On<MapMarkersPacket>(Receive);
        MapMarkers.OnPlaced(context, _ => _changed |= Sharing);
        MapMarkers.OnRemoved(context, _ => _changed |= Sharing);
    }

    public void Clear() => _changed = false;

    public void Tick()
    {
        if (!_changed)
            return;
        _changed = false;
        if (!Sharing)
            return;
        var markers = MapMarkers.All();
        _session.Send(new MapMarkersPacket(markers.ToArray()));
        _context.Log($"Map markers changed: {markers.Count} sent");
    }

    private bool Sharing => _session.Connected && _inSharedWorld() && Gm.InGame && WorldMap.Available;

    private void Receive(RemotePlayer sender, MapMarkersPacket packet)
    {
        if (!Sharing)
            return;
        // (Set, as a mod does: not the player's placing - nothing goes back.)
        MapMarkers.Set(packet.Markers);
        Gm.AudioPlaySound(Sound.snd_checkbox_on, 4);
        _context.Log($"Map markers from {sender.Name}: {packet.Markers.Length}");
    }
}
