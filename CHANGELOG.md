# StoneshardMP changes

## 0.15.0

Protocol 17. Dungeons are shared whole.
- A dungeon's values on its world-map tile all go to the others now, not just its four layout values: its saved floor
  graphs (a floor one game has built is rebuilt from that exact layout in the others), which rooms dropped what,
  whether its boss is alive, whether it's open, its reset timer, the trapgate's cage, its mob levels, its size, tier
  and faction. Its contract (whether it has one, its NPC and boss, complete) stays in each game until contracts are
  shared: the contract is an index into each game's own list.
- They go out when one is set, and whenever a player leaves a location (a dungeon floor's graph is filled in place,
  with nothing to hook). The reset timer, which counts down every hour on every dungeon alike, only goes with other
  changes.

## 0.14.2

- Tidied up: each feature has its own folder under `Features` (Players, Effects, Menu, Areas, Clock, Join, Saves,
  World, Loot, Debug) and namespace (`StoneshardMP.Features.<Folder>`), and the packets are grouped the same way under
  `Net\Packets`. Nothing changes in game.

## 0.14.1

Protocol 16.
- Dropped items fly for everyone. Loot goes out the moment it appears, with its throw if it's still in the air (where it
  is, the tile it's landing on, its speed and gravity), and the other game flies its copy along the same arc to the
  same tile, with the landing sound and dust. Before, it only went out once it had landed, and popped in.
- A client's own drop no longer vanishes and comes back. It goes to the host in the air, with a token; the host throws
  its copy along the same arc, and when that comes back with the token the client's own item becomes the shared one.
  One the host doesn't bring back within 5 seconds is removed.
- Loot is checked every 4 frames instead of 12, so throws are caught early in their arc.

## 0.14.0

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

## 0.13.2

- A multiplayer world's saves are named for who plays in it rather than for the host's character: the Load Game
  screen's header for its character folder reads e.g. "FailMelon, Friend (1)" - the host, then every player whose
  character the host keeps. A world becomes a multiplayer one the first time it's saved while hosting, and stays one;
  its names are brought up to date with each save. (The GML version's scr_mp_slot_players_set / scr_mp_slot_title.)

## 0.13.1

- A client is only in the game for the others once it plays the host's world. While it makes its character it's on
  a copy of the host's map, where the same rooms are somewhere else, so the host used to see it standing beside them
  in the tavern intro. Until then it sends no position (the others have no Player object for it).
- The same goes for the world clock: a client making its character no longer gives the host a world turn per action,
  and doesn't take the host's clock in its intro.

## 0.13.0

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

## 0.12.4

- Ghosts are now called players. Another player on your screen is the game object `o_stoneshardmp__player`
  (was `o_stoneshardmp__ghost`), written as `Players\Player` and `Players\PlayerManager` in C#, with GML helpers
  `MpPlayerBuild`, `MpPlayerInitialize` and `MpPlayerUnitMove`. Nothing else changes, and the protocol stays 13.

## 0.12.3

- A client making its character when the host leaves now goes back to the main menu too. The game refuses a room
  change mid-conversation or mid-cutscene (the new character's intro at Osbrook's tavern is both), and the return
  was tried only once. It's now retried each frame until it's under way, and no save is kept meanwhile.

## 0.12.2

Protocol 13.
- The host leaving its world sends everyone in it back to the main menu (HostLeft). There they wait, and join again
  automatically when the host plays again (Continue, Load Game or New Game).
- The host's Save & Exit first has everyone in its world save (SaveRequest). Each client autosaves, which sends its
  character to the host. The exit waits for all of them, up to 15 seconds, so the host's exit save holds everyone's
  latest characters (the GML version's SAVE_ALL / exit hold).
- A host that's gone (Stop Hosting, connection lost) also sends a client in its world back to the main menu.
- Characters a host receives while out of a world (made alongside its new game, or saved as it left) now replace
  what its next world has for those players, since they're newer.

## 0.12.1

- A client no longer waits for the host to finish making its own character. As soon as the host's new game has its
  world seed (before its character creation), the waiting players start making theirs alongside it, on the host's
  world map. A character finished before the host's world is ready is kept on the host and let in once it is (the
  GML version's `mp_host_new_game`).
- The host's New Game, while hosting, goes straight into the Adventure with permadeath off. The prologue is a world
  of its own, so it's left out.

## 0.12.0

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

## 0.11.24

- Diagnostics: Ctrl+Shift+D writes everything within 12 cells of the player (object, cell, sprite and frame,
  visibility, depth; units' state, animation flags, AI and sync binding) with the game's role, place and clock to
  `%LOCALAPPDATA%\StoneShard\stoneshardmp-dump-<role>-<process>.txt`, one file per game, to compare the host's and a
  client's view of an area.

## 0.11.23

- Fixed the host's game stopping on the first action a client sent: the world-turn readiness check (MpWorldTickReady)
  called the game's cutscene check with no instance, and that check reads `object_index`. It now runs as the player.

## 0.11.22

- World turns are back on (the on-move model): a player's completed action is one world turn for everyone.
  - A client's action reaches the host, which runs one idle world turn: time, upkeep and its units' turn loop. The
    area's units move, and AreaUnits streams them to the client.
  - The host's actions, and each turn it runs, send its clock to the clients.
  - A client in the host's area only takes the clock. Its units there are the host's, so it runs no AI of its own;
    that was what crashed the client in 0.11.14.
  - A client elsewhere still gives its own area one idle turn per other player's action. Turns are queued and run
    from the mod's tick once the world is ready, never inside the network handler.

## 0.11.21

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

## 0.11.20

Protocol 11.
- Synchronize the NPC animation-mode flag and render sprite used by scr_npc_change_animation, including work poses.

## 0.11.18

- Moved player-state binary serialization into StatePacket. Gameplay models no longer depend on the span wire reader/writer; all other packet payload serializers already reside under Net/Packets.
- Preserved protocol 9 and the existing field order.

## 0.11.17

Protocol 9.
- Migrated all packet contracts, connection data, and feature handlers to SpanReadWrite packet structs under Net/Packets.
- Removed PacketType and anonymous serialization callbacks; added envelope validation and bounded string reads.
- Verified round trips, Unicode, large rosters, truncated input, and trailing-data rejection.

## 0.11.16

- The Multiplayer window uses the game's journal frame, laid out as the journal: the tabs in its left pane, the page and the Host / Join / Leave / Close buttons in its right. It's built from StoneForge's reworked windows (any frame sprite).

## 0.11.15

- Disabled the on-move action-forwarding experiment after its receiving WorldTick handler closed the client; added full handler error logging for the next isolated diagnosis.

## 0.11.14

Protocol 7.
- Replaced the disabled 600 ms experiment with legacy-style on-move world turns: each player's completed action advances the other players by one safe idle turn through the host.

## 0.11.13

- Temporarily disabled the fixed-world-clock experiment after it caused a freeze. Ghost/effect/state networking remains enabled.

## 0.11.12

- Matched the legacy tick gate's input-phase behavior: queued clicks and context actions remain responsive while waiting for a world tick.

## 0.11.11

Protocol 6.
- Added the first host-authoritative, fixed out-of-combat world clock: safe idle turns and time are now driven by the host every 600 ms.

## 0.11.10

Protocol 5.

- Inspection now uses the other player's actual resistance values, instead of the Caravan Dummy template. Ghosts are
  labelled as players in that panel.

## 0.11.9

Protocol 4.

- Inspection now shows another player's live health and energy percentages. The ghost remains locally invulnerable
  until combat synchronization is implemented, so those values cannot be changed by unsynchronized local damage.

## 0.11.8

- Ghosts now start from the game's complete Caravan Dummy parameter record, allowing the normal inspection panel to
  read their type, stats, resistances, and other expected unit fields safely.

## 0.11.7

- Passive ghosts now have a remote-player name and description for inspection, and bypass the inherited enemy
  loot/corpse cleanup path when removed.

## 0.11.6

- Removed the training-dummy marker from ghosts as well. They are ordinary passive enemy-unit proxies, with no
  dummy-specific context actions.

## 0.11.5

- Ghosts now inherit directly from the enemy unit rather than the training dummy. This removes the dummy-only
  **Change protection class** right-click action while retaining passive unit collision and targeting behavior.

## 0.11.4

- Fixed passive ghosts inheriting the dummy unit's delayed stat-calculation alarm. Ghosts do not have (or need)
  combat stat templates until combat synchronization is implemented.

## 0.11.3

- Ghosts are now passive dummy units in their remote player's occupied cell. They use normal unit collision and
  targeting data, but stay neutral, invulnerable, and invisible to enemy AI until combat forwarding is added.

## 0.11.2

Protocol 3.

- Visual effects now travel with the player who made them: hit flashes, spells, projectiles, and effects such as
  burning or stun are drawn on the other games. They are sprite-only echoes, so they cannot deal damage, create
  further effects, or otherwise run game logic remotely.

## 0.11.1

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

## 0.11.0

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
