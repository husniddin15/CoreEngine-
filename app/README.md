# app

The Unity project (Unity 6000.6.2f1, URP 17.6, [ADR-0001](../docs/adr/ADR-0001-game-engine.md)). For now it holds only the Phase 0 spikes ([docs/11 §3](../docs/11-roadmap.md)); throwaway code under `Assets/Spike/`.

The simulation core is not copied into the project: `Packages/manifest.json` loads it as the local package `com.coreengine.sim` from `../core/CoreEngine.Sim`.

## Before opening the project

1. `powershell -ExecutionPolicy Bypass -File native\manifold\build.ps1` puts `manifoldc.dll` into `Assets/Plugins/x86_64/` ([native/README.md](../native/README.md)).
2. `powershell -ExecutionPolicy Bypass -File tools\fetch-lab-assets.ps1` downloads the Garage lab (about 50 MB of CC0 files from Poly Haven, checked by MD5) into `Assets/ThirdParty/PolyHaven/`, which git ignores. Without it the Garage falls back to a drawn room.
3. Unity Hub: add `app/`, open it with 6000.6.2f1 (Windows Build Support (IL2CPP) module installed).
4. Menu **CoreEngine → Spike → Configure Project** creates the URP pipeline, physics and player settings, the plugin settings, and the two scenes `Assets/Spike/Garage.unity` (first in the build) and `Assets/Spike/RobotSpike.unity`. It builds the lab from the downloaded files (`Editor/SpikeLab.cs`) and bakes its light probes into `Assets/Spike/Garage/`, which git also ignores.

## The Garage (main screen prototype, ADR-0009)

The game starts in the Garage (`Scripts/Garage/GarageSpike.cs`). The selected robot turns on an aluminium turntable on a workbench in a photographed robotics lab. The robot card is on the left, the six action tiles on the right, and the robot bar with rendered thumbnails at the bottom. Drag to orbit, mouse wheel to zoom.

- **Customize**: free finishes and the two launch packs' finishes; a locked finish can be tried (shown everywhere, never saved).
- **Check & repair**: battery charge and motor winding temperatures from the core models (`BatteryPack`, `MotorWinding`); burnt motors and empty batteries are replaced for free, with a "why it broke" card.
- **Code**: the sketch in the code editor; **Upload** compiles it with the bundled arduino-cli on a worker thread and the arena then runs the new firmware.
- **START** runs the robot in the obstacle field; **◀ Garage** (or Esc) in the arena brings it back with its battery and motor state. The robots are saved to `garage.json` in the player's data folder.
- **Build** (`Scripts/Garage/GarageEdit.cs`): add parts from the Parts Bin; click a part to select it, drag it on the deck, **R** turns it, **Del** removes it; **Ctrl+Z** undoes.
- **Wire**: drag from a pin to another pin (or click one, then the other) to add a jumper; the label under the mouse names the pin; **Look at** brings the camera close to a part and prints its pin names; **Connect from the lists** picks both ends by name. A header pin takes one jumper, a screw terminal two wires. Click a wire and press **Del** to remove it. Right-drag turns the view, the middle button pans, the wheel zooms toward the mouse.
- **Body**: shape, size, thickness, corners, walls, decks, M3 hole grid and material; Manifold rebuilds the plates on a worker thread; **Export STL** saves to `Documents\CoreEngine\Exports`.
- START builds the arena robot from the design (`RobotSpike.cs`): wheels on the motors that were placed, mass and centre of mass from the parts, and the circuit from the wires, so wrong wiring behaves wrongly. A new Uno runs Blink until a sketch is uploaded.
- Notebook, Shop and Workshop show their plans. The prototype starts with two test robots; the shipped game starts empty (D16).

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

Benchmark: `Builds\Spike\CoreEngineSpike.exe -spikeBench report.txt` opens a 1280 × 720 window for about a minute. It goes through the Garage (customize, a modelled motor burn-out and repair, a real upload), builds a new robot from an empty chassis in Body, Build and Wire (with an STL export and two wiring mistakes that are checked and undone), presses START with that robot, measures the arena (emulator speed, frame rate, emulator cost per physics step, robot telemetry, mesh booleans, fonts, editor scrolling and typing, a simulated tab drag), returns to the Garage, writes `report.txt` and about fifteen screenshots next to it, and quits. Other programs using the CPU change the numbers noticeably on a laptop; compare the "emulator alone" line between runs.
