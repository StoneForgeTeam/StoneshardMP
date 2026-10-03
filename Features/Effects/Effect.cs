using StoneForge;

namespace StoneshardMP.Features.Effects;

// One harmless remote visual effect. It has no parent and replaces its draw, so the game's real spell/hit/projectile
// code never runs here; EffectManager supplies exactly the sprite properties received over the network.
public sealed class Effect : GameObject
{
    private readonly EffectManager _manager;

    public Effect(EffectManager manager) : base("effect", "") => _manager = manager;

    protected override void OnCreate(Instance self)
    {
        self["image_speed"] = 0;
        self["mp_life"] = 60;
    }

    protected override void OnStep(Instance self) => _manager.Step(self);
}
