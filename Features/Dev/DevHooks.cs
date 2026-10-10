using System;
using System.Collections.Generic;
using StoneshardMP.Features.Areas;
using StoneshardMP.Features.Players;
using StoneshardMP.Net;

namespace StoneshardMP.Features.Dev;

// What the dev tools reach into: the session and the place's ownership and units, and from the rest of the mod - each
// feature's one-line state, starting the place's syncing over, and the dump.
public sealed class DevHooks
{
    public required Session Session { get; init; }
    public required AreaOwnership Ownership { get; init; }
    public required AreaUnits AreaUnits { get; init; }
    public required PlayerManager Players { get; init; }
    /// <summary>Each feature's state, a line each (the Area tab).</summary>
    public required IReadOnlyList<(string Name, Func<string> State)> Features { get; init; }
    /// <summary>Everything kept about the place we're in, dropped: each feature starts it over (asks the owner again).</summary>
    public required Action Resync { get; init; }
    /// <summary>The debug dump (Ctrl+Shift+D's), written now.</summary>
    public required Action Dump { get; init; }
    /// <summary>Whether we're in the shared world (not on the menu, not alone).</summary>
    public required Func<bool> InSharedWorld { get; init; }
}
