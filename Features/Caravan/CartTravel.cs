using System;
using StoneForge;
using StoneshardMP.Features.Players;
using StoneshardMP.Net;

// Verren's cart taking its rider to a settlement (scr_move_to_settlement: the brewery to Brynn, on the caravan quest).
[assembly: HookScript(nameof(Scripts.scr_move_to_settlement))]

namespace StoneshardMP.Features.Caravan;

// Verren's cart rides for everyone there: the game's fast travel by it (scr_move_to_settlement, free - not a coachman's
// paid ride, which stays a player's own) moved only the player who took it, and the others were left behind - at the
// brewery, with the story moved on to Brynn without them. Now the players in the same place ride along to the same
// settlement, by the game's own travel (its time, the cart left at the other end, the quest step - all as their own).
public sealed class CartTravel
{
    private readonly ModContext _context;
    private readonly Session _session;
    private readonly Func<bool> _inSharedWorld;
    // (Riding along as another's said: not said again.)
    private bool _riding;

    public CartTravel(ModContext context, Session session, Func<bool> inSharedWorld)
    {
        _context = context;
        _session = session;
        _inSharedWorld = inSharedWorld;
        session.On<CartTravelPacket>(Receive);
        Scripts.scr_move_to_settlement.Before(context, call =>
        {
            // (Free: no price given - o_confirm_panel, the game's default. A coachman's ride passes its price.)
            bool free = call.Args.Length < 2 || call.Args[1].IsUndefined
                || call.Args[1].Kind == GmKind.Real && call.Args[1].AsInt == (int)GameObjectId.o_confirm_panel;
            if (!_riding && free && _session.Connected && _inSharedWorld() && call.Args.Length > 0
                && call.Args[0].Kind == GmKind.String && OurPlayer.Place is { } here)
                _session.Send(new CartTravelPacket(here, call.Args[0].AsString));
            return false;
        });
    }

    private void Receive(RemotePlayer from, CartTravelPacket packet)
    {
        if (!_inSharedWorld() || !Gm.InGame || Rooms.IsChanging || OurPlayer.Place != packet.From)
            return;
        _context.Log($"{from.Name} rode Verren's cart to {packet.Settlement}: riding along");
        _riding = true;
        try { Game.CallScript("scr_move_to_settlement", default, packet.Settlement); }
        finally { _riding = false; }
    }
}
