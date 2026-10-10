using System;
using System.Collections.Generic;

namespace StoneshardMP.Features.Dev;

// Dev tools: the latest happenings worth a look (newest first) - what the dev tools did, desyncs found, and whatever a
// feature notes with Add - for the Log tab. Also written to the mod's log, so they're in bridge.log after.
public static class DevLog
{
    private const int Max = 300;
    private static readonly LinkedList<string> Lines = new();

    /// <summary>Where Add also writes (the mod's log), once the dev tools are set up.</summary>
    public static Action<string>? Echo { get; set; }

    public static IEnumerable<string> Recent => Lines;

    public static void Add(string line)
    {
        Lines.AddFirst($"{DateTime.Now:HH:mm:ss} {line}");
        while (Lines.Count > Max)
            Lines.RemoveLast();
        Echo?.Invoke("[dev] " + line);
    }

    public static void Clear() => Lines.Clear();
}
