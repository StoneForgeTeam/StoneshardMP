using StoneForge;

namespace StoneshardMP.Features.Players;

// Another player's character on our screen (o_stoneshardmp__player): their look, drawn where and as their game draws
// them, with their shadow and a name tag. It is a passive child of o_enemy, so it occupies its actual cell and the
// game recognizes it as a character, without inheriting the training dummy's protection-class interaction.
// Which player's it is: its mp_slot. What it draws comes from PlayerManager's view of them.
public sealed class Player : GameObject
{
    private readonly PlayerManager _manager;

    public Player(PlayerManager manager) : base("player", "o_enemy")
    {
        _manager = manager;
        Sprite = "s_dummy";
    }

    protected override bool ReplacesDraw => true;

    protected override void OnCreate(Instance self)
    {
        // A direct o_enemy child has the unit machinery but no mob parameter record. Give it the game's harmless
        // Caravan Dummy record first: that supplies every field the normal inspection UI expects (type, stats,
        // resistances, icons, mobIndex, etc.) without inheriting the dummy object's context actions.
        Game.CallScript("scr_param", self, "Caravan Dummy");
        // o_enemy supplies the normal unit fields, collision and target shape. Keep this proxy passive:
        // no AI, no saves, no dialogue, no enemy attention, and enough local HP that unsynchronized local damage
        // cannot destroy it before combat forwarding is implemented.
        self["ai_is_on"] = false;
        self["is_neutral"] = true;
        self["is_ignored_by_enemies"] = true;
        self["can_speak"] = false;
        self["roomEntityIsSavable"] = false;
        self["name"] = "Player";
        self["desc"] = "Another player.";
        self["Unbreakable"] = true;
        self["can_broke"] = false;
        self["can_drop_loot"] = false;
        self["is_full_destroy"] = false;
        self["corpse_type"] = -4;
        self["blood_emit"] = false;
        self["blood_ext"] = false;
        self["max_hp"] = 1000000000;
        self["HP"] = 1000000000;
        self["max_mp"] = 1000000000;
        self["MP"] = 1000000000;
        // o_enemy schedules Alarm 2 to derive stats from bSTR/bAGI/etc. A passive visual proxy has none of
        // those base-stat fields, and must never run that calculation.
        self.Alarm[2] = -1;
    }

    // The inherited o_enemy Destroy event has loot and corpse work intended for a real mob. This runs before that
    // event and leaves the proxy with nothing to save, drop, or inspect as a corpse.
    protected override void OnDestroy(Instance self)
    {
        self["is_full_destroy"] = false;
        self["can_drop_loot"] = false;
        self["corpse_type"] = -4;
    }

    protected override void OnStep(Instance self) => _manager.Step(self);

    // (Draw Begin: the game's compositor draws to surfaces, as o_player's own rebuild does here.)
    protected override void OnDrawBegin(Instance self) => _manager.Build(self);

    protected override void OnDraw(Instance self) => _manager.Draw(self);
}
