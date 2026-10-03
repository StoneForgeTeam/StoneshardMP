using StoneshardMP.Memory;

namespace StoneshardMP.Net.Packets;

// A contract (ContractSync), from a game in the world with that seed: one that changed, or one of the host's full copy
// for a client just in its world - its list (0: every kind, 1: handed out), index and JSON, compressed
// (JoinCompression). Or a client asking for that copy (no contract).
public readonly record struct ContractPacket(ContractKind Kind, double Seed, byte List, int Index, byte[] Data) : IPacket
{
    public const byte PacketId = 24;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer)
    {
        writer.Write((byte)Kind);
        writer.Write(Seed);
        writer.Write(List);
        writer.Write(Index);
        writer.Write(Data.Length);
        writer.Write((System.ReadOnlySpan<byte>)Data);
    }

    public static ContractPacket Read(ref SpanReadWrite reader) => new(
        Kind: (ContractKind)reader.ReadByte(),
        Seed: reader.ReadDouble(),
        List: reader.ReadByte(),
        Index: reader.ReadInt32(),
        Data: JoinCompression.ReadBlob(ref reader));
}

public enum ContractKind : byte
{
    Changed = 0,
    Full = 1,
    CopyRequest = 2,
}
