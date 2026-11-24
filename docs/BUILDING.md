# Building OoTMM PC

This builds the launcher and relay server from this repo. The games and the seed generator live in their own repos and have their own guides.

## What you need

- Windows 10 or 11, 64-bit
- [Git](https://git-scm.com/download/win)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Visual Studio 2022 or newer with "Desktop development with C++" (the games need it)

## Launcher and relay server

Open PowerShell wherever you want the code and paste this:

```powershell
git clone https://github.com/OotMM-PC/OotMMReshipified.git
cd OotMMReshipified
dotnet publish OotMM.Launcher -p:PublishProfile=win-x64
dotnet publish OotMM.Server -p:PublishProfile=win-x64
```

Both end up in `artifacts\win-x64`. The first build takes a while because it pulls down Qt.

To run the tests:

```powershell
dotnet test
```

## The games

Build our forks, not the originals. They're on the right branch by default, and each one has a `docs/BUILDING.md` that walks you through it:

- OoT: [OotMM-PC/Shipwright](https://github.com/OotMM-PC/Shipwright)
- MM: [OotMM-PC/2ship2harkinian](https://github.com/OotMM-PC/2ship2harkinian)

Then either put them in folders named `SoH` and `2ship` next to the folder with `OotMM.Launcher.exe`, or open the launcher and click Browse on each game to point it at `soh.exe` and `2ship.exe`.

## Seeds

The generator is [OotMM-PC/OoTMM](https://github.com/OotMM-PC/OoTMM). Its README covers building it and making PC seeds.
