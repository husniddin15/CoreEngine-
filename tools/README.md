# tools

Scripts for the bundled Arduino toolchain (docs/05-arduino-emulation-spec.md §8, ADR-0003).

| Script | What it does |
|---|---|
| `fetch-toolchain.ps1` | Downloads arduino-cli 1.5.1 from its GitHub release (SHA-256 verified) and installs the `arduino:avr` 1.8.8 core with avr-gcc 7.3.0 into `tools/arduino/`. Run once per machine. |
| `compile-sketch.ps1` | Compiles one sketch folder with the bundled toolchain and prints the time taken. |
| `build-golden.ps1` | Recompiles the golden test sketches in `core/CoreEngine.Sim.Tests/Golden/Sketches` and updates the committed `.hex` files. |

```powershell
powershell -ExecutionPolicy Bypass -File tools\fetch-toolchain.ps1
powershell -ExecutionPolicy Bypass -File tools\build-golden.ps1
```

`tools/arduino/` is created by the fetch script and is never committed. It holds:

| Folder | Contents |
|---|---|
| `bin/` | `arduino-cli.exe` |
| `data/` | Arduino AVR core, avr-gcc, avr-libc, avrdude (arduino-cli's data directory) |
| `user/` | Sketchbook and user libraries |
| `staging/`, `cache/` | Downloads and the build cache |
| `builds/` | Build output of the scripts and of `simcli compile` |
| `arduino-cli.yaml` | Configuration pointing all of the above into this folder, English output |

## Licences

arduino-cli and avr-gcc are GPLv3. They run only as separate programs, never linked into the game (ADR-0003, docs/12 §3). The shipping build will include their licence texts and source links in `tools/licenses/`.
