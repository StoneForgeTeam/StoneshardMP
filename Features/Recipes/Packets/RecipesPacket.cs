using StoneshardMP.Memory;
using StoneshardMP.Net.Packets;

namespace StoneshardMP.Features.Recipes;

/// <summary>To everyone in the world: the recipes a player knows - one of the character's lists of them (List: the
/// game's attribute, "recipesFoodOpened" for cooking or "recipesConsumsOpened" for crafting schematics), every id in
/// it, comma-separated. Each game adds what it lacks.</summary>
public readonly record struct RecipesPacket(string List, string Ids) : IPacket
{
    public const byte PacketId = 56;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer)
    {
        writer.Write(List);
        writer.Write(Ids);
    }

    public static RecipesPacket Read(ref SpanReadWrite reader) => new(reader.ReadString(), reader.ReadString());
}
