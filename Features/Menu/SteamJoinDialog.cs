using System;
using System.Linq;
using StoneForge;
using StoneshardMP.Net;
using StoneshardMP.Net.Steam;

namespace StoneshardMP.Features.Menu;

// Join a Friend's dialog: the Steam friends hosting StoneshardMP (in a lobby of Stoneshard's - SteamLink), a Join for
// each, Refresh and Cancel. Joining goes through Steam: no address, no ports.
public sealed class SteamJoinDialog : UIWindow
{
    private const int Shown = 8;
    private const double Row = 30;

    private readonly Session _session;
    private readonly Func<string> _playerName;

    public SteamJoinDialog(Session session, Func<string> playerName) : base("Join a Friend")
    {
        _session = session;
        _playerName = playerName;
        FrameWidth = 320;
        FrameHeight = 120 + Shown * Row;
    }

    protected override void OnOpen()
    {
        double y = 4;
        if (!SteamLink.Available)
        {
            Content.Add(new UILabel("Steam isn't running.", 0, y) { Width = Content.Width, Align = Draw.AlignCenter });
            y += Row;
        }
        else
        {
            var friends = SteamLink.Friends().OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).Take(Shown).ToArray();
            if (friends.Length == 0)
            {
                Content.Add(new UILabel("No friends are hosting right now.", 0, y) { Width = Content.Width, Align = Draw.AlignCenter, Colour = Draw.Muted });
                y += Row;
            }
            foreach (FriendLobby friend in friends)
            {
                Content.Add(new UILabel(friend.Name, 8, y + 6) { Width = Content.Width - 120 });
                Content.Add(new UIButton("Join", Content.Width - 104, y, 100, 26, () => Join(friend)));
                y += Row;
            }
        }
        var buttons = Content.Add(new UIButtonRow(0, Math.Max(y + 8, Content.Height - 34), Content.Width) { Positions = new double[] { 27, 129 } });
        buttons.Add("Refresh", () => { Close(); Open(); });
        buttons.Add("Cancel", Close);
    }

    private void Join(FriendLobby friend)
    {
        Close();
        _session.JoinSteam(friend, _playerName());
    }
}
