using System;
using StoneForge;
using StoneshardMP.Net;

namespace StoneshardMP.Features.Menu;

// Join Game's dialog: the host's address (kept as the Join address setting), Join and Cancel - in the game's confirm
// panel (s_skill_confirm_panel: its message area 26,26 256x55, its two buttons' places at 53,90 and 155,90).
public sealed class JoinDialog : UIWindow
{
    private readonly Session _session;
    private readonly MpSettings _settings;
    private readonly Func<string> _playerName;
    private UITextBox _address = null!;

    public JoinDialog(Session session, MpSettings settings, Func<string> playerName)
    {
        _session = session;
        _settings = settings;
        _playerName = playerName;
        FrameSprite = (int)Sprite.s_skill_confirm_panel;
        ContentInsets = new UIInsets(26, 26, 26, 7);
        CloseButton.Visible = false;
    }

    protected override void OnOpen()
    {
        Content.Add(new UILabel($"Join a game - the host's address (port {_settings.Port.Value:0}):", 0, 6) { Width = Content.Width, Align = Draw.AlignCenter });
        _address = Content.Add(new UITextBox((Content.Width - 180) / 2, 24, 180, _settings.JoinAddress.Value, "127.0.0.1") { MaxLength = 64 });
        _address.Submitted += _ => Join();
        _address.Focus();
        var buttons = Content.Add(new UIButtonRow(0, 64, Content.Width) { Positions = new double[] { 27, 129 } });
        buttons.Add("Join", Join);
        buttons.Add("Cancel", Close);
    }

    private void Join()
    {
        string address = _address.Text.Trim();
        if (address.Length == 0)
            return;
        _settings.JoinAddress.Value = address;
        Close();
        _session.Join(address, (int)_settings.Port.Value, _playerName());
    }
}
