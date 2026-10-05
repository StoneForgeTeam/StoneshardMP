using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using StoneForge;

namespace StoneshardMP.Features.Join;

// Host: who each of the world's player slots is (WorldSlots) - its character's name and level, or a new character if the
// slot has none - for the main menu's player list (PlayersPanel). In our world, from its save data as it is; on the main
// menu, from the save picked to play (HostLobby - read once, again each time we're back on the menu), nothing till one
// is. The host's own is slot 0: the save's own character.
public sealed class SlotCharacters
{
    // (In our world: read again this often - characters come in, level up.)
    private const int RefreshFrames = 60;

    // A slot's character, and the characters kept by name from before world slots (by the player's name).
    private readonly Dictionary<int, string> _slots = new();
    private readonly Dictionary<string, string> _byName = new();
    private readonly Func<SaveFile?> _picked;
    private string? _readFrom;
    private bool _wasOnMenu, _known;
    private int _frames;

    public SlotCharacters(Func<SaveFile?> picked) => _picked = picked;

    /// <summary>A slot's character - "Arna, level 5" - for the player playing it (a character of theirs kept by name
    /// from before world slots counts); "new character" if it has none; "" with no save to say (none picked yet).</summary>
    public string Label(int slot, string? player = null)
        => !_known ? ""
            : _slots.TryGetValue(slot, out string? label) ? label
            : player != null && slot > 0 && _byName.TryGetValue(player, out string? old) ? old
            : "new character";

    /// <summary>Each frame the list shows (the host's): read again when it's time.</summary>
    public void Refresh()
    {
        if (JoinSave.HostInWorld())
        {
            _wasOnMenu = false;
            _readFrom = null;
            if (++_frames % RefreshFrames == 1 && SaveData.Map is { } save)
            {
                Read(save);
                _known = true;
            }
            return;
        }
        _frames = 0;
        if (!Gm.InMainMenu)
            return;
        // (Back on the menu: read again - what we were in has been saved.)
        if (!_wasOnMenu)
        {
            _wasOnMenu = true;
            _readFrom = null;
        }
        var current = _picked();
        string from = current == null ? "" : current.Slot.Name + "/" + current.Name;
        if (from == _readFrom)
            return;
        _readFrom = from;
        _slots.Clear();
        _byName.Clear();
        _known = current != null;
        if (current == null || Game.CallScript("scr_slotSaveDataMapLoad", default, current.Slot.Name, current.Name).AsDsMap is not { Exists: true } read)
            return;
        try { Read(read); }
        finally { read.Destroy(); }
    }

    private void Read(DsMap save)
    {
        _slots.Clear();
        _byName.Clear();
        if (save.GetMap("characterDataMap") is { } own && Describe(own["nameKey"], own["LVL"]) is { } host)
            _slots[0] = host;
        foreach (var (key, json) in JoinSave.StoredCharacters(save))
        {
            if (Describe(json) is not { } label)
                continue;
            if (key.Slot is int slot)
                _slots[slot] = label;
            else if (key.Name is { } name)
                _byName[name] = label;
        }
    }

    // A kept character (its sections as JSON): its name and level.
    private static string? Describe(string json)
    {
        try
        {
            return JsonNode.Parse(json)?["characterDataMap"] is JsonObject character
                ? Describe(character["nameKey"]?.ToString() ?? "N/A", character["LVL"]?.GetValue<double>() ?? 0)
                : null;
        }
        catch (System.Exception e) when (e is System.Text.Json.JsonException or System.InvalidOperationException or System.FormatException)
        {
            return null;
        }
    }

    private static string? Describe(GmValue nameKey, GmValue level)
        => nameKey.Kind == GmKind.String ? Describe(nameKey.AsString, level.Kind == GmKind.Real ? level.AsReal : 0) : null;

    // "Arna, level 5": the game's name for the character (global.char_name, by its name key), and its level.
    private static string Describe(string nameKey, double level)
    {
        string name = Game.Global["char_name"].AsDsMap is { } names && names.Get(nameKey, "N/A") is { Kind: GmKind.String } shown
            && shown.AsString != "N/A" ? shown.AsString : nameKey;
        return level > 0 ? $"{name}, level {level:0}" : name;
    }
}
