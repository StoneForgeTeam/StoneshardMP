using StoneForge;

namespace StoneshardMP;

// Mirrors the legacy manager's two tiny injections: input events occur after Begin Step and before the first
// unit Step. While that window is open the out-of-combat gate does not reject a click; it merely holds the
// resulting movement/action until the next host tick.
public sealed class ClockInputPhase : GameObject
{
    private Instance _instance;

    public ClockInputPhase() : base("clock_input", "")
    {
        Persistent = true;
        Visible = false;
    }

    public void Ensure()
    {
        if (_instance.IsNone || !_instance.Exists)
            _instance = Create(-10000, -10000);
    }

    protected override void OnCreate(Instance self) => Game.Global["mp_world_input_phase"] = false;
    protected override void OnBeginStep(Instance self) => Game.Global["mp_world_input_phase"] = true;
    protected override void OnStep(Instance self) => Game.Global["mp_world_input_phase"] = false;
}
