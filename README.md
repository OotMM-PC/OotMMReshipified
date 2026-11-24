# OoTMM PC

An unofficial PC port of [OoTMM](https://github.com/OoTMM/OoTMM), the Ocarina of Time and Majora's Mask combo randomizer. It is not made by or affiliated with the OoTMM team or HarbourMasters.

The randomizer is OoTMM's. Seeds come from [our fork of their generator](https://github.com/OotMM-PC/OoTMM) that adds a PC seed export, and the games are our forks of [Ship of Harkinian](https://github.com/HarbourMasters/Shipwright) and [2 Ship 2 Harkinian](https://github.com/HarbourMasters/2ship2harkinian) with OoTMM's changes ported in. This repo is the launcher that ties them together. It starts the game you're in, switches to the other one when you cross over, and keeps your items, saves and tracker in sync. Play alone or with friends.

## Which OoTMM version

We're on [OoTMM v31.1](https://github.com/OoTMM/OoTMM/releases/tag/v31.1). Our generator is v31.1 plus the PC seed export, and the launcher only takes seeds made by it. Seeds from the official site or newer OoTMM versions won't load, and nothing from after v31.1 is in yet.

## What we added

These aren't part of OoTMM:

- See the other players in your world, with their custom models.
- PvP with shield blocks, knockdowns and elemental arrows, plus friendly fire and a damage setting.
- Death link that's off, per team or for everyone.
- Trap strength the host can tune.
- A tracker that follows your seed's logic, a Progress tab, and a map that fills in shuffled entrances as you find them.
- A mods and models picker for each game.
- Small comfort options like pot rolling, no cooldown on Fierce Deity beams, and not sliding while transforming.

## Getting started

1. Download the latest zip from [Releases](https://github.com/OotMM-PC/OotMMReshipified/releases) and extract it anywhere.
2. Make a seed with our [fork of the OoTMM generator](https://github.com/OotMM-PC/OoTMM). You get one `.ootmm.json` file per player.
3. Open `Launcher/OotMM.Launcher.exe`, pick your seed and press Start.

The first start takes a few minutes. Each game asks for your own copy of its ROM and builds its assets from it. If your seed uses Master Quest dungeons, you'll also need an OoT Master Quest ROM.

## Playing with friends

One person turns on "Host the relay server on this machine". Everyone else enters the host's address under "Server address". The default port is 42801.

Playing over the internet? The host needs to port forward 42801 (TCP) to their PC, then send everyone their public IP. That's the only port it uses. If you changed the port, forward that one instead, and if Windows asks about the firewall, hit Allow.

The host sets the rules for everyone: PvP, friendly fire, death link and trap strength.

## Releases

Each release has the launcher, the relay server and both games, ready to run on 64-bit Windows 10 or 11.

## For developers

Want to build it yourself? [Building guide here](docs/BUILDING.md).

## License

This repo is under the [GPL v3](LICENSE). The projects it builds on keep their own licenses.

## Credits

The randomizer itself, its logic and its changes to the games come from [OoTMM](https://github.com/OoTMM/OoTMM). The games run on Ship of Harkinian, 2 Ship 2 Harkinian and libultraship.

| Project | Original | Our version |
| --- | --- | --- |
| OoTMM generator | [OoTMM/OoTMM](https://github.com/OoTMM/OoTMM) | [OotMM-PC/OoTMM](https://github.com/OotMM-PC/OoTMM) |
| Ship of Harkinian (OoT) | [HarbourMasters/Shipwright](https://github.com/HarbourMasters/Shipwright) | [OotMM-PC/Shipwright](https://github.com/OotMM-PC/Shipwright) |
| 2 Ship 2 Harkinian (MM) | [HarbourMasters/2ship2harkinian](https://github.com/HarbourMasters/2ship2harkinian) | [OotMM-PC/2ship2harkinian](https://github.com/OotMM-PC/2ship2harkinian) |
| libultraship | [Kenix3/libultraship](https://github.com/Kenix3/libultraship) | [OotMM-PC/libultraship](https://github.com/OotMM-PC/libultraship) |
