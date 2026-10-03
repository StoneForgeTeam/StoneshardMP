using System;
using StoneForge;
using StoneshardMP.Memory;

namespace StoneshardMP.Net.Packets;

// A call to one of the game's shared-state scripts (QuestSync: a quest step, reputation, a dialogue or location flag,
// a crime record), from a game in the world with that seed, for the others to make too. Its arguments are numbers,
// text, true/false or undefined (anything else - an array, a struct - goes as undefined, so the script's default
// applies).
public readonly record struct SharedCallPacket(double Seed, string Script, GmValue[] Args) : IPacket
{
    public const byte PacketId = 22;
    private const byte Undefined = 0, Real = 1, Text = 2, Bool = 3;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer)
    {
        writer.Write(Seed);
        writer.Write(Script);
        writer.Write((byte)Args.Length);
        foreach (GmValue arg in Args)
        {
            switch (arg.Kind)
            {
                case GmKind.Real:
                    writer.Write(Real);
                    writer.Write(arg.AsReal);
                    break;
                case GmKind.String:
                    writer.Write(Text);
                    writer.Write(arg.AsString);
                    break;
                case GmKind.Bool:
                    writer.Write(Bool);
                    writer.Write(arg.AsBool ? (byte)1 : (byte)0);
                    break;
                default:
                    writer.Write(Undefined);
                    break;
            }
        }
    }

    public static SharedCallPacket Read(ref SpanReadWrite reader)
    {
        double seed = reader.ReadDouble();
        string script = reader.ReadString();
        var args = new GmValue[reader.ReadByte()];
        for (int i = 0; i < args.Length; i++)
        {
            args[i] = reader.ReadByte() switch
            {
                Real => reader.ReadDouble(),
                Text => reader.ReadString(),
                Bool => reader.ReadByte() != 0,
                Undefined => GmValue.Undefined,
                _ => throw new InvalidOperationException("Unknown argument type"),
            };
        }
        return new SharedCallPacket(seed, script, args);
    }
}
