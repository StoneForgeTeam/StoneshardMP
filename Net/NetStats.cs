using System;
using System.Collections.Generic;
using System.Linq;

namespace StoneshardMP.Net;

// What the session sends and receives, for the dev tools: totals and each packet type's count and bytes, both ways, a
// rate over the last second, and the latest packets (the chatty ones - a player's state every frame - left out unless
// asked for).
public static class NetStats
{
    public sealed class Counter
    {
        public long Count, Bytes;
    }

    private const int RecentMax = 200;
    // (Sent every few frames by every player: they'd drown the rest.)
    private static readonly HashSet<string> Chatty = new() { "PlayerStatePacket", "PingPacket", "AreaUnitsPacket" };

    public static readonly Dictionary<string, Counter> In = new(), Out = new();
    public static long BytesIn, BytesOut, PacketsIn, PacketsOut;
    // (Per second: the last whole second's.)
    public static long RateIn, RateOut;
    private static long _secondStart, _secondIn, _secondOut;
    private static readonly LinkedList<string> RecentList = new();

    /// <summary>Whether the latest-packets list keeps the chatty ones too.</summary>
    public static bool LogChatty { get; set; }

    public static IEnumerable<string> Recent => RecentList;

    public static void Sent(string type, int bytes, int to) => Count(Out, type, bytes, true, to < 0 || to == Session.Everyone ? "all" : $"#{to}");

    public static void Received(string type, int bytes, int from) => Count(In, type, bytes, false, $"#{from}");

    private static void Count(Dictionary<string, Counter> table, string type, int bytes, bool sent, string peer)
    {
        if (!table.TryGetValue(type, out var counter))
            table[type] = counter = new Counter();
        counter.Count++;
        counter.Bytes += bytes;
        if (sent) { PacketsOut++; BytesOut += bytes; _secondOut += bytes; }
        else { PacketsIn++; BytesIn += bytes; _secondIn += bytes; }
        Roll();
        if (LogChatty || !Chatty.Contains(type))
        {
            RecentList.AddFirst($"{DateTime.Now:HH:mm:ss.f} {(sent ? "->" : "<-")} {peer} {type.Replace("Packet", "")} {bytes}B");
            while (RecentList.Count > RecentMax)
                RecentList.RemoveLast();
        }
    }

    // (The rate: once a second has gone, what it carried.)
    public static void Roll()
    {
        long now = Environment.TickCount64;
        if (now - _secondStart < 1000)
            return;
        RateIn = _secondIn;
        RateOut = _secondOut;
        _secondIn = _secondOut = 0;
        _secondStart = now;
    }

    public static IEnumerable<(string Type, Counter Counter)> Top(Dictionary<string, Counter> table, int count)
        => table.OrderByDescending(p => p.Value.Bytes).Take(count).Select(p => (p.Key, p.Value));

    public static void Reset()
    {
        In.Clear();
        Out.Clear();
        BytesIn = BytesOut = PacketsIn = PacketsOut = RateIn = RateOut = 0;
        RecentList.Clear();
    }
}
