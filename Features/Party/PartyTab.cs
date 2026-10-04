using StoneForge;

namespace StoneshardMP.Features.Party;

// The frames' hide / show tab at the screen edge, beside the first frame: the game's tooltip frame with a gold arrow -
// right (slide them away) while they're shown, left (bring them back) while hidden.
internal sealed class PartyTab : UIElement
{
    private readonly PartyFrames _frames;

    public PartyTab(PartyFrames frames)
    {
        _frames = frames;
        Width = PartyFrames.TabWidth;
        Height = PartyFrames.TabHeight;
    }

    protected override void OnUpdate(double deltaTime) => Tooltip = _frames.Hidden ? "Show party frames" : "Hide party frames";

    protected override void OnDraw(double x, double y)
    {
        Draw.Frame(x, y, Width, Height);
        if (IsHovered)
            Draw.Rectangle(x + 2, y + 2, x + Width - 3, y + Height - 3, Draw.White, 0.12);
        double cx = x + Width / 2, cy = y + Height / 2;
        Game.CallBuiltin("draw_set_colour", PartyFrame.Gold);
        if (_frames.Hidden)
            Game.CallBuiltin("draw_triangle", cx - 3, cy, cx + 2, cy - 4, cx + 2, cy + 4, false);
        else
            Game.CallBuiltin("draw_triangle", cx + 3, cy, cx - 2, cy - 4, cx - 2, cy + 4, false);
        Game.CallBuiltin("draw_set_colour", Draw.White);
    }

    protected override void OnClick() => _frames.Toggle();
}
