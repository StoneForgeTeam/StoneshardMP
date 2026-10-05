namespace StoneshardMP.Features.Areas;

/// <summary>What our game is to the place it's in (AreaOwnership).</summary>
public enum AreaRole
{
    /// <summary>Nobody else is here: we run it, as single-player does.</summary>
    Alone,
    /// <summary>Others are here, and we run it: our units, loot and fights are the real ones, and we stream them.</summary>
    Owner,
    /// <summary>Others are here, and one of them runs it: our units are copies of theirs, with their AI off.</summary>
    Follower,
}
