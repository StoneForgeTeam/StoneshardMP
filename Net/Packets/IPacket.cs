using StoneshardMP.Memory;

namespace StoneshardMP.Net.Packets;

public interface IPacket
{
    byte Id { get; }
    void Write(ref SpanReadWrite writer);
}
