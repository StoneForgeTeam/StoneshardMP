using System.IO;
using System.IO.Compression;
using System.Text;
using StoneshardMP.Memory;

namespace StoneshardMP.Net.Packets;

// Saves and characters travel as gzipped UTF-8 JSON - a save's few hundred KB of JSON is tens of KB - under
// PacketCodec.MaxBytes, LiteNetLib fragmenting it.
public static class JoinCompression
{
    public static byte[] Compress(string json)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            gzip.Write(bytes, 0, bytes.Length);
        }
        return output.ToArray();
    }

    public static string Decompress(byte[] data)
    {
        using var input = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
        using var reader = new StreamReader(input, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    // A length-prefixed byte blob (the writers: Write(length), Write(bytes)).
    public static byte[] ReadBlob(ref SpanReadWrite reader)
    {
        int length = reader.ReadInt32();
        if (length < 0 || length > reader.Length - reader.Position)
            throw new EndOfStreamException("Blob exceeds remaining packet data");
        return reader.ReadBytes(length).ToArray();
    }
}
