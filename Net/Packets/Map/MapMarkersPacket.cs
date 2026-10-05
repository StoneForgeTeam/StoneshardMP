using System;
using StoneForge;
using StoneshardMP.Memory;

namespace StoneshardMP.Net.Packets;

/// <summary>To everyone: every marker on the sender's world map, after it changed there - everyone in the world shares
/// one set (MapMarkerSync).</summary>
public readonly record struct MapMarkersPacket(MapMarker[] Markers) : IPacket
{
    public const byte PacketId = 36;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer)
    {
        int count = Math.Min(Markers.Length, ushort.MaxValue);
        writer.Write((ushort)count);
        for (int i = 0; i < count; i++)
        {
            MapMarker marker = Markers[i];
            writer.Write(marker.Sprite);
            writer.Write((byte)Math.Clamp(marker.Image, 0, byte.MaxValue));
            writer.Write(marker.Position.X);
            writer.Write(marker.Position.Y);
        }
    }

    public static MapMarkersPacket Read(ref SpanReadWrite reader)
    {
        var markers = new MapMarker[reader.ReadUInt16()];
        for (int i = 0; i < markers.Length; i++)
        {
            string sprite = reader.ReadString();
            int image = reader.ReadByte();
            double x = reader.Read<double>(), y = reader.Read<double>();
            markers[i] = new MapMarker(sprite, image, new Point(x, y));
        }
        return new MapMarkersPacket(markers);
    }
}
