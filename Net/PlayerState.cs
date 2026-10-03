using System.Globalization;

namespace StoneshardMP.Net;

// Where and how a player is drawn (MpPlayerState): the place they're in, what o_player draws with, its shadow and
// the cell they stand on. Sent as the State packet, unreliably every other frame (only the latest matters).
public sealed record PlayerState(
    string Place, float X, float Y, float ScaleX, float ScaleY, float Frame, byte Row, int Depth, bool Visible,
    float Angle, float Diss, float Alpha, int ShadowSprite, float ShadowX, float ShadowY, float ShadowScaleX,
    float ShadowScaleY, float ShadowAlpha, short CellX, short CellY, float Health, float MaxHealth, float Energy, float MaxEnergy)
{
    // From MpPlayerState's "|"-separated text (null: none - not in a game, or not as expected).
    public static PlayerState? Parse(string text)
    {
        var f = text.Split('|');
        if (f.Length != 24)
            return null;
        float F(int i) => float.TryParse(f[i], NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0;
        return new PlayerState(f[0], F(1), F(2), F(3), F(4), F(5), (byte)F(6), (int)F(7), F(8) != 0, F(9), F(10), F(11),
            (int)F(12), F(13), F(14), F(15), F(16), F(17), (short)F(18), (short)F(19), F(20), F(21), F(22), F(23));
    }

    // Moved sideways (the mirror test draws us two cells to the right).
    public PlayerState Shifted(float dx) => this with { X = X + dx, ShadowX = ShadowX + dx, CellX = (short)(CellX + (int)(dx / 26)) };
}
