# CoreEngine

A Windows 3D STEAM-education robotics game: design a robot body, place real electronic parts, wire them like on a real breadboard, write real Arduino code that is compiled by the real toolchain and executed by a cycle-accurate ATmega emulator, and test the robot in physics arenas. A pure sandbox, free to play on Steam, in English, Uzbek and Russian.

<img width="1920" height="1080" alt="Screenshot (310)" src="https://github.com/user-attachments/assets/9752b6f2-d89d-4d5f-bdac-c13375a1093f" />

Project status: **Phase 0, technical spikes** (September 2026). The ATmega328P emulator runs real compiled Arduino sketches ([core/README.md](core/README.md)), and a Unity spike drives a physics robot with one of them ([app/README.md](app/README.md)).

<img width="1920" height="1080" alt="Screenshot (311)" src="https://github.com/user-attachments/assets/c461bc14-4a8e-4959-a4ee-8b964a78635a" />

```powershell
powershell -ExecutionPolicy Bypass -File tools\fetch-toolchain.ps1   # once: Arduino toolchain into tools/arduino
dotnet test core/CoreEngine.slnx -c Release
dotnet run --project core/CoreEngine.Sim.Cli -c Release -- compile core/CoreEngine.Sim.Tests/Golden/Sketches/Blink --run 3
```

- Documentation index: [docs/00-README.md](docs/00-README.md)
- Start here: [docs/01-vision-and-scope.md](docs/01-vision-and-scope.md)
- Decisions still open: [docs/13-open-questions-and-risks.md](docs/13-open-questions-and-risks.md)
- Roadmap: [docs/11-roadmap.md](docs/11-roadmap.md)

Repository layout (see docs/04 §3): `core/` (C# simulation core), `app/` (Unity 6), `native/` (Manifold mesh booleans, [native/README.md](native/README.md)), `tools/` (arduino-cli + AVR core), later `content-src/`.

Arduino® is a trademark of Arduino S.r.l. This project is not affiliated with Arduino.
<img width="840" height="648" alt="Screenshot 2026-09-29 225248" src="https://github.com/user-attachments/assets/4015d298-5d21-4ba8-a24e-500a28c9a1d1" />


