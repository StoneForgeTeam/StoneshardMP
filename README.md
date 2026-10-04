# StoneshardMP

Co-op multiplayer for Stoneshard, as a [StoneForge](https://github.com/StoneForgeTeam) mod. The host and up to 7 other
players share one world. Networking runs over [LiteNetLib](https://github.com/RevenantX/LiteNetLib) (UDP).

This is the port of the GML version (built with MSL) to C#. It's being rebuilt feature by feature. Players can already
see each other's character, equipment, movement, hit flashes, spells, projectiles, and character effects. See
CHANGELOG.md for what works so far.

## Installing

1. Install StoneForge.
2. Put this folder in `Stoneshard\mods\StoneshardMP`.
3. Start the game.
4. Open **Mods** on the main menu, go to StoneshardMP and tick **Enabled**. StoneshardMP is a *trusted* mod: it needs
   the network and brings its own DLL (`lib\LiteNetLib.dll`). StoneForge only runs it once you've allowed it.

## Playing

- **Multiplayer** (on the main menu, after Play) opens the Multiplayer screen: **Host Game**, **Join Game**,
  **Players & Settings**, **Back**.
- **Host Game** listens on the port (UDP 7777 by default) and shows the game's Continue / New Game / Load Game, to
  start playing, and **Stop Hosting**. To be reached over the internet, the host forwards that port on their router.
- **Join Game** asks for the host's address. Once in, you join the host's world when the host is in it (Continue,
  New Game or Load Game): with your character if the host's world has it, or straight into making one (on the host's
  world map) if not. The host keeps everyone's save - nothing is saved on a client's PC.
- **One world:** areas and dungeons are built from the world seed, so everyone gets the same layout. When you leave
  an area you were running, what's in it (what's dead, taken or opened) goes to the others. A client coming into the
  host's world gets a copy of every area the host has, and takes the host's weather.
- **Ground loot:** where you're together, the host's loot is the real one. Items dropped, picked up or left by a
  kill show up for everyone there.
- **Quests:** one story for everyone. Quest steps, reputation, what's been said to whom, and crime records are shared,
  and a quest item counts as long as anyone has it.
- **Contracts:** the host's contracts are everyone's: taken, progressed and handed in for all, with deadlines on the
  host's clock.
- **Players & Settings** lists who's in the game and has your name, the join address and name tags. The port and
  player limit are on StoneshardMP's page in the Mods window.

To test with two games on one PC, add `steam_appid.txt` (containing `625960`) to the game folder, then start the
game twice.

## Developing

- `StoneshardMP.csproj` is for editing in an IDE. The game compiles the `.cs` files itself.
- To build it against an installed StoneForge, set `STONESHARD_DIR` to the game folder (or run it from the game's
  `mods` folder).
- `Net\Session.cs` is the session: a star with the host as slot 0, which relays clients' packets.
- `Features\` has one folder per feature (Players, Effects, Menu, Areas, Clock, Join, Saves, World, Loot, Debug),
  each in its own `StoneshardMP.Features.<Folder>` namespace. Its packets are in the same-named folder under
  `Net\Packets`. `MultiplayerMod.cs` wires them together. It's all C#: the game's data, saves, world map, clock,
  ground items and characters come through StoneForge's API (no GML of its own).
- `Net\Packets` contains one struct per packet, each with its numeric ID and span-based Read/Write contract. `PacketCodec` validates and decodes envelopes; `Session.On<T>` and `Session.Send(packet)` dispatch typed messages. Raise `Session.Protocol` whenever the wire format changes.

## Releasing

- Write each change under `## Unreleased` in `CHANGELOG.md` as it's made. Don't change `version` in `mod.json` by
  hand: releases set it.
- To release, tag the commit and push the tag: `git tag v0.2.0`, then `git push origin v0.2.0`. A tag with a suffix
  (`v0.2.0-beta.1`) makes a pre-release. The **Release** workflow then:
  1. sets `version` in `mod.json` and titles the `## Unreleased` section with the version (`build\Stamp-Version.ps1`);
  2. builds the mod to check it compiles;
  3. packages `StoneshardMP-<version>.zip`, a `StoneshardMP` folder to put in `Stoneshard\mods`
     (`build\Package.ps1`);
  4. publishes the GitHub release, with that CHANGELOG section as its notes;
  5. commits the version back to `main`, and moves the tag onto that commit if `main` hadn't moved on.
- The **Build** workflow checks every push to `main` and every pull request compiles, against StoneForge's `main` as
  it is now (its head). GitHub's runners have no Stoneshard, so `build\Get-StoneForge.ps1 -Version main` takes
  `StoneForge.API.dll` from StoneForge's rolling `main-latest` pre-release, which its Main build workflow packages
  from its `main` after every push (the script waits until that's been made from `main`'s head).
- A release builds against the StoneForge release `mod.json` names (`"stoneforge"`), the one players install. Raise
  `"stoneforge"` when the mod starts using a newer StoneForge's API, once that StoneForge is released.
- To package locally: `build\Stamp-Version.ps1 -Version 0.2.0`, then `build\Package.ps1 -Version 0.2.0` (the zip is
  in `artifacts\`).

## Licences

LiteNetLib is MIT-licensed. See `lib\LICENSE-LiteNetLib.txt`.
