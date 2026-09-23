# CoreEngine

A Windows 3D STEAM-education robotics game: design a robot body, place real electronic parts, wire them like on a real breadboard, write real Arduino code that is compiled by the real toolchain and executed by a cycle-accurate ATmega emulator, and test the robot in physics arenas. A pure sandbox, free to play on Steam, in English, Uzbek and Russian.

Project status: **Phase 0, technical spikes** (September 2026). The ATmega328P emulator runs real compiled Arduino sketches; see [core/README.md](core/README.md).

```powershell
powershell -ExecutionPolicy Bypass -File tools\fetch-toolchain.ps1   # once: Arduino toolchain into tools/arduino
dotnet test core/CoreEngine.slnx -c Release
dotnet run --project core/CoreEngine.Sim.Cli -c Release -- compile core/CoreEngine.Sim.Tests/Golden/Sketches/Blink --run 3
```

- Documentation index: [docs/00-README.md](docs/00-README.md)
- Start here: [docs/01-vision-and-scope.md](docs/01-vision-and-scope.md)
- Decisions still open: [docs/13-open-questions-and-risks.md](docs/13-open-questions-and-risks.md)
- Roadmap: [docs/11-roadmap.md](docs/11-roadmap.md)

Planned repository layout (see docs/04 §3): `core/` (C# simulation core), `app/` (Unity 6), `native/` (Manifold CSG wrapper), `tools/` (arduino-cli + AVR core), `content-src/`.

Arduino® is a trademark of Arduino S.r.l. This project is not affiliated with Arduino.
