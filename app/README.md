# app

The Unity project (Unity 6000.6.2f1, URP 17.6, [ADR-0001](../docs/adr/ADR-0001-game-engine.md)). For now it holds only the Phase 0 spikes ([docs/11 §3](../docs/11-roadmap.md)); throwaway code under `Assets/Spike/`.

The simulation core is not copied into the project: `Packages/manifest.json` loads it as the local package `com.coreengine.sim` from `../core/CoreEngine.Sim`.

## Before opening the project

1. `powershell -ExecutionPolicy Bypass -File native\manifold\build.ps1` puts `manifoldc.dll` into `Assets/Plugins/x86_64/` ([native/README.md](../native/README.md)).
2. Unity Hub: add `app/`, open it with 6000.6.2f1 (Windows Build Support (IL2CPP) module installed).
3. Menu **CoreEngine → Spike → Configure Project** creates the URP pipeline, physics and player settings, the plugin settings, and the two scenes `Assets/Spike/Garage.unity` (first in the build) and `Assets/Spike/RobotSpike.unity`.

## The Garage (main screen prototype, ADR-0009)

The game starts in the Garage (`Scripts/Garage/GarageSpike.cs`): the selected robot turns on a turntable on the player's desk, with the robot card on the left, the actions on the right and the robot bar with rendered thumbnails at the bottom. Drag to orbit, mouse wheel to zoom.

- **Customize**: free finishes and the two launch packs' finishes; a locked finish can be tried (shown everywhere, never saved).
- **Check & repair**: battery charge and motor winding temperatures from the core models (`BatteryPack`, `MotorWinding`); burnt motors and empty batteries are replaced for free, with a "why it broke" card.
- **Code**: the sketch in the code editor; **Upload** compiles it with the bundled arduino-cli on a worker thread and the arena then runs the new firmware.
- **START** runs the robot in the obstacle field; **◀ Garage** (or Esc) in the arena brings it back with its battery and motor state. The robots are saved to `garage.json` in the player's data folder.
- Build, Wire and Body show what they will do; Notebook, Shop and Workshop show their plans. The prototype starts with two test robots; the shipped game starts empty (D16).

## Phase 0 spikes

`RobotSpike` builds a 3 × 3 m arena with random boxes and a two-wheel robot (articulation bodies, TT motors through an L298N model, HC-SR04 as a 17-ray cone). The robot runs the real compiled `ObstacleAvoider` sketch (`core/CoreEngine.Sim.Tests/Golden/Sketches/ObstacleAvoider`) on the ATmega328P emulator, 160 000 cycles per 10 ms physics step. Press **C** to switch between the follow camera and the top view.

`CsgSpike` cuts holes with Manifold through P/Invoke: two boxes and a cylinder hole, and a chassis plate with 48 holes.

`UiSpike` (Phase 0.6) docks UI Toolkit panels around the 3D view: the code editor (`Scripts/UI/CodeEditor.cs`, a 522-line sketch), an inspector with live sensor and motor values, a console with an error card, the Serial Monitor with the robot's output, and an event log. Drag a tab onto another area to move its panel; **EN / OʻZ / RU** switch the language; **F1** hides the panels. Fonts are Segoe UI and Consolas from Windows. `SpikeSetup` copies the sketch and firmware from `core/CoreEngine.Sim.Tests/Golden` into `Assets/StreamingAssets`.

Builds (menu or batch mode):

```powershell
$unity = "C:\Program Files\Unity\Hub\Editor\6000.6.2f1\Editor\Unity.exe"
& $unity -batchmode -quit -projectPath app -executeMethod CoreEngine.Spike.Editor.SpikeSetup.BuildWindowsIl2cpp -logFile build.log
& $unity -batchmode -quit -projectPath app -executeMethod CoreEngine.Spike.Editor.SpikeSetup.BuildWindowsMono -logFile build.log
```

The IL2CPP build (`Builds/Spike/`) is the reference for speed and takes about 12 minutes from clean; the Mono build (`Builds/SpikeMono/`) takes under a minute and is for debugging.

Benchmark: `Builds\Spike\CoreEngineSpike.exe -spikeBench report.txt` opens a 1280 × 720 window for about a minute. It goes through the Garage (customize, a modelled motor burn-out and repair, a real upload), presses START, measures the arena (emulator speed, frame rate, emulator cost per physics step, robot telemetry, mesh booleans, fonts, editor scrolling and typing, a simulated tab drag), returns to the Garage, writes `report.txt` and about fifteen screenshots next to it, and quits. Other programs using the CPU change the numbers noticeably on a laptop; compare the "emulator alone" line between runs.
