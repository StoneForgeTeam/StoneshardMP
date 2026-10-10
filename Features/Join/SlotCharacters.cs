using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Text.Json.Nodes;
using StoneForge;

namespace StoneshardMP.Features.Join;

/// <summary>A world slot's character, for the lobby: the game's name for it, its level, its class ("Runaway Sorceress")
/// and its avatar - the portrait the game's save menu shows for it (characterDataMap's "avatar", a sprite's name).</summary>
public sealed record SlotCharacter(string Name, int Level, string Class, string Avatar)
{
    /// <summary>"Arna, level 5".</summary>
    public string Label => Level > 0 ? $"{Name}, level {Level}" : Name;
}

// Host: who each of the world's player slots is (WorldSlots) - its character's name, level, class and avatar, or a new
// character if the slot has none - for the main menu's lobby (PlayersPanel). In our world, from its save data as it is;
// on the main menu, from the save picked to play (HostLobby - read once, again each time we're back on the menu), nothing
// till one is. The host's own is slot 0: the save's own character.
public sealed class SlotCharacters
{
    // (In our world: read again this often - characters come in, level up.)
    private const int RefreshFrames = 60;

    // A slot's character, and the characters kept by name from before world slots (by the player's name).
    private readonly Dictionary<int, SlotCharacter> _slots = new();
    private readonly Dictionary<string, SlotCharacter> _byName = new();
    private readonly Func<SaveFile?> _picked;
    private string? _readFrom;
    private bool _wasOnMenu, _known;
    private int _frames;

    public SlotCharacters(Func<SaveFile?> picked) => _picked = picked;

    /// <summary>Whether there's a save to say who the slots are (none picked yet on the menu: no).</summary>
    public bool Known => _known;

    /// <summary>A slot's character, for the player playing it (a character of theirs kept by name from before world slots
    /// counts); null if it has none - a new character - or there's no save to say (Known).</summary>
    public SlotCharacter? Of(int slot, string? player = null)
        => !_known ? null
            : _slots.TryGetValue(slot, out var character) ? character
            : player != null && slot > 0 && _byName.TryGetValue(player, out var old) ? old
            : null;

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
        if (save.GetMap("characterDataMap") is { } own && Describe(own.ToJsonNode()) is { } host)
            _slots[0] = host;
        foreach (var (key, json) in JoinSave.StoredCharacters(save))
        {
            if (Describe(json) is not { } character)
                continue;
            if (key.Slot is int slot)
                _slots[slot] = character;
            else if (key.Name is { } name)
                _byName[name] = character;
        }
    }

    // A kept character (its sections as JSON).
    private static SlotCharacter? Describe(string json)
    {
        try
        {
            return JsonNode.Parse(json)?["characterDataMap"] is JsonObject character ? Describe(character) : null;
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    // A character's map (characterDataMap): the game's name for it (global.char_name, by its name key), its level, its
    // class ("RunawaySorceress", shown "Runaway Sorceress") and its avatar (the game's save menu falls back to s_Default).
    private static SlotCharacter? Describe(JsonObject character)
    {
        if (character["nameKey"]?.ToString() is not { Length: > 0 } nameKey)
            return null;
        string name = Game.Global["char_name"].AsDsMap is { } names && names.Get(nameKey, "N/A") is { Kind: GmKind.String } shown
            && shown.AsString != "N/A" ? shown.AsString : nameKey;
        int level = character["LVL"] is JsonValue lvl && lvl.TryGetValue(out double l) ? (int)l : 0;
        string cls = Regex.Replace(character["classKey"]?.ToString() ?? "", "(?<=[a-z])(?=[A-Z])", " ");
        string avatar = character["avatar"]?.ToString() is { Length: > 0 } a ? a : "s_Default";
        return new SlotCharacter(name, level, cls, avatar);
    }
}
