using LiteNetLib;
using StoneshardMP.Net.Packets;
using LiteNetLib.Utils;
using StoneForge;
using StoneshardMP.Net;

namespace StoneshardMP.Features.Areas;

public sealed class AreaUnits
{
    private readonly Session _session;
    private int _frame;
    public AreaUnits(Session session)
    {
        _session = session;
        session.On<AreaUnitsPacket>(Receive);
    }
    public void Clear() => _frame = 0;
    public void Tick()
    {
        if (!_session.Connected || !Gm.InGame || _session.Mode != Session.SessionMode.Host || ++_frame % 6 != 0)
            return;
        PlayerState? mine = PlayerState.Parse(Gml.MpPlayerState());
        if (mine == null)
            return;
        string snapshot = Gml.MpAreaUnitSnapshot();
        foreach (RemotePlayer player in _session.Players)
            if (player.State?.Place == mine.Place)
                // A complete room roster is often larger than LiteNetLib's 1,020-byte sequenced-packet cap.
                // ReliableOrdered fragments it safely and also ensures a follower never applies a newer roster
                // before an older snapshot that created one of its bindings.
                _session.Send(new AreaUnitsPacket(mine.Place, snapshot), player.Slot, DeliveryMethod.ReliableOrdered);
    }
    private void Receive(RemotePlayer sender, AreaUnitsPacket packet)
    {
        if (_session.Mode != Session.SessionMode.Client || sender.Slot != 0)
            return;
        string place = packet.Place;
        string snapshot = packet.Snapshot;
        PlayerState? mine = PlayerState.Parse(Gml.MpPlayerState());
        if (mine?.Place == place)
            Gml.MpAreaUnitApply(snapshot);
    }
}
