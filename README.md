# StoneshardMP

Current port milestone: synchronized remote player state and sprite-only effects. The action-forwarding experiment is disabled while its receiving-side failure is isolated. Combat, shared interactions, area ownership, and shared time are still separate milestones.

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
- **Players & Settings** lists who's in the game and has your name, the join address and name tags. The port and
  player limit are on StoneshardMP's page in the Mods window.

To test with two games on one PC, add `steam_appid.txt` (containing `625960`) to the game folder, then start the
game twice.

## Developing

- `StoneshardMP.csproj` is for editing in an IDE. The game compiles the `.cs` files itself.
- To build it against an installed StoneForge, set `STONESHARD_DIR` to the game folder (or run it from the game's
  `mods` folder).
- `Net\Session.cs` is the session: a star with the host as slot 0, which relays clients' packets.
- `Net\Packets` contains one struct per packet, each with its numeric ID and span-based Read/Write contract. `PacketCodec` validates and decodes envelopes; `Session.On<T>` and `Session.Send(packet)` dispatch typed messages. Raise `Session.Protocol` whenever the wire format changes.

## Licences

LiteNetLib is MIT-licensed. See `lib\LICENSE-LiteNetLib.txt`.
