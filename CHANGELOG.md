# StoneshardMP changes

Changes go under **## Unreleased** as they're made. Pushing a version tag (`git tag v0.2.0`, `git push origin v0.2.0`)
releases them: the release workflow titles that section with the version, sets it in mod.json and publishes the zip.

## Unreleased

- **Shared ground effects.** Where players are together, fire, acid, smoke and poison clouds, blood and lava follow the
  area owner's: what's missing in a follower's game is made, what the owner hasn't got is taken away, and each takes the
  owner's turns left, so it ends when the owner's does. What a follower's own player makes (a fire bomb, an acid flask,
  a smoke cloud) is made on the owner too. Copies don't spread on their own: a cloud's spread comes from the owner's. A
  place's own (a dungeon's lava) is in every game already and isn't sent. Protocol 36.

- **Crimes reach the owner.** A follower who hits one of the owner's town NPCs (a blow, an arrow, a thrown item)
  commits the crime on the owner too: the NPC counts it toward its warning, then the faction's crime is set, the guards
  are called and the town turns on the players, as in single player. The NPC's "stop that" warning is shown to the
  follower who did it, not the owner, and fighting on from it counts. The follower's own game keeps its own crime record
  (wanted, jail). The guards still head for the owner's player first. Protocol 36.

- **NPC conversations: one player talks or trades at a time, others listen in.** Talking to an NPC another player
  is talking to opens a listener's window instead: the game's dialogue window showing the talker's conversation as
  it goes - the NPC's line, the talker's last answer, and their answers greyed out - with [Stop listening] (or Esc).
  It shows exactly what the talker's game shows, so the random greetings and answer variants, and answers that depend
  on that player's quests and reputation, are theirs; the listener's game never starts the conversation itself, so
  nothing is said, rolled or flagged there, and closing it takes no turn. It closes when the conversation ends. An NPC
  someone is trading with can't be opened (you're told who's trading); two players starting at once, the first keeps
  it. An NPC in a conversation or trade stays put on its turns. A speech cloud bobs over a player who's talking or
  trading, and over their NPC. Protocol 36.

- **Dev tools for the mod's contributors** (`mod.json`'s `contributors`): Ctrl+Shift+M opens a window, on the menu
  and in the game, with tabs for the session (players, versions, pings, places, how fresh their state is), the area
  (its owner and followers, and what each feature is holding), the unit under the mouse (object, cell, health, AI
  state, sync ids, the ground effects and items on its cell), network traffic (totals, rate, the biggest packet types,
  the latest packets, and a simulated delay on what we send), a desync check (our units against the other games'
  here, differences listed and marked on the map), a log, and tools: sync ids over units, the cell under the mouse,
  the debug dump, starting the place's syncing over, teleporting (to the mouse, to a player) and passing a turn.
  Every tab can be copied to the clipboard. Two packets (50, 51) for the desync check; protocol 36.

- **Fixed: the white flag over a fleeing unit only showed in the owner's game.** The icons the game puts over a unit
  for its state - the flag when it flees, the "!" when it's alerted, threatening, suspicious - come from its AI, which
  is off for a follower's copies. They're copied with the owner's units now.

- **Fixed: a town NPC turned hostile could still only be talked to in a follower's game.** Its hostility to the
  players (`is_player_enemy`, what the game goes by to attack an NPC on a click rather than talk to it) is copied with
  the owner's units now, so followers get the attack cursor and can fight it.

- **Fixed: NPCs couldn't break a follower's runic boulder.** They broke the owner's copy, but the caster's game took
  that as a lost copy and sent the boulder back, whole. Now the owner tells the caster, whose boulder breaks as the
  game breaks one (its end animation, the caster's Runic Power put right). The same for stone spikes.

- **Dying in the host's world sends a client back to the last save - the host's.** Each time the host saves, every
  player's character as it is then becomes their checkpoint, placed where the host saved (kept in the host's save; a
  player not saved yet has the character they joined with). When a client dies, the game's death plays, and everything
  they picked up since the checkpoint - in their bag or worn - drops where they fell, for anyone to pick up: quest items
  and keys stay in the world, and nothing they still had is doubled. Amounts count: a stack (arrows, coins) drops only
  what's more than the checkpoint had, and a bag (a moneybag's gold, a backpack) drops with only what's new in it. Worn
  cursed gear drops too. The death screen offers only Respawn (back as
  their checkpoint, at the host's save, the world loaded in place) and Disconnect (back as their checkpoint next time).
  The world itself doesn't go back.
  - **The host dies the same way**, against its own checkpoint (its last save): what it picked up since drops, and its
    death screen has Respawn (the world as it is now reloaded in place with the host as it was at its last save; every
    client's character is asked for first, so they come back as they are), Load (the game's save menu - everyone loads
    it too) and Disconnect (stop hosting without saving). Respawn needs the world to have been saved once. Protocol 36.

## 0.3.0

- Built against StoneForge 0.9.0. `mod.json` lists the mod's contributors, for StoneForge's development tools.
  Corpse and placed-object syncing use StoneForge's `Gm` wrappers in place of raw GameMaker calls.

- Apply forced-move animation interruption on the area owner, and repair follower sprite/speed divergence even
  when the owner's last-sent animation is unchanged, so knocked-back NPCs agree about their work/idle pose.

- Play the runic boulder dismissal animation when a synced copy is removed, without repeating caster buff or damage effects.

- Fix spell replicas expiring after one turn: normalize event handles before checking replica identity. A stale
  placed-object roster can no longer remove or strip the owner from the caster's live boulder or stone spikes.

- **Shared placed objects.** Campfires, crafted tents/bedrolls, runic boulders and stone spikes follow the area
  owner's roster. Followers send placements and removals; spell replicas leave caster effects and expiry to
  the caster's game. Includes off-screen objects, arrival catch-up and ownership handoffs. Protocol 35.

- Keep the lobby players panel out of the way while the Load Game picker is open; restore it when the picker closes.

- **Shared corpses.** The area owner's death corpses are copied to followers, including appearance, decay and
  butchering loot. Follower-only death corpses are removed; room-placed corpses are left alone. Protocol 34.

- **Shared trap discovery.** A trap spotted by any player becomes revealed to everyone in the same area. Discoveries
  catch up when another player arrives; off-screen traps are revealed when they reactivate, without triggering or disarming them.
  Protocol 33.

- **Fixed: players on the same dungeon floor had different floors.** Each game worked out the same floor seed from the
  world seed, and the game seeds its generator with it to build the floor. But the generator was then put back to
  random once the seed script returned (StoneForge's `Game.WithSeed`), so the rest of the floor was each game's own.
  It's left seeded now, as the GML version did, so every game builds the same floor.
- **Fixed: NPCs doubling where players were together.** A follower applies the owner's units a few a frame. In a big
  place like Osbrook, the owner's next list came before it finished, and each new list started it over. So it never
  got to the end, where the units the owner didn't send are removed. Now a list that arrives mid-way waits its turn.
  Also, a unit with no twin on its exact cell is matched to the nearest one of the same kind within a few cells, instead
  of a new one being made beside it: an NPC that wandered a step in one game used to end up as two.
- **Fixed: a client asked for the host's whole world again at every room change**, because the game is briefly between
  rooms then. The host resent every place, and its older copies of places the client had just been in came back. It
  asks once now, as it comes into the host's world.
- **Shared breakables.** Where players are together, crates, barrels, furniture and everything else that can be broken
  have one health pool. Damage one player does comes off everyone's copy, so two players hitting the same crate add
  up, and once it breaks it breaks for everyone, with its own debris and noise. When players come together, the
  place's owner sends what's been damaged or broken there since it arrived. Something that's off screen gets the news
  when it comes back on screen. Protocol 32.
- **Shared chests.** Where players are together, chests, barrels, tombs, corpses and other containers have one set of
  contents for everyone there (StoneForge's `Containers`). Before, each game kept its own, so two players could loot the
  same chest and something stashed by one wasn't there for the other.
  - A container someone has open is theirs until they close it. Another player who tries to open it is told who's
    looking in it, in the game's log, and it stays shut for them.
  - Closing one sends what's in it to the others, whose copy becomes that. One they'd never opened counts as opened,
    with that loot, so it isn't rolled again.
  - When players come together, the place's owner sends every container it has opened, since the others' copies may be
    older. A container that's off screen gets its contents when it's next on screen.
  - A player's own stash chest isn't shared live: each player has their own (below).
- **World player slots** (as Divinity has them). The host's world keeps the other players' characters by slot (1, 2,
  and so on) instead of by name, so two players with the same name don't share a character. Each player plays the slot
  of the order they joined in. On the main menu, the host's player list shows each player's slot and has **Swap**,
  which moves a player to the next slot, trading places with whoever has it. Swap works while the player is still
  waiting to be let in, before the host is in its world. A character kept by name in an older save moves into its
  player's slot the next time it's saved.
  - The host can play another slot's character too. **Swap** on the host's own row picks the slot (0 is its own), on
    the main menu before loading. The next save the host loads trades the two characters and their stashes in the save,
    so the host plays that character and whoever plays that slot gets the host's. If the slot has no character in that
    save, the host plays its own.
  - Under each player in the list: their slot and its character's name and level ("slot 1: Arna, level 5"),
    or "new character" if the slot has none. On the main menu that's from the save picked to play; in the host's world,
    from the world as it is. The host sends the list to everyone when it changes, so clients see it too. Protocol 31.
  - **Hosting, a save is picked before it's played.** Continue picks the last save played, and a save clicked in Load
    Game's list is picked, not loaded. The player list says which save it is, and shows the slots and characters from
    it while everyone chooses. **Play** then loads it. New Game still starts straight away, since a new world has no
    characters to choose from.
- **Each player has their own stash in the chest by the bed** (`o_player_chest`). The game keeps a chest's items with
  its place, and places' saves go between games, so whoever left last decided what was in it: after a reload, only one
  player's items were there. Each player's stash is now kept on its own in the host's world, by world slot, place and
  chest. It's put in the chest just before that player opens it, and kept as theirs when they close it. A client's goes
  to the host, and comes back with the host's world when the client joins. A stash from before this is the host's; a
  client's starts empty.

## 0.2.0

- **Fixed: a player joining a world the host hadn't saved since making it crashed while loading.** The world map's
  fog is only written into the save data as the game saves, so the joining game got none and its world map failed to
  load it. The host writes it in now (StoneForge's `WorldMap.Save`) before sending the save.
- A door opened or shut goes to the others at once (StoneForge's `Doors.OnChanged`), where before each game compared
  its doors every few frames and could miss one opened and shut again in between. Saving a location as we leave it
  uses StoneForge's `Locations.OnSaved`.
- **Map markers are shared** (from the GML version). Everyone in the world has one set of markers on the world map.
  When a player places one or takes one off (StoneForge's `MapMarkers.OnPlaced` / `OnRemoved`), the whole set goes to
  the others, whose markers become those; an open map's change on the spot. A client joining gets the host's markers
  with its world. Protocol 27.
- **Area ownership** (from the GML version): where players share a place, whoever got there first runs it, not always
  the host. Each game runs the place its player is in; where two or more are together, one owns it (its units, loot,
  fights and rounds are the real ones) and the others follow it. The owner keeps the place for as long as it stays,
  then the earliest of the rest takes over. The host decides and tells each client who runs its place when that
  changes, and every second. Protocol 26. This fixes NPCs duplicating and respawning when players came and went:
  - The host arriving where a client had been took the place over with its own, older save of it, so what the client
    had killed came back. The client keeps running it now, and the host follows.
  - A client's copies of the host's units stood frozen (AI off, out of its turns) after the host left. Leaving the
    follower role hands them back: their AI is on and they take their own turns again (StoneForge's
    `Units.ReturnToTurns`).
  - A new owner rebinds the same units instead of making new ones.
  - Whoever leaves a place last sends its copy of the place (what's dead, taken, opened) to the others, even if
    they followed someone earlier. Before, a client that had once followed the host there never sent its copy, so
    what it did there after the host left was lost.
  - Shared rounds are run by the place's owner, and a follower's actions don't move the place's units a second
    time.
- **Fixed: the game crashed when another player left your place** - during a round, or on your next move once their
  units were yours to run again. Their stand-in stayed in the list of units the game runs each turn, and the units
  that had fought it still had it as their target; once it was destroyed, the units' turn read a unit that was gone.
  The stand-in is kept out of that list now, as the GML version did, and taken out quietly (StoneForge's
  `Units.Remove`): out of the grids, the turn list and its faction's list, with its effects, without its Destroy
  event, and with every unit's references to it cleared. Units handed back drop references to units that are gone
  too. (Its faction's list was what crashed the game when bandits were handed back: they looked there for players to
  fight.)
- **Doors are shared** (from the GML version). A few times a second each game compares its place's doors with how they
  last were, and one opened or shut - by a player, or by an NPC or enemy - opens or shuts in the other games there the
  game's own way, with its animation and sound. A door a player opened is unlocked for the others too (they had the
  key, or picked or broke the lock). When players come together, the place's first player (the host if it's there)
  sends all its doors and the others' match them; a door off screen gets its change when it's next on. Protocol 25.
- Cells and world-map tiles are StoneForge's `Cell` and `WorldTile` throughout, in place of `(x, y)` tuples: a player's state, the units' moves and rosters, following, the party frames' compass, and the shared world's tiles. (Packets are unchanged.)
- **Time goes by at one player's pace.** Every player's move is a world turn, and a turn lets 30 seconds pass, so with
  more players the day went by that many times as fast. A move now lets 30 seconds pass over the number of players in
  the world: 15 with two, 10 with three. What's left of a second carries to the next move, so it still adds up
  exactly. A shared round keeps its 30 seconds (everyone acts, then the world takes one turn), and sleeping,
  travelling and the like let their time pass as ever.
- `mod.json`'s `"stoneforge"` is `"latest"` while in development: any StoneForge loads it. A release names
  StoneForge's newest release, which it was built against, in its `mod.json` and notes. It fails if the mod needs
  StoneForge API that isn't released yet.
- **The game's code comes through StoneForge's API now, not the mod's own calls into it.** About 80 distinct direct calls
  to the game's scripts and functions were replaced by StoneForge's `Units`, `UnitEffects`, `Player`, `Combat`,
  `Factions`, `Draw`, `Blackout`, `GameDialogs`, `Journal`, `Contracts`, `Doors`, `Turns`, `Steam`, `SaveData`,
  `Locations` and `WorldTile`. The mod's own `UnitGrid` is gone; its code is StoneForge's `Units` now.
  - What's left is meant to be: a test of the game's JSON decoder in the debug dump, loading the host's save from
    memory, a unit's highlight refresh, the thief's wine check, and one check that a data structure still exists.
  - The Disconnect confirmation is StoneForge's `GameDialogs.Confirm`, so it no longer hooks the Esc menu's Exit
    button. Its Yes runs `Rooms.ToMainMenu(save: true)`, the game's own Save & Exit, which the host's wait for
    everyone's saves still holds.
- Needs StoneForge's next release (these APIs aren't in 0.5.0).

- **Who's in the game is on the main menu.** While you're hosting or in a game, the middle of its left edge lists everyone: you first,
  then the others, with the host marked and each player's ping. Alone as the host, it says players can join on the UDP
  port.
- **The host can kick a player** from that list: **Kick**, then **Sure?** within 3 seconds. The player's game closes
  the connection with "The host removed you from the game", the others see them leave, and the host's status says they
  were removed. (They can join again.)
- **No more Players & Settings window.** Its settings duplicated StoneshardMP's page in the Mods window, where they all
  are (name, join address, name tags, party frames, port, player limit), and its player list is the panel above. The
  Multiplayer screens lose the button.
- Following stops on a click on the world, now also not when the click is on a mod's UI (the party frames, the turn
  carousel...): StoneForge's `Mouse.ClickedWorld`.

## 0.1.0

The first release of StoneshardMP on StoneForge: co-op for Stoneshard, the host and up to 7 other players in one world,
over LiteNetLib (UDP).

- **Hosting and joining** from the main menu's Multiplayer screen. The host keeps everyone's save: a client joins with
  its character from the host's world, or makes a new one on the host's world map.
- **One shared world:** the same areas and dungeons from the world seed, what's dead, taken or opened, the host's
  weather and clock, ground loot, quests, reputation, dialogue flags, crime records and contracts.
- **Other players' characters** with their equipment, movement, attacks, spells and effects, name tags, and Follow from
  their right-click menu.
- **Combat together:** a client's attacks, knockbacks and status effects count on the host's enemies, enemies fight
  clients, and kill XP is shared with everyone nearby.
- **Turn-based rounds** when anyone's in combat, with the turn order carousel.
- **Party frames** for the other players: health, energy, level, effects, and a compass needle to them.
- **Saving and loading** through the host: when the host loads a save, everyone reloads into it in place, and the
  host's saves have everyone where they are. Disconnect in the Esc menu.
- Needs StoneForge 0.5.0 or newer.

## Development history

The versions before the first release, while StoneshardMP was ported from the GML version to StoneForge.

### 0.24.2

- **Only what a client's own player does to the host's units goes to the host.** 0.24.1 sent every move and effect on
  a client's copies, whatever made it. Copies on the world map moved by the game's own code went to the host as
  "moved unit 39 to 384,53". The enemies' own No Retreat buff, which refreshes itself every step, went as a flood of
  "put o_b_no_retreat on unit N (a refresh)", over a hundred of them.
  - Moves are sent only inside the client player's own attack, or a knockback whose owner is its player (o_knockback,
    from an attack or a skill; scr_knockback is now hooked).
  - Effects are sent only inside those, or when their owner is the client's player (a skill's).
- **The host no longer drops everyone.** The host's quest-trigger hook (scr_everyPlayerTurnQuestTriggers) threw
  "Instance … no longer exists" when the instance calling it had been destroyed while its own event was still running.
  After three in a row, StoneForge paused StoneshardMP on the host, which stopped its networking, and the client timed
  out. When the caller is already gone, the hook now leaves the game to run its own triggers, unshared, for that call.

### 0.24.1

- **Knockback and status effects from a client's actions happen in the host's world too.** Before, only an attack's
  damage went to the host. A client's knockback moved its copy of the enemy, but the host's enemy stayed put; a stun
  or bleed stayed on the copy, and the host's enemy kept acting. The two games' enemies then drifted apart. Now the
  client watches the game's own scripts for these, whatever caused them (an attack or a skill):
  - **A unit moved to another cell** (scr_change_coordinat: knockback, pull) goes to the host as UnitMovedPacket. The
    host moves the real unit there if the cell is free there.
  - **An effect created** (scr_effect_create) **or refreshed** (scr_effect_update) on one of the host's units goes to
    the host as UnitEffectPacket. The host puts it on the real unit with the game's own scripts (its immunities and the
    target's fortitude), from the client's stand-in. It counts as that player's part in the kill.
- **The host's roster carries each unit's visible effects**, with their durations. A client's copies get the same
  effects: missing ones are made, durations are set, and ones the host doesn't have are taken off. So effects from the
  host's own attacks show on clients too.
- **A client's copies are put back where the host has them** whenever they differ. Before, a copy was only moved when
  the host's cell changed, so a copy that moved on its own was never corrected. After a client's own move or effect,
  its copy is left alone for 1.5 seconds while the host catches up, so it doesn't snap back and forth.
- New packets 32 (UnitMovedPacket) and 33 (UnitEffectPacket); protocol 24. scr_change_coordinat, scr_effect_create and
  scr_effect_update are now hooked, so the first start after updating rebuilds the game data.
- Still to come: skills' and spells' damage.

### 0.24.0

- **Combat between players' games.** Each game resolves the fights its own character is in - it has the real stats,
  gear, buffs and skills - and the host's enemies are the real ones:
  - **A client's attacks count in the host's world.** The client rolls its attack as the game does - hit, dodge,
    block, crit, with its own weapon - against its copy of the host's enemy, and the host deals the damage to the real
    one, as from that client. Whether it dies is the host's to say: the client's copy stays alive until the host's
    update says it's dead, and its corpse and loot come from the host.
  - **Enemies fight clients.** On the host, a client's stand-in is in the game's "Player" faction list, so enemies
    hostile to the player chase and attack it as they would the player, and turn on a client who hits them. Their
    attack isn't resolved on the host: the client's game has its copy of the enemy attack the client's character, with
    its real armour, dodge and block.
  - **Kill XP is shared**, as in the GML version: when any player took part in a kill (hit it), every player in that
    place within 20 tiles gets its XP, each scaled by their own level against its tier as the game does, with the
    game's kill line in the log. The host is counted in the enemy's damage list if only a client hit it, so it gets its
    share from the game's own death code.
  - The host logs each hit ("<player> hit unit N (<object>): X damage, Y health left"); the client what it sent, and
    the XP for a kill.
  - Still to come: skills and spells, status effects, a client knocked out or killed.
- **Shared turn-based rounds are back** (from the GML version). Where two or more players are in the host's place and
  any of them needs turns - in combat, bleeding to death (their bleeds would take them to 0 before they stop and health
  comes back), or on fire - play goes in rounds: each player in turn, in slot order (the host first), one action each,
  then the enemies, all together.
  - Until it's your turn, and once you've taken it, your character counts as busy: no walking, attacking, skipping or
    using anything.
  - The host's enemies wait until every player has acted (the host's own action is held from setting them off), then
    all move. A client's action isn't a world turn of its own meanwhile.
  - Whoever hasn't acted in 30 s is skipped, so nobody holds a round up for good.
  - **The turn order carousel is back**, under your status effects: whose turn it is in the centre, who's next to its
    right and who just went to its left, turning as the turn moves on; the enemies' portrait is the nearest enemy after
    you. Below: YOUR TURN / <NAME>'S TURN / ENEMIES' TURN, and why play is turn-based. It always shows whose turn it is
    now (the GML version stepped through each turn for a moment, and fell behind quick rounds).
  - The host logs each round, who's acted and who was skipped; a client each round it's in.
- **Follow another player is back** (from the GML version): "Follow" on their character's right-click menu ("Stop
  following" while you do).
  - Whenever your character is standing still and they're more than a cell away, it walks to the free cell next to
    them on your side, as a click on the ground would - so in a round it goes on your turn.
  - When they leave your place, you go out the way they did: the door, stairs or entrance nearest where you last saw
    them (through it, or walked to and through, as the game's own Exit), or across the edge of the area; then on
    following them there.
  - It stops on a left click on the world in your own window (not on the UI), when an enemy's after you, when you lose
    them, or when they leave the game. It goes on while your game's window is in the background.
- **A crash when players stood next to each other is fixed.** Another player's character took their cell in the
  game's position grid even when your own character (or an area unit) was there for a moment, overwriting it; the game
  then crashed when it read that cell. It only moves onto a free cell now (or its own), as the GML version did, and
  catches up once the cell's free. And it's put on its cell at once rather than walked there: a walking unit clears the
  cell it leaves as it starts - which, after two players came through the same door onto the same cell, was yours.
- **A crash when a player came into a place another was in is fixed.** The units the host doesn't have are taken out
  of the other player's game without their Destroy event, and the effects on them (a stun, No Retreat...) were left
  behind pointing at a unit that was gone; the game crashed reading their target. They go with the unit now, as in
  the GML version - and with another player's character when it goes.
- Enemies go after another player's character again: it was marked ignored by enemies every step, undoing the combat
  change above.
- Needs the StoneForge release with `ContextMenus`.
- Protocol 23.

### 0.23.0

- **Party frames are back** (from the GML version), down the right edge of the screen: one per other player, in the
  game's own look.
  - Each frame has the game's tooltip frame, the HUD's health and energy bars and digits (to the thresholds' caps), and
    the player's head as the portrait.
  - It shows their name, their level ("Unconscious" when they're out cold) and their status effects, harmful first, as
    half-size icons to the left of the frame.
  - A compass needle points at them, with a label:
    - In the same place, the label is how many moves away they are.
    - In another world-map area, the needle points that way and the label is how many areas away ("3a").
    - In another room or dungeon floor of the same area, there's no needle: the label is "in", or their floor ("F2").
  - A gold tab at the screen edge slides the frames away and back, and remembers it. There's a "Party frames" setting
    to turn them off.
  - The frames are StoneForge UI elements on its new HUD layer (ModUI.Hud). They sit under the game's windows
    (inventory, map, dialogue), as before. A click on a game window over them goes to the window, and they hide with
    the HUD and in cutscenes.
- **Changes from the old frames:**
  - The distance is in moves (diagonals count as one, as the game moves), not straight-line tiles.
  - A frame pulses red while that player's health is under 30%.
  - Players whose state has stopped coming for 5 seconds show greyed out with "no word", rather than frozen as they
    were.
  - Another floor shows which one it is ("F2"), not just "in".
  - Hovering a frame shows the player's level, whether they're fighting, and their ping.
  - What the frames need goes as a PartyPacket (id 26, protocol 22): level, head, the caps, combat, effects. It's sent
    only when something changes, plus every 3 seconds and to a newcomer at once. Before, it went every half second.
    Effects go by object name, not index.

### 0.22.6

- **The Mods button stays on the main menu after multiplayer.** The Multiplayer screens put the menu back with
  MainMenu.RestoreButtons, which undoes everyone's changes since startup, the loader's Mods button included. They now
  undo only their own (MainMenu.UndoChanges). StoneForge also counts its own buttons as part of the startup menu now.

### 0.22.5

- **No flash of old gear when a player comes in.** On joining, or after a reload, a player now sends its look before
  its first position, every time. Before, positions went every other frame but the look only every 30 frames, and only
  when it changed. So for up to half a second the others drew the player in what it wore before the reload: a bow it
  had since dropped, for example.

### 0.22.4

- **The save top-up works when characters arrive mid-save.** The client's fresh character came in while the host's
  save was still running, and the host had its player hidden for a few frames. So the host held the character as
  "pending", wrote nothing, and dropped it when the save was loaded. Now a held character counts toward the top-up, the
  host keeps it once it's back in its world, and then it rewrites the save. Before, the rewrite ran when the character
  arrived.
- The host's request after a save no longer needs its player visible, only save data.
- New log lines: "Saved <slot>/<save>: asking N player(s)...", and "...character kept (held until we were back in our
  world)".

### 0.22.3

- **The client's character goes to the host with where it really is.** The host's log showed the client's refreshes
  arriving, but every one said the end of the intro (r_taverninside1floor, 481,507). Before reading its character, a
  client now checks each character section of its save data against the game's live one (global.characterDataMap
  and the rest: what scr_savegame writes to). Any that differ get relinked, and the client logs which.
- Each refresh logs what was sent next to where the player really is, to compare.
- **The black screen while the host loads says what's happening**, from the join status: "<host> is loading a save...",
  "Receiving <host>'s world...", "Received <host>'s world - loading...".
- A world that arrives damaged during a reload doesn't leave the client stuck on black: it goes back to the main menu.

### 0.22.2

- **The host's saves have every client where they really are.** Right after the host writes a save, it asks the clients
  in its world for their characters. When they arrive (within 5 seconds), it writes that save's data again with them.
  Before, a save only had what the last 20-second refresh had sent, so it could have a client back at an old spot.
- **The host loading a save fades clients to black at once.** The screen stays black while the host loads, and the
  host's world then loads in place behind it. The game's room change fades it back in. Before, clients played on in the
  old world until the reload suddenly began.
- The host logs where each client's character is as it keeps it, to check positions in the log.

### 0.22.1

- **A host loading a save no longer kicks clients to the main menu.** They stay in game. Once the save is up, the host's
  world loads in place with each client's character as that save has it: a fade to black and back.
  - New WorldReloadPacket (id 25) in place of HostLeftPacket for this; protocol 21.
  - The host lets nobody in until the loaded save has been up and calm for half a second. Before, it could answer with
    the world it was leaving.
  - A client with no character in the loaded save goes back to the main menu to make one.
- **The host's saves have where the clients really are.** A client in the host's world sends it its character every 20
  seconds, outside busy moments. It runs only the game's save step (scr_savegame), with no fade and nothing written
  here. Before, a client's character only went to the host when the client saved. A loaded save could put a client back
  where it last saved, such as next to Verren after the new-character intro.
- The host's Save & Exit request uses the same refresh instead of a client autosave.

### 0.22.0

- **Only the host loads a save.** A client's Esc menu has no Load Game: its own saves aren't the host's world.
- **When the host loads a save, everyone comes back into it.** That's from its Esc menu's Load Game, or the main menu's.
  - The clients go back to their main menus without saving, as when the host leaves its world, and ask again. Once the
    save is up, the host sends each their world with their character as that save has it.
  - Characters still coming from the world being left are dropped while it loads, along with any held for a world not
    yet up. Otherwise a client's character from the old world would overwrite the loaded save's.

### 0.21.2

- **The host's Esc menu has Disconnect too**, in place of Save & Exit while it hosts in its world. It asks with the
  game's confirmation, then runs the game's Save & Exit: it waits for everyone in its world to save, as before, and saves
  the host's game. Back on the main menu it stops hosting, which sends every client back to their main menu.

### 0.21.1

- The client's Esc menu button is **Disconnect** rather than the game's Exit. It asks with the game's own confirmation,
  sends the host our character (the game's Save & Exit, its save going to the host), goes back to the main menu, and
  leaves the session there - rather than staying connected on the menu.

### 0.21.0

- **A client's Esc menu has Exit, not Save & Exit**, while it plays the host's world (or makes its character for it),
  with StoneForge's new EscMenu: it keeps no saves of the host's world. Its Exit (the game's, with the game's
  confirmation) still sends the host its character first: confirmed, it runs the game's Save & Exit, whose save goes to
  the host and isn't written here. So the host has where the client got to, not just its last autosave. Back to the
  game's menu otherwise.

### 0.20.0

Two quest gaps from the GML version filled, with StoneForge's hooks on functions inside another script's file:
- **The vineyard thief's wine counts as anyone's.** The wine check (scr_npc_lines_vineyard_thief_check_wine) is hooked:
  while the quest triggers run, it says yes if another player holds the thief's wine. So the player without it no longer
  winds his quest back. Before, the check was out of reach: when another player had the wine and we didn't, that turn's
  quest triggers were skipped altogether, for every quest.
- **Gwynel's house cutscene steps are shared.** Verren walking there, the door, down to the lab and off to camp
  (scr_rewards_find_guinnel_1-7 and _door_1-3) move, animate and hide NPCs and open doors, which only works on the game
  running the area. A client's step is also made on the host when it's in the same place, so the host's Verren - the
  one everyone sees - does it. Its quest progress was shared already. Not the steps that pay out or count searches.
- Shared calls can be made only where they should be (`Share(..., madeHereIf)`).

### 0.19.13

- The debug dump (Ctrl+Shift+D), with the profiler on (Ctrl+Shift+P), has its whole last second: every part of every
  mod, where the overlay shows ten. That's average and worst ms a frame, runs a frame, and ms a run.

### 0.19.12

- The world turn run for another player's action sets one skill's alarm 10, not every skill's. All it does
  (o_abilities' alarm 10) is set global.skill_can_cast again, the same global for every skill, and none of o_skill's
  children does more. Setting an alarm is a slow call into the game (about 14 µs), and there are hundreds of skills: it
  was about 2.5 ms a step.
- The profiler splits the check that a turn can go in: the world, a cutscene, the player locked, the player's own turn.

### 0.19.11

- The profiler splits the world turn a game runs for another player's action into its parts: the check that it can go
  in, the game's world turn (scr_global_turn), the skills' cooldowns, and the units' turns (alarm 4).

### 0.19.10

- The host's unit roster costs a client less, and no longer in one go:
  - **Spread out:** it's applied 12 units a frame over the next frames (a newer roster starts over), and the tidying
    up - ours out of our turns, units the host didn't send removed - once it's all done. Before, it was all at once, up
    to 22 ms in one frame.
  - **Twins found from a table:** a unit we haven't bound finds its twin in a table of our unbound units (object and
    cell) made once a pass. Before, it searched every unit, reading each one's cell, for each unbound unit, on every
    roster.
  - **Grids found once:** moving a unit uses the controller and grids found once a pass, its position read once, and
    skips the big-unit scripts for a unit of one cell (they do nothing for one). Whether a unit is big is read once.
  - **Highlight on a new sprite only:** the highlight (scr_set_hl) is set again only when the unit's sprite changes,
    which is all it reads.
  - The profiler lists the parts: a unit applied, a unit bound, the tidying up.

### 0.19.9

- A client taking the host's clock writes just the seconds when that's all that differs, as before 0.19. Since 0.19.0,
  every host tick set the whole time (Time.Set), which also works the time of day out again. Minutes passing still go
  through Time.Advance, and a jump through Time.Set.
- (The host's "clock" in the profiler is mostly the world turn it runs for a client's step: the game's own turn for
  every unit, the same work its own steps cost.)

### 0.19.8

- Fix: the client stuttered every few frames where the host's NPCs are. Applying the host's unit roster took about 55 ms
  each time (up to 76 ms), 11 ms a frame on average, by the profiler. For every unit it wrote all its values, ran the
  game's highlight script, moved it through the grids, read its object again, and walked the turn list.
  - Now only what changed since the last roster is written.
  - The highlight runs again only then (not for a frame moving on), and the grids only when the unit's cell changed.
  - A unit's object is read once, and the turn list is walked only when a unit is new or the list has changed.

### 0.19.7

- Timings go to StoneForge's profiler (Ctrl+Shift+P): each feature's frame, the network, and the loot and area units
  received are listed under StoneshardMP. Its objects' events are timed by StoneForge itself. The mod's own Timings
  class and its log lines are gone.

### 0.19.6

- Fix: the loot sync slowed both games to about 24 fps. On the host it took 17 ms a frame (about 70 ms each time it ran),
  and on the client nearly 20 ms. Every few frames it asked the game, for every ground item, whether it's persistent and
  whether it's static, though neither ever changes. For an off-screen item each read walks the room's deactivated
  instances (thousands), and the client woke each static one to read it.
  - Both are read once per item now.
  - Checking whether known items are gone only asks the game about those not seen on the ground in the same tick.
- Timings also split out the loot packets received and the host's units applied on the client.

### 0.19.5

- Timings: while in a session, every 10 seconds the log has how long each part of the mod takes per frame (`Timings
  (host, 24 fps, per frame): loot 3.10ms (0.3/f), ...`). That covers each feature's frame, the network with what its
  packets do, and other players' and effects' object events. It's for finding what slows a game down.

### 0.19.4

- Fix: a client couldn't join a world where a dungeon's name has an apostrophe ("Bernarhof's Cenotaph"): the host's
  world arrived "damaged". The world sent to a client is written with its text as it is. System.Text.Json's default
  escaping (`'` for ', `<` for <...) made the game's json_decode give up on the world map's section, so the
  client stayed in its own copy of the map, waiting.

### 0.19.3

- With a kept unreadable world (0.19.2), the debug dump also tries the game's json_decode on it: whole, without
  System.Text.Json's escaping, and section by section, to the log.

### 0.19.2

- The debug dump's input line also shows StoneForge's hold on the game's input: its typing flag (hotkeys and key-bound
  clicks held off) and where its invisible mouse blocker is.
- A host's world that arrives unreadable is kept (`%LOCALAPPDATA%\StoneShard\stoneshardmp-unreadable-world.json`), and
  the log says why it didn't read.

### 0.19.1

- The debug dump (Ctrl+Shift+D) has an input line: what decides whether the player can act and the game shows its cell
  cursor and path. That's scr_is_cutscene's conditions (the cutscene controller, the UI hidden, the screen faded), a room
  change, a dialogue, the player's locks and turn alarms, and the world clock's input phase.

### 0.19.0

Protocol 20. No GML left: everything the mod did through its own GML or hand-rolled helpers now uses StoneForge's API.
Needs StoneForge 0.4.0 and its CharacterLook.
- **Ground loot** (`Features/Loot/LootSync.cs`): the 19 GML loot functions are C#, using `GroundItems`. That covers
  culled items, an item's saved state, and making one from it, and a throw's flight replayed. The loot tables (sync ids,
  bindings, drop tokens, marks) are C# dictionaries instead of GML globals. Matching the owner's loot against a
  follower's goes through the ground once per snapshot or diff, not once per item.
- **Area units** (`Features/Areas/AreaUnits.cs`): the roster snapshot and its apply are C#. Moving a unit through the
  game's grids, and removing one quietly, are in `UnitGrid` (also used for other players' units). The host's units go
  by sync ids of their own on the wire, and the bindings start over in a new place.
- **Other players' looks**: `CharacterLook` reads ours and builds theirs with the game's compositor (it replaces
  `OurPlayer.Look` / `Build`). The look's JSON is StoneForge's, hence the new protocol.
- **Where we are**: `WorldMap.Place`, the same string as before.
- **The shared world** (`SharedWorld`): tiles through `WorldMap`, locations through `Locations` (export, store, the
  copy list), the save data through `SaveData`.
  - Seeds are drawn with `Game.WithSeed`: tile seeds have the same values as before. A dungeon's floor seed and special
    floors are rolled under it too. Before, the special floors put the generator back to the floor seed's start,
    replaying its numbers. A dungeon's lists now go whole as JSON.
- **Joining** (`JoinSave`): `SaveData` (the character's sections, the players' characters as a mod map), `Game.IsBusy`
  for the calm moment, and `Rooms` for a new character and for going back to the menu.
- **Save names** (`SaveNames`): `SaveSlots.OnInfoSaving` and `SaveSlots.SetTitle`. StoneForge hooks scr_slotMapSave
  itself.
- **The clock** (`GameClock`): `Time` - a small gap passes with `Time.Advance` (as the player), then the clock is set to
  the host's exactly. The turn check uses `Game.IsCutscene`.
- **Contracts** (`ContractData`): `DsList` / `DsMap`, and a contract is copied in place with `DsMap.AssignFrom`, at any
  depth. Before, a nested map was copied shallowly, and a slot that changed kind kept its old mark.
- The debug dump lists what's around in C#, and counts the ground items (off screen too) in place of the old culling
  check.
- Gone: `GameData.cs` (the Ds, InGame and GmJson helpers) and the `GML` folder.

### 0.18.3

- Needs StoneForge 0.3.0 (off-screen instances, and hooks on undeclared scripts refused at load).

### 0.18.2

- The debug dump (Ctrl+Shift+D) checks StoneForge's new off-screen instances against the loot GML they're to
  replace. Its first line counts the room's ground loot by `Instances.All(o_loot, includeCulled: true)` and by
  `MpLootAll`, how many are culled and whether their built-ins read, and which of MpLootAll's are missing from
  StoneForge's list.

### 0.18.1

- The world turn and a new player object's setup are C# too, with StoneForge's new alarms (`instance.Alarm[n]`):
  the turn is held while the player's own (alarms 1 and 4) is under way, and sets the skills' alarm 10; a player
  object's alarm 2 (o_enemy's stat setup) is stopped. 27 GML functions are left: the loot sync, the area units, moving
  a player's unit through the grids, and the debug dump.

### 0.18.0

- Most of the mod's GML is C# now, using StoneForge's new arrays and structs (GmArray, GmStruct): 43 of its 73 GML
  functions. Nothing changes in game, and the protocol stays 19.
  - Joining (`Features/Join/JoinSave.cs`), with the save data's JSON handled by System.Text.Json.
  - The shared world (`Features/World/SharedWorld.cs`): tile and dungeon seeds, locations, tiles, the world copy and
    the weather. Floors a dungeon rebuilt are counted in C#, not in a game global.
  - Contracts (`Features/Contracts/ContractData.cs`), save names (`Features/Saves/SaveNames.cs`) and the clock
    (`Features/Clock/GameClock.cs`).
  - Our player (`Features/Players/OurPlayer.cs`): its state, look and resistances, and building another player's
    sprites. Its state is now a `PlayerState` straight away, worked out once a frame, where seven features each read
    and parsed it every frame.
  - `GameData.cs`: small helpers for the game's ds_maps and ds_lists, its instances, and values as JSON.
- Still GML: the loot sync and the area units, which go through every ground item or NPC every few frames (off-screen
  ones too, which StoneForge can't list from C# yet); the world tick and anything setting an alarm (`alarm[n]` wasn't
  reachable from C#: 0.18.1); the debug dump; and moving another player's unit through the game's grids, which the area units
  GML uses too.

### 0.17.1

- A client's arrows and bolts no longer lose their ammo. An arrow drops it by its target, far from the shooter, and
  the loot sync only counted loot turning up next to the client as the client's own drop - so the client removed it
  as loot the host doesn't have. An arrow's landing spot now counts too, for a moment after it lands
  (MpLootShotSpot), and so does an item the client throws (marked its drop while it's in the air).

### 0.17.0

Protocol 19. Contracts are shared, ported from the GML version's contract sync (ContractSync, ContractPacket).
- The host's contracts: a client in the host's world makes none of its own (they're made at random over time in
  every game) and has the host's. A client coming into the host's world gets a full copy.
- Twice a second every contract (every kind, and every one handed out) is compared with what was last sent, and the
  changed ones go to the others: taken, progress, targets, completed, handed in. They're copied in place, so the
  journal, the diary and the contract's dungeon see the change, and a taken contract goes into the others' journal.
- Deadlines count on the host's clock alone while playing together, paused while any player is at the contract's
  dungeon. A contract failed on the host's clock fails for everyone: listed as failed, the morale hit, its quest items
  gone.
- A village's contract counts (out, completed) are shared calls, and a dungeon's contract values now come with the
  dungeon (0.15.0 held them back): a contract's index is the same in every game.

### 0.16.0

Protocol 18. One story for everyone in a world, ported from the GML version's quest sync (QuestSync).
- Shared calls: every call to a script that changes the shared story goes to the others, who make the same call, so
  their quest book, journal and reputation log update the game's own way (SharedCallPacket). That's the ten quest
  scripts (start, progress, complete, failed, fields, timestamps, discard, complete-until, next target, dismiss),
  settlement reputation (its tile filled in where the caller stands), dialogue flags (what's been said, offered,
  settled), a location's flags, and a faction's crime record (status, penalty, attacks, state, time). Calls made
  while applying one aren't sent back.
- Shared quest items: both games run the quest triggers every turn and every hour, and they check *our* character
  for the lost plane, the black tablet's key and relic, 1000 gold (the abbey contract) or the thief's wine. The
  player without it wound the quest back. While the triggers run, "has it" now means anyone in the world has it
  (QuestItemsPacket: who passes which check, sent as soon as it changes).
- Not yet: the Gwynel house cutscene steps the GML version also shared, and contracts.

### 0.15.0

Protocol 17. Dungeons are shared whole.
- A dungeon's values on its world-map tile all go to the others now, not just its four layout values: its saved floor
  graphs (a floor one game has built is rebuilt from that exact layout in the others), which rooms dropped what,
  whether its boss is alive, whether it's open, its reset timer, the trapgate's cage, its mob levels, its size, tier
  and faction. Its contract (whether it has one, its NPC and boss, complete) stays in each game until contracts are
  shared: the contract is an index into each game's own list.
- They go out when one is set, and whenever a player leaves a location (a dungeon floor's graph is filled in place,
  with nothing to hook). The reset timer, which counts down every hour on every dungeon alike, only goes with other
  changes.

### 0.14.2

- Tidied up: each feature has its own folder under `Features` (Players, Effects, Menu, Areas, Clock, Join, Saves,
  World, Loot, Debug) and namespace (`StoneshardMP.Features.<Folder>`), and the packets are grouped the same way under
  `Net\Packets`. Nothing changes in game.

### 0.14.1

Protocol 16.
- Dropped items fly for everyone. Loot goes out the moment it appears, with its throw if it's still in the air (where it
  is, the tile it's landing on, its speed and gravity), and the other game flies its copy along the same arc to the
  same tile, with the landing sound and dust. Before, it only went out once it had landed, and popped in.
- A client's own drop no longer vanishes and comes back. It goes to the host in the air, with a token; the host throws
  its copy along the same arc, and when that comes back with the token the client's own item becomes the shared one.
  One the host doesn't bring back within 5 seconds is removed.
- Loot is checked every 4 frames instead of 12, so throws are caught early in their arc.

### 0.14.0

Protocol 15. Live ground loot, ported from the GML version's loot sync (LootSync, LootPacket).
- Where the host and clients are together, the host's ground loot is the real one. A client arriving gets the host's
  list: what it has from the same save is matched up, the rest made from the host's data, and anything else removed.
  After that the host sends what changes every few frames: new loot once it has landed (drops, kills), and loot that
  left (picked up by anyone).
- A client's pickups go to the host, which removes the item too. A client's drops go to the host, which makes them;
  they come back as the one shared item, where it landed on the host. A drop is only counted as one when it turns up
  next to the client right after the game's "dropped" line, so the game swapping an item for a new one (food going
  off) can't multiply items. The host takes at most 10 drops a second from a client, against a runaway loop.
- A player's place now includes the world-map cell. Neighbouring areas of the world map are built in the same room,
  so players (and NPCs, and loot) in two different areas were treated as together.

### 0.13.2

- A multiplayer world's saves are named for who plays in it rather than for the host's character: the Load Game
  screen's header for its character folder reads e.g. "FailMelon, Friend (1)" - the host, then every player whose
  character the host keeps. A world becomes a multiplayer one the first time it's saved while hosting, and stays one;
  its names are brought up to date with each save. (The GML version's scr_mp_slot_players_set / scr_mp_slot_title.)

### 0.13.1

- A client is only in the game for the others once it plays the host's world. While it makes its character it's on
  a copy of the host's map, where the same rooms are somewhere else, so the host used to see it standing beside them
  in the tavern intro. Until then it sends no position (the others have no Player object for it).
- The same goes for the world clock: a client making its character no longer gives the host a world turn per action,
  and doesn't take the host's clock in its intro.

### 0.13.0

Protocol 14. The shared world is back, ported from the GML version's world sync (WorldSync, WorldDataPacket).
- **Same layout:** an area's layout seeds (first visit or respawn) and a dungeon's floors (layout, and which floors
  are special) come from the world seed instead of a random roll. Every game in the same world builds the same area
  and dungeon. This applies solo too, so areas a host builds before anyone joins are the ones others will find. A
  dungeon floor the game rejects and rebuilds counts the attempt, so it doesn't rebuild the same floor forever.
- **Same contents:** when a game leaves a location it was running, its saved state (what's dead, taken or opened,
  and its flags) goes to the others, who keep it as their own save of that location. A client that followed the host
  in a location doesn't send it: the host's copy is the real one. Tiles whose seeds or dungeon layout are set go out
  too.
- **Catching up:** a client coming into the host's world asks for the host's copy of the world, every location's
  state and every world-map tile, sent a few a frame.
- **Weather:** a client in the host's world takes its weather and fog instead of rolling its own.

### 0.12.4

- Ghosts are now called players. Another player on your screen is the game object `o_stoneshardmp__player`
  (was `o_stoneshardmp__ghost`), written as `Players\Player` and `Players\PlayerManager` in C#, with GML helpers
  `MpPlayerBuild`, `MpPlayerInitialize` and `MpPlayerUnitMove`. Nothing else changes, and the protocol stays 13.

### 0.12.3

- A client making its character when the host leaves now goes back to the main menu too. The game refuses a room
  change mid-conversation or mid-cutscene (the new character's intro at Osbrook's tavern is both), and the return
  was tried only once. It's now retried each frame until it's under way, and no save is kept meanwhile.

### 0.12.2

Protocol 13.
- The host leaving its world sends everyone in it back to the main menu (HostLeft). There they wait, and join again
  automatically when the host plays again (Continue, Load Game or New Game).
- The host's Save & Exit first has everyone in its world save (SaveRequest). Each client autosaves, which sends its
  character to the host. The exit waits for all of them, up to 15 seconds, so the host's exit save holds everyone's
  latest characters (the GML version's SAVE_ALL / exit hold).
- A host that's gone (Stop Hosting, connection lost) also sends a client in its world back to the main menu.
- Characters a host receives while out of a world (made alongside its new game, or saved as it left) now replace
  what its next world has for those players, since they're newer.

### 0.12.1

- A client no longer waits for the host to finish making its own character. As soon as the host's new game has its
  world seed (before its character creation), the waiting players start making theirs alongside it, on the host's
  world map. A character finished before the host's world is ready is kept on the host and let in once it is (the
  GML version's `mp_host_new_game`).
- The host's New Game, while hosting, goes straight into the Adventure with permadeath off. The prologue is a world
  of its own, so it's left out.

### 0.12.0

Protocol 12. The host keeps everyone's save, as the GML version did: a client has no save data of its own.

- Joining: once in, a client asks to join the host's world. It waits on the main menu (the status says so) until the
  host presses Continue, New Game or Load Game; the client's Joined screen has no Play buttons of its own.
- The host's world has a character for that player: the host sends its world (its save data, with that character in
  it and the host's dialogue progress added) and the client loads it, as a save of its own would load.
- It hasn't: the client goes straight into a new game (Adventure: the class picked at Verren) on the host's world map
  (its seed). Its first save sends the character to the host and asks again, and it's let into the host's world.
- A client's saves are never written to its own disk: each sends its character to the host, which keeps it in its
  world's save data (`mpPlayersDataMap`) - saved with the host's saves.
- Saves and characters travel gzipped (JSON), in new packets: JoinRequest, JoinReply, JoinWorld, JoinCharacter.
- Not yet (from the GML version): the host asking everyone to save when it saves or leaves, re-joining when the host
  loads another save, and blocking a host's world from being loaded on its own.

### 0.11.24

- Diagnostics: Ctrl+Shift+D writes everything within 12 cells of the player (object, cell, sprite and frame,
  visibility, depth; units' state, animation flags, AI and sync binding) with the game's role, place and clock to
  `%LOCALAPPDATA%\StoneShard\stoneshardmp-dump-<role>-<process>.txt`, one file per game, to compare the host's and a
  client's view of an area.

### 0.11.23

- Fixed the host's game stopping on the first action a client sent: the world-turn readiness check (MpWorldTickReady)
  called the game's cutscene check with no instance, and that check reads `object_index`. It now runs as the player.

### 0.11.22

- World turns are back on (the on-move model): a player's completed action is one world turn for everyone.
  - A client's action reaches the host, which runs one idle world turn: time, upkeep and its units' turn loop. The
    area's units move, and AreaUnits streams them to the client.
  - The host's actions, and each turn it runs, send its clock to the clients.
  - A client in the host's area only takes the clock. Its units there are the host's, so it runs no AI of its own;
    that was what crashed the client in 0.11.14.
  - A client elsewhere still gives its own area one idle turn per other player's action. Turns are queued and run
    from the mod's tick once the world is ready, never inside the network handler.

### 0.11.21

- Multiplayer is a set of main menu screens again, as in the GML version, built on StoneForge's main menu layout:
  - **Multiplayer** (after Play): Host Game, Join Game, Players & Settings, Back.
  - **Hosting:** the game's Continue / New Game / Load Game, Players & Settings, Stop Hosting.
  - **Joined:** the same, with Leave Game.
  - The screen follows the session: it changes on its own when hosting starts or stops, a join succeeds, or the
    connection drops. The session's status shows under the menu meanwhile.
- **Join Game** opens a dialog in the game's confirm-panel frame: the host's address (remembered), Join and Cancel.
  Enter joins.
- **Players & Settings** (the journal-framed window, its frame put back): who's in the game; your name, the join
  address and name tags. Hosting and joining moved out of it into the menu.

### 0.11.20

Protocol 11.
- Synchronize the NPC animation-mode flag and render sprite used by scr_npc_change_animation, including work poses.

### 0.11.18

- Moved player-state binary serialization into StatePacket. Gameplay models no longer depend on the span wire reader/writer; all other packet payload serializers already reside under Net/Packets.
- Preserved protocol 9 and the existing field order.

### 0.11.17

Protocol 9.
- Migrated all packet contracts, connection data, and feature handlers to SpanReadWrite packet structs under Net/Packets.
- Removed PacketType and anonymous serialization callbacks; added envelope validation and bounded string reads.
- Verified round trips, Unicode, large rosters, truncated input, and trailing-data rejection.

### 0.11.16

- The Multiplayer window uses the game's journal frame, laid out as the journal: the tabs in its left pane, the page and the Host / Join / Leave / Close buttons in its right. It's built from StoneForge's reworked windows (any frame sprite).

### 0.11.15

- Disabled the on-move action-forwarding experiment after its receiving WorldTick handler closed the client; added full handler error logging for the next isolated diagnosis.

### 0.11.14

Protocol 7.
- Replaced the disabled 600 ms experiment with legacy-style on-move world turns: each player's completed action advances the other players by one safe idle turn through the host.

### 0.11.13

- Temporarily disabled the fixed-world-clock experiment after it caused a freeze. Ghost/effect/state networking remains enabled.

### 0.11.12

- Matched the legacy tick gate's input-phase behavior: queued clicks and context actions remain responsive while waiting for a world tick.

### 0.11.11

Protocol 6.
- Added the first host-authoritative, fixed out-of-combat world clock: safe idle turns and time are now driven by the host every 600 ms.

### 0.11.10

Protocol 5.

- Inspection now uses the other player's actual resistance values, instead of the Caravan Dummy template. Ghosts are
  labelled as players in that panel.

### 0.11.9

Protocol 4.

- Inspection now shows another player's live health and energy percentages. The ghost remains locally invulnerable
  until combat synchronization is implemented, so those values cannot be changed by unsynchronized local damage.

### 0.11.8

- Ghosts now start from the game's complete Caravan Dummy parameter record, allowing the normal inspection panel to
  read their type, stats, resistances, and other expected unit fields safely.

### 0.11.7

- Passive ghosts now have a remote-player name and description for inspection, and bypass the inherited enemy
  loot/corpse cleanup path when removed.

### 0.11.6

- Removed the training-dummy marker from ghosts as well. They are ordinary passive enemy-unit proxies, with no
  dummy-specific context actions.

### 0.11.5

- Ghosts now inherit directly from the enemy unit rather than the training dummy. This removes the dummy-only
  **Change protection class** right-click action while retaining passive unit collision and targeting behavior.

### 0.11.4

- Fixed passive ghosts inheriting the dummy unit's delayed stat-calculation alarm. Ghosts do not have (or need)
  combat stat templates until combat synchronization is implemented.

### 0.11.3

- Ghosts are now passive dummy units in their remote player's occupied cell. They use normal unit collision and
  targeting data, but stay neutral, invulnerable, and invisible to enemy AI until combat forwarding is added.

### 0.11.2

Protocol 3.

- Visual effects now travel with the player who made them: hit flashes, spells, projectiles, and effects such as
  burning or stun are drawn on the other games. They are sprite-only echoes, so they cannot deal damage, create
  further effects, or otherwise run game logic remotely.

### 0.11.1

Protocol 2.

- Ghosts: the other players in the same place as you (room, and dungeon floor) are drawn where and as their game draws
  them:
  - their look, built by the game's own compositor from their layers;
  - their shadow, and a name tag (Name tags setting);
  - smoothed for small moves and snapped for jumps.
- Your state goes out every other frame (unreliable, latest only), your look whenever it changes and to newcomers.
- The ghost is a StoneForge mod object (`o_stoneshardmp__ghost`). For now it's only a picture: it becomes a unit, which
  attacks and spells can target, with combat.
- Ctrl+Shift+G, the mirror test: your own ghost two cells to the right, to try it with one game.
- The mod has GML functions (`GML\`): reading the player's state and look and building a ghost's sprites need GML
  arrays, which StoneForge's C# can't hold yet.

### 0.11.0

The StoneForge port begins: StoneshardMP is now a C# mod on StoneForge, networked with LiteNetLib (UDP) in place of
GameMaker's TCP sockets. It's protocol 1, so it can't play with the GML versions (0.10.x).

- Session:
  - The host runs a LiteNetLib server (UDP port 7777 by default). Players get slots: the host is 1 and the others
    2-8, up to the host's Max players.
  - The host relays packets between players and stamps each with its real sender.
  - The handshake (protocol, version, name) rides on the connection request. A version mismatch or a full game is
    refused with the reason shown to the joining player.
  - LiteNetLib keeps the connection alive on its own thread, with a 30 s timeout, so a game frozen while loading
    doesn't drop out.
- Multiplayer window on the main menu:
  - Host, Join and Leave.
  - Your name (empty: your Steam name) and the host's address.
  - The players in the game, with their versions and ping.
- Settings on the mod's page in the Mods window: name, join address, port, max players.
- Needs StoneForge with trusted mods. It's a trusted mod, so it needs allowing in the Mods window.
