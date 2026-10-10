using System;
using System.Collections.Generic;
using System.Linq;
using StoneForge;
using StoneshardMP.Net;
using StoneshardMP.Net.Packets;

namespace StoneshardMP.Features.Recipes;

// Recipes are the party's: one player learning a recipe or schematic (reading it: o_inv_recipes' user event 14, which
// adds its id to one of the character's lists - recipesFoodOpened for cooking, recipesConsumsOpened for crafting) teaches
// everyone in the world. Each game sends its lists as they grow, and all of them again when someone comes into the world
// (so a player joining later catches up); each adds what it lacks to its own lists, says so, and refreshes the crafting
// menu if it's open, as the game does after a recipe's read. So every character's lists are the union of everyone's.
public sealed class RecipeSync
{
    private static readonly string[] Lists = { "recipesFoodOpened", "recipesConsumsOpened" };
    // (Our lists looked at this often.)
    private const int Interval = 60;

    private readonly Session _session;
    private readonly Func<bool> _inSharedWorld;
    // What we last sent of each list (its ids, sorted), and who was in the world then.
    private readonly Dictionary<string, string> _sent = new();
    private string _present = "";
    private int _frame;

    public RecipeSync(Session session, Func<bool> inSharedWorld)
    {
        _session = session;
        _inSharedWorld = inSharedWorld;
        session.On<RecipesPacket>(Receive);
    }

    public void Clear()
    {
        _sent.Clear();
        _present = "";
    }

    private bool Ready => _session.Connected && _inSharedWorld() && Gm.InGame && StoneForge.Player.Exists;

    public void Tick()
    {
        if (!Ready || ++_frame % Interval != 0)
            return;
        // (Someone's come into the world: everything again, for them.)
        string present = string.Join(",", _session.Players.Where(p => p.State != null).Select(p => p.Slot).OrderBy(s => s));
        if (present != _present)
        {
            _present = present;
            _sent.Clear();
        }
        if (present.Length == 0)
            return;
        foreach (string list in Lists)
        {
            string ids = string.Join(",", Known(list).OrderBy(id => id, StringComparer.Ordinal));
            if (_sent.TryGetValue(list, out string? sent) && sent == ids)
                continue;
            _sent[list] = ids;
            _session.Send(new RecipesPacket(list, ids));
        }
    }

    // A list of ours: the ids in it.
    private static List<string> Known(string list)
        => StoneForge.Player.Attribute(list).AsDsList is { Exists: true } ids
            ? Enumerable.Range(0, ids.Count).Select(i => ids[i]).Where(v => v.Kind == GmKind.String).Select(v => v.AsString).ToList()
            : new List<string>();

    private void Receive(RemotePlayer from, RecipesPacket packet)
    {
        if (!Ready || !Lists.Contains(packet.List) || StoneForge.Player.Attribute(packet.List).AsDsList is not { Exists: true } ours)
            return;
        var have = new HashSet<string>(Known(packet.List));
        int learned = 0;
        foreach (string id in packet.Ids.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!have.Add(id))
                continue;
            ours.Add(id);
            learned++;
        }
        if (learned == 0)
            return;
        bool food = packet.List == "recipesFoodOpened";
        Game.CallScript("scr_actionsLogAddMessage", default,
            $"{from.Name} taught you {learned} {(food ? "recipe" : "schematic")}{(learned == 1 ? "" : "s")}.");
        // (As reading one does: the crafting menu, if it's open, shows them.)
        foreach (Instance menu in Instances.All(GameObjectId.o_craftingMenu))
            foreach (int e in new[] { 11, 13, 12 })
                Game.CallBuiltinAs("event_user", menu, menu, e);
    }
}
