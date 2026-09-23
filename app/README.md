# app

The Unity project (Unity 6000.6.2f1, URP 17.6, [ADR-0001](../docs/adr/ADR-0001-game-engine.md)). For now it holds only the Phase 0 spikes ([docs/11 §3](../docs/11-roadmap.md)); throwaway code under `Assets/Spike/`.

The simulation core is not copied into the project: `Packages/manifest.json` loads it as the local package `com.coreengine.sim` from `../core/CoreEngine.Sim`.

## Before opening the project

1. `powershell -ExecutionPolicy Bypass -File native\manifold\build.ps1` puts `manifoldc.dll` into `Assets/Plugins/x86_64/` ([native/README.md](../native/README.md)).
2. Unity Hub: add `app/`, open it with 6000.6.2f1 (Windows Build Support (IL2CPP) module installed).
3. Menu **CoreEngine → Spike → Configure Project** creates the URP pipeline, physics and player settings, the plugin settings and `Assets/Spike/RobotSpike.unity`.

## Phase 0 spike

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

Benchmark: `Builds\Spike\CoreEngineSpike.exe -spikeBench report.txt` opens a 1280 × 720 window for about 35 seconds, then writes `report.txt` (emulator speed, frame rate, emulator cost per physics step, robot telemetry every 0.5 s, mesh boolean timings, font coverage, editor scrolling and typing costs, a simulated tab drag) and seven screenshots next to it, and quits. Other programs using the CPU change the numbers noticeably on a laptop; compare the "emulator alone" line between runs.
