# Street Fighter X Tekken — Xbox 360 Recomp

![Street Fighter X Tekken Recomp](SFXT%20Recomp.jpg)

A native PC port of **Street Fighter X Tekken** built with the
[ReXGlue](https://github.com/rexglue/rexglue-sdk) static recompilation
toolkit — no emulator, no JIT: the game's PowerPC code is converted once,
at build time, into native C++ that runs directly on your machine
(D3D12 renderer).

## Status

Playable end to end: menus, character select, language switching, full
fights (intros, rounds, K.O., victory sequences) — tested with a DualSense
on Windows 11 (D3D12, windowed).

## Legal

**No game content is distributed in this repository.** The recompilation
output, game assets, and binaries are generated locally from **your own**
Xbox 360 disc. You must own the game.

## Quick start (prebuilt launcher)

1. Download `SFxTLauncher.exe` from the
   [Releases](../../releases) page, with `launcher.json` and
   `banner.jpg` beside it.
2. Put `extract-xiso.exe`
   ([free, open source](https://github.com/XboxDev/extract-xiso)) next to
   the launcher.
3. **Add disc…** → pick your own Street Fighter X Tekken `.iso`. The
   launcher extracts the game files into `game\`.
4. **▶ Launch** — the embedded recompiled binaries are written out on
   first launch and the game starts.

The launcher also exposes graphics settings, a player-name profile, and a
mods manager.

## Building from source

### 1. The recomp (`recomp/`)

Requires the [ReXGlue SDK](https://github.com/rexglue/rexglue-sdk)
(v0.10.0, install its `win-amd64` prefix), CMake 3.25+, Ninja, and a
clang toolchain targeting MSVC.

```bat
rexglue init --project-name SFxT --xex-path <your dump>\default.xex ^
    --game-root <your dump> --project-root sfxt-recomp
cd sfxt-recomp
rexglue codegen SFxT_manifest.toml
cmake -S . -B build -G Ninja -DCMAKE_BUILD_TYPE=Release ^
    -DCMAKE_PREFIX_PATH=<rexglue sdk prefix>
cmake --build build
```

`includes/functions.toml` contains the manually-discovered function
entries (virtual-call thunks and indirect targets the static analyzer
cannot see) gathered while bringing the game up — keep it; new runtime
fatals of the form *"Call to invalid or unregistered function at
0x8XXXXXXX"* are fixed by adding the address there and re-running codegen.

### 2. The launcher (`launcher/`)

Requires the .NET SDK (10+):

```powershell
cd launcher
.\build.ps1
```

The build embeds whatever you place in
`launcher\src\RecompLauncher\payload\` — drop in your freshly built
`SFxT.exe`, `rexruntime.dll` and `rexgpu-xenos.dll` — and produces a
single self-contained `publish\SFxTLauncher.exe`.

## Repository layout

```
recomp/     ReXGlue project: manifest, function declarations, app stubs
launcher/   WPF launcher: embedded payload, Add-disc extraction, settings
```

## Credits

- [ReXGlue SDK](https://github.com/rexglue/rexglue-sdk) — Xbox 360
  recompilation runtime and toolkit
- [extract-xiso](https://github.com/XboxDev/extract-xiso) — disc image
  extraction
- Capcom / Namco Bandai — Street Fighter X Tekken
