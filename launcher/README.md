# Street Fighter X Tekken — Recomp Launcher

Distribution launcher for the Street Fighter X Tekken Xbox 360 recomp
(ReXGlue). One self-contained `SFxTLauncher.exe` carries the recompiled
game binaries inside it; nobody downloads or ships game content.

## First run

1. Put `extract-xiso.exe` (free, open-source Xbox 360 image extractor,
   https://github.com/XboxDev/extract-xiso) next to `SFxTLauncher.exe`.
2. Click **Add disc…** and pick your own Street Fighter X Tekken disc
   image (`.iso`). The launcher unpacks the game files the recomp needs
   (`default.xex`, `archive/`, `stream/`, …) into the `game\` folder.
   Your disc, your files — nothing copyrighted is distributed here.
3. Press **▶ Launch**. On the very first launch the launcher also writes
   out the embedded game binaries (`SFxT.exe`, `rexruntime.dll`,
   `rexgpu-xenos.dll`) into `game\`.

You can also skip step 2 and point the launcher at an already-extracted
game folder by editing `game.gameDataRoot` in `launcher.json`.

## Headless extraction (optional)

```
SFxTLauncher.exe --extract-iso <profile folder> <image.iso> [destDir]
```

## Player name (the "usernames" fix)

The launcher's **Profile** tab writes your player name into the cvar
config (`SFxT.toml`) as:

```toml
profile_name = "YourName"
online_username = "YourHandle"
```

This is the same *synthetic profile* approach used by the Puzzle Fighter
recomp: the launcher persists a stable name in a cvar, and the recomp
surfaces it wherever the game would have shown the Xbox Live gamertag
(player cards, save labels, online lobbies).

The cvar names are configured per game in `launcher.json`
(`game.usernameCvar` / `game.onlineUsernameCvar`), so the same launcher
works for recomps that use different cvar names. Runtimes that don't
implement the cvar yet simply ignore it — the setting is harmless.

For this SFxT recomp the cvar is written and ready; surfacing it inside
the guest (the game reads gamertags through `XamUserReadProfileData`)
is the remaining piece and follows the Puzzle Fighter recomp's
`docs/synthetic_profile.md` pattern.

## Notes

- **Fullscreen is intentionally forced off** at launch (`--no-fullscreen`).
  On this SDK build the fullscreen present path runs far slower than
  borderless/windowed, and the cvar config loader mis-restores flag cvars
  (a saved `fullscreen = false` still launches fullscreen). Reported
  upstream; revisit when the loader is fixed.
- Settings live in `game\SFxT.toml` — the same cvar file the in-game F4
  dialog reads and writes, so the launcher and the game stay in sync.
- Mods: drop mod folders into `mods\`; enabled mods are staged over the
  game data on launch.

## Layout

```
SFxT_launcher\
  launcher.json     SFxT profile (per-game settings schema)
  banner.jpg        header image
  extract-xiso.exe  (you provide) disc image extractor
  game\             created at runtime: binaries + extracted disc content
  build.ps1         builds publish\SFxTLauncher.exe (needs .NET SDK 10+)
  src\RecompLauncher\  WPF app source (game payloads embed from payload\)
```

## Building

```
.\build.ps1            # release -> publish\SFxTLauncher.exe (single file)
```

The game payload (`src\RecompLauncher\payload\`) is embedded at build time.
