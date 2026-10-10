using System;
using StoneForge;

namespace StoneshardMP.Features.Death;

// The death screen's choices while playing together (in place of the game's Load, log and Exit, under its "You died").
// A client: Respawn - back as you were at the host's last save, where they saved; what you picked up since lies where
// you died - and Disconnect. The host: Respawn - back as you were at your last save, the world as it is now - Load (the
// game's save menu: everyone comes along) and Disconnect. They come in as the game's own would, a moment after the screen.
public sealed class DeathScreen : UIElement
{
    public enum Choice { Respawn, Load, Disconnect }

    private const double ButtonWidth = 160, ButtonHeight = 26, Gap = 6;
    // (The game's own buttons fade in about a second in.)
    private const double AppearAfter = 1.1;

    private readonly Func<string> _host;
    private readonly UIButton _respawn, _load, _disconnect;
    private double _shownFor;
    private bool _isHost;

    public DeathScreen(Func<string> host, Action<Choice> choose)
    {
        _host = host;
        Anchor = UIAnchor.Center;
        Width = ButtonWidth;
        Height = ButtonHeight * 3 + Gap * 2;
        // (A little below the middle: under the game's "You died".)
        Y = 40;
        Visible = false;
        _respawn = Add(new UIButton("Respawn", 0, 0, ButtonWidth, ButtonHeight, () => choose(Choice.Respawn)));
        _load = Add(new UIButton("Load", 0, ButtonHeight + Gap, ButtonWidth, ButtonHeight, () => choose(Choice.Load))
        {
            Tooltip = "Load a save. Everyone in your world loads it with you, as that save has them.",
        });
        _disconnect = Add(new UIButton("Disconnect", 0, ButtonHeight + Gap, ButtonWidth, ButtonHeight, () => choose(Choice.Disconnect)));
    }

    /// <summary>Shown, for a client or the host; canRespawn: whether there's a save to come back to (the host may have
    /// none - a world never saved).</summary>
    public void Show(bool isHost, bool canRespawn)
    {
        _isHost = isHost;
        _shownFor = 0;
        _respawn.Enabled = canRespawn;
        _respawn.Tooltip = !canRespawn ? "Nothing to come back to: this world hasn't been saved yet."
            : isHost ? "Wake where you last saved, as you were then - the world goes on as it is, and the others stay as "
                + "they are. Everything you picked up since - carried or worn - has dropped where you died."
            : $"Wake where {_host()} last saved, as you were then. Everything you picked up since - carried or worn - has "
                + "dropped where you died.";
        _disconnect.Tooltip = isHost
            ? "Stop hosting, without saving: the world stays as your last save left it - for everyone."
            : "Leave the game. You'll come back as you were at the last save when you next join.";
        _disconnect.Y = (isHost ? 2 : 1) * (ButtonHeight + Gap);
        Visible = true;
        _respawn.Visible = _load.Visible = _disconnect.Visible = false;
    }

    public void Hide() => Visible = false;

    protected override void OnUpdate(double deltaTime)
    {
        // (Gone with the death screen - a room change, a load.)
        if (!Gm.InstanceExists(GameObjectId.o_dead_panel))
        {
            Visible = false;
            return;
        }
        _shownFor += deltaTime;
        bool shown = _shownFor >= AppearAfter;
        _respawn.Visible = _disconnect.Visible = shown;
        _load.Visible = shown && _isHost;
    }
}
