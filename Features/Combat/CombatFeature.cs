using System;
using StoneForge;
using StoneshardMP.Features.Areas;
using StoneshardMP.Features.Players;
using StoneshardMP.Net;

namespace StoneshardMP.Features.Combat;

// Combat where players are together, all of it: each game resolves its own character's fights, the owner's units the
// real ones (CombatSync); a follower's noise - steps, fights, spells - heard by the owner's units, from its stand-in
// (NoiseSync); a follower's Determination striking the owner's units that come into its marks (MarkStrikes); an enemy
// turning on a player beside it rather than chasing another (EnemyTargeting); a follower's arrows stuck in the owner's
// units, dropped with them (StuckArrows).
public sealed class CombatFeature
{
    private readonly CombatSync _sync;
    private readonly NoiseSync _noise;
    private readonly MarkStrikes _markStrikes;
    private readonly EnemyTargeting _targeting;
    private readonly StuckArrows _stuckArrows;

    public CombatFeature(ModContext context, Session session, AreaUnits areaUnits, AreaOwnership ownership, PlayerManager players,
        Func<bool> inSharedWorld)
    {
        _sync = new CombatSync(context, session, areaUnits, ownership, players, inSharedWorld);
        _noise = new NoiseSync(context, session, ownership, players, inSharedWorld);
        _markStrikes = new MarkStrikes(context);
        _targeting = new EnemyTargeting(context, ownership, players, inSharedWorld);
        _stuckArrows = new StuckArrows(context, session, areaUnits, ownership, inSharedWorld);
    }

    /// <summary>What's kept about fights under way, forgotten (a session's end).</summary>
    public void Clear() => _sync.Clear();
}
