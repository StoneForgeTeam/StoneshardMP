namespace StoneshardMP.Features.Loot;

public enum LootKind : byte
{
    // Owner -> followers.
    Snapshot = 0,
    Changes = 1,
    // Follower -> owner.
    Taken = 2,
    Dropped = 3,
    SnapshotRequest = 4,
}
