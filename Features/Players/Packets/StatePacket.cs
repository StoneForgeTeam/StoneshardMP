using StoneshardMP.Memory;
using StoneshardMP.Net;
using StoneshardMP.Net.Packets;

namespace StoneshardMP.Features.Players;

// Owns the complete player-state wire contract, including the empty-place sentinel.
public readonly record struct StatePacket(PlayerState? State) : IPacket
{
    public const byte PacketId = 4;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer)
    {
        if (State == null)
        {
            writer.Write("");
            return;
        }

        var state = State;
        writer.Write(state.Place);
        writer.Write(state.X);
        writer.Write(state.Y);
        writer.Write(state.ScaleX);
        writer.Write(state.ScaleY);
        writer.Write(state.Frame);
        writer.Write(state.Row);
        writer.Write(state.Depth);
        writer.Write((byte)(state.Visible ? 1 : 0));
        writer.Write(state.Angle);
        writer.Write(state.Diss);
        writer.Write(state.Alpha);

        writer.Write(state.ShadowSprite);
        writer.Write(state.ShadowX);
        writer.Write(state.ShadowY);
        writer.Write(state.ShadowScaleX);
        writer.Write(state.ShadowScaleY);
        writer.Write(state.ShadowAlpha);

        writer.Write(state.CellX);
        writer.Write(state.CellY);

        writer.Write(state.Health);
        writer.Write(state.MaxHealth);
        writer.Write(state.Energy);
        writer.Write(state.MaxEnergy);
    }

    public static StatePacket Read(ref SpanReadWrite reader)
    {
        string place = reader.ReadString();
        if (place.Length == 0)
            return new StatePacket(null);
        return new StatePacket(new PlayerState(
            Place: place,
            X: reader.ReadSingle(),
            Y: reader.ReadSingle(),
            ScaleX: reader.ReadSingle(),
            ScaleY: reader.ReadSingle(),
            Frame: reader.ReadSingle(),
            Row: reader.ReadByte(),
            Depth: reader.ReadInt32(),
            Visible: reader.ReadBoolean(),
            Angle: reader.ReadSingle(),
            Diss: reader.ReadSingle(),
            Alpha: reader.ReadSingle(),
            ShadowSprite: reader.ReadInt32(),
            ShadowX: reader.ReadSingle(),
            ShadowY: reader.ReadSingle(),
            ShadowScaleX: reader.ReadSingle(),
            ShadowScaleY: reader.ReadSingle(),
            ShadowAlpha: reader.ReadSingle(),
            CellX: reader.ReadInt16(),
            CellY: reader.ReadInt16(),
            Health: reader.ReadSingle(),
            MaxHealth: reader.ReadSingle(),
            Energy: reader.ReadSingle(),
            MaxEnergy: reader.ReadSingle()));
    }
}
