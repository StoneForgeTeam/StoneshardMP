using System.Linq;
using StoneForge;
using StoneshardMP.Features.Areas;
using StoneshardMP.Features.Players;
using StoneshardMP.Net;

namespace StoneshardMP.Features.Death;

// What a death puts right in the world, as reloading does in the game - the hostility it left behind:
// - The place died in calms down: whoever runs it (its owner, or the one dying if alone there) ends its panic - the game's
//   own scr_villagePanicOff, as when a fine's paid: every NPC neutral again, the players off their enemy lists. Their
//   copies elsewhere follow the owner's roster (is_neutral, is_player_enemy).
// - The faction's crime record - its state (wanted, dead or alive), the crime status and penalty, the attacks counted -
//   goes back to what the host's last save has (clear, if there's no save yet). The host sets it with the game's own
//   scr_globalFraction, which QuestSync shares with everyone.
public sealed class DeathCalm
{
    private static readonly string[] CrimeKeys = { "Crime_State", "Crime Status", "Penalty", "Attack_Count" };

    private readonly ModContext _context;
    private readonly Session _session;
    private readonly AreaOwnership _ownership;

    public DeathCalm(ModContext context, Session session, AreaOwnership ownership)
    {
        _context = context;
        _session = session;
        _ownership = ownership;
        session.On<CalmPacket>((from, packet) => Calm(packet.Place, packet.Faction, from.Name));
    }

    /// <summary>We're dying here: our place calms down, here or where it's run, and the host restores the crime record.</summary>
    public void Died()
    {
        string place = OurPlayer.Place ?? "";
        string faction = Faction();
        // (Ours to calm if we run it - or are alone in it; its owner's otherwise.)
        if (_ownership.Role != AreaRole.Follower)
            CalmPlace(place, "us");
        if (_session.Mode == Session.SessionMode.Host)
            RestoreCrime(faction, "us");
        _session.Send(new CalmPacket(place, faction));
    }

    private void Calm(string place, string faction, string who)
    {
        if (_ownership.Role == AreaRole.Owner && _ownership.Place == place)
            CalmPlace(place, who);
        if (_session.Mode == Session.SessionMode.Host)
            RestoreCrime(faction, who);
    }

    // The settlement's faction where we are ("" for none, or no crime there).
    private static string Faction()
    {
        GmValue owner = Game.CallScript("scr_glmap_getOwnerLocation", default);
        if (owner.AsStruct is not { } at)
            return "";
        using (at)
        {
            GmValue faction = Game.CallScript("scr_globaltile_get", default, "Fraction", at["x"], at["y"]);
            return faction.Kind == GmKind.String ? faction.AsString : "";
        }
    }

    private void CalmPlace(string place, string who)
    {
        if (!Gm.InGame && !Instances.All(GameObjectId.o_NPC).Any())
            return;
        bool panicking = Game.CallScript("scr_villagePanicGet", default).AsBool;
        Game.CallScript("scr_villagePanicOff", default);
        _context.Log($"Death ({who}): {place} calmed down{(panicking ? " (it was in a panic)" : "")}");
    }

    // Host: the faction's crime record as our last save has it.
    private void RestoreCrime(string faction, string who)
    {
        if (faction.Length == 0)
            return;
        DsMap? saved = null;
        try
        {
            if (SaveSlots.CurrentSave is { } save
                && Game.CallScript("scr_slotSaveDataMapLoad", default, save.Slot.Name, save.Name).AsDsMap is { Exists: true } read)
                saved = read;
            DsMap? record = saved?.GetMap("factionsDataMap")?.GetMap(faction);
            foreach (string key in CrimeKeys)
            {
                GmValue value = record is { } r && r[key] is { Kind: GmKind.Real } v ? v : 0;
                Game.CallScript("scr_globalFraction", default, faction, key, value);
            }
            // (The wanted marks on us follow the record - the game's own check.)
            Game.CallScript("scr_check_crime_state", default);
            _context.Log($"Death ({who}): {faction}'s crime record back to {(record != null ? "our last save's" : "clear")}");
        }
        finally
        {
            saved?.Destroy();
        }
    }
}
