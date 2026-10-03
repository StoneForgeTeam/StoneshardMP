namespace StoneshardMP.Net;

// Where and how a player is drawn (OurPlayer.State): the place they're in, what o_player draws with, its shadow and
// the cell they stand on. Sent as the State packet, unreliably every other frame (only the latest matters).
public sealed record PlayerState(
    string Place, float X, float Y, float ScaleX, float ScaleY, float Frame, byte Row, int Depth, bool Visible,
    float Angle, float Diss, float Alpha, int ShadowSprite, float ShadowX, float ShadowY, float ShadowScaleX,
    float ShadowScaleY, float ShadowAlpha, short CellX, short CellY, float Health, float MaxHealth, float Energy, float MaxEnergy)
{
    // Moved sideways (the mirror test draws us two cells to the right).
    public PlayerState Shifted(float dx) => this with { X = X + dx, ShadowX = ShadowX + dx, CellX = (short)(CellX + (int)(dx / 26)) };
}
