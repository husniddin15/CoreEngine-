# 04 — Technical Design Document (TDD)

Status: Accepted DRAFT v0.2 (2026-09-23) · Depends on: [01](01-vision-and-scope.md), [03](03-game-design.md) · Detailed specs: [05](05-arduino-emulation-spec.md), [06](06-electrical-simulation-spec.md), [07](07-physics-world-sensors-spec.md), [08](08-body-designer-spec.md) · Decisions: `docs/adr/`

---

## 1. Principles

1. **Engine-independent simulation core.** Everything that decides *what happens* (emulator, electrical solver, component behaviour, timing, project data) lives in a plain C# library with no game-engine dependency. Unity only renders, handles input, runs rigid-body physics and integrates Steam. This keeps the core unit-testable, runnable headless (CI, regression tests, replays) and portable to Godot or another engine if needed.
2. **Determinism.** Same project + same user inputs ⇒ same outcome, on the same build. Fixed time steps, no wall-clock dependence, seeded randomness.
3. **Data-driven components.** Parts are JSON definitions (pins, geometry, mass, electrical model type + parameters) plus reusable C# behaviour classes. Adding a sensor should not require touching the engine layer.
4. **Real toolchain, real binaries.** We never reimplement the compiler. `arduino-cli` produces the `.hex`; the emulator executes it.
5. **Offline first, Windows first.** No servers required to play. All tools bundled.
6. **Fidelity is a feature; performance is a constraint.** Budget: 1× real time with 2 MCUs at 60 fps on min-spec.

## 2. High-level architecture

```
+-------------------------------------------------------------------+
|  Unity 6 application (C#)                                          |
|  +----------------+ +--------------+ +-----------+ +-------------+ |
|  | Workbench/Wire | | Body Studio  | | Code Desk | | Arena/World | |
|  | (placement, UI)| | (CSG, import)| | (editor,  | | (PhysX,     | |
|  |                | |              | |  monitor) | |  sensors)   | |
|  +--------+-------+ +------+-------+ +-----+-----+ +------+------+ |
|           |                |               |              |        |
|  +--------v----------------v---------------v--------------v------+ |
|  |  App.Session  (owns a SimulationHost, drives it from FixedUpdate)| |
|  +---------------------------+-----------------------------------+ |
+------------------------------|-------------------------------------+
                               | C# API (no UnityEngine types)
+------------------------------v-------------------------------------+
|  CoreEngine.Sim (netstandard2.1 class library)                     |
|  +-----------+ +------------+ +------------+ +---------+ +--------+|
|  | Avr       | | Electrical | | Components | | Sched-  | | Model/ ||
|  | (CPU +    | | (nets,     | | (behaviour | | uler    | | Serial-||
|  | periph.)  | |  solver)   | |  models)   | | (events)| | ization||
|  +-----------+ +------------+ +------------+ +---------+ +--------+|
|  +-----------+ +------------+                                      |
|  | Compile   | | Physics    |  <- interfaces only; Unity implements |
|  | (arduino- | | Bridge     |     IPhysicsWorld, ISensorQuery       |
|  |  cli)     | | (contracts)|                                      |
|  +-----------+ +------------+                                      |
+--------------------------------------------------------------------+
        |  subprocess                      |  P/Invoke (native/)
+-------v--------+               +---------v-----------+
| tools/arduino- |               | manifoldc.dll       |
| cli + avr-gcc  |               | (Manifold C API)    |
+----------------+               +---------------------+
```

## 3. Solution layout (repository)

```
CoreEngine/
  docs/                      this documentation
  core/                      engine-independent C# (built with .NET SDK; Unity loads CoreEngine.Sim as a local package)
    CoreEngine.Sim/          Avr/, Electrical/, Components/, Scheduler/, Model/, Serialization/, Compile/, Physics/ (contracts)
    CoreEngine.Sim.Tests/    xUnit tests; golden sketches; timing tests
    CoreEngine.Sim.Cli/      headless runner: `simcli run project.rbp --seconds 30 --telemetry out.csv --expect expect.json` (regression tests)
  app/                       Unity 6 project
    Assets/App/{Core,Workbench,Body,Code,World,Tutorial,Notebook,Shop,UI,Steam,Save}/   asmdefs per folder
    Assets/Content/{Components,Arenas,Tutorial,Notebook,Localization}/   JSON + prefabs + meshes + string tables (en, uz, ru)
    Packages/
  native/
    manifold/                CMake project: Manifold's own C API built as one self-contained x64 DLL (manifoldc.dll)
  tools/
    arduino-cli/             bundled binary + config; scripts to fetch the arduino:avr core into a local data dir
    licenses/                third-party licence texts and the GPL source offer
  content-src/               Blender sources, textures, datasheet summaries (Markdown)
  build/                     CI scripts (GitHub Actions), Steam depot config
```

Unity references `core/CoreEngine.Sim` as a **local package** (source) with an asmdef, so there is one source of truth and no DLL copying. The same source builds under the .NET 10 SDK (LTS) for tests and the CLI; the library itself targets netstandard2.1 for Unity. Rule: `CoreEngine.Sim` must compile without `UnityEngine` and without `unsafe` unless benchmarked (see §13).

## 4. Runtime model — one fixed step

The application drives the simulation from Unity `FixedUpdate` at **100 Hz** (10 ms). Each step:

```
FixedUpdate (Δt = 10 ms):
  1. PhysicsBridge.CollectInputs()      -> wheel angular speeds, joint angles, sensor geometry queries prepared
  2. host.Advance(10 ms)                -> Sim core runs 10 electrical ticks of 1 ms:
       for tick in 1..10:
         a. Avr.Run(16 000 cycles)      -> CPU executes; peripherals fire events (pin changes, UART bits, ADC sample requests)
            (pin-change events are applied to nets immediately, cycle-stamped; digital nets re-evaluated event-driven)
         b. Electrical.SolveDc()        -> nodal analysis on analog nets; component currents/voltages updated
         c. Components.Step(1 ms)       -> behaviour models integrate (motor current/torque, battery SoC, sensor timers, temperatures)
         d. Failures.Check()            -> overcurrent/overvoltage/thermal accumulators; may mark parts destroyed, trip fuses
  3. PhysicsBridge.ApplyOutputs()       -> motor torques, servo joint targets, buzzer waveform → audio ring buffer
  4. Unity physics step (PhysX, 10 ms)
  5. Telemetry sampling, event log
Update (render, variable):
  interpolate rigid bodies; animate LEDs/wires/overlays from the latest sim snapshot
```

Why three rates: the MCU needs cycle resolution (62.5 ns) for correctness; electrical/analog dynamics of interest (motor current, battery sag, sensor timing) are well captured at 1 kHz; rigid-body physics of a 20 cm robot at ≤ 1 m/s is fine at 100 Hz (1 cm of travel per step). Events that need sub-millisecond precision (HC-SR04 echo edges, UART bits, servo pulses, WS2812 timing) are scheduled at cycle precision inside step 2a by the components themselves, using the latest physics/analog state. Rationale and alternatives: [ADR-0006](adr/ADR-0006-time-and-sync-model.md).

**Time scale.** Speed ×k runs k fixed steps per rendered frame (bounded by CPU). Pause stops stepping; single-step runs one step. Nothing in the core reads the wall clock.

**Multiple boards.** Each board is an `Mcu` instance with its own cycle counter; all are advanced by the same 1 ms slices. Cross-board communication happens only through nets (UART/I2C/GPIO), so boards stay in lockstep.

## 5. Threading

- v1: the core runs **synchronously inside FixedUpdate** on the main thread. Budget per 10 ms step: ≤ 4 ms for two MCUs + solver on min-spec.
- v1.x optimisation (if needed): move `host.Advance` to a worker thread running one step ahead; physics inputs/outputs are exchanged through double-buffered snapshots (introduces one 10 ms latency between physics and sensors, acceptable). The core is already designed with no shared mutable state outside the host.
- Compilation runs `arduino-cli` on a background task; UI stays responsive; the result is applied on the main thread.

## 6. Domain model (core)

| Entity | Key fields |
|---|---|
| `Project` | One robot in the Garage ([ADR-0009](adr/ADR-0009-garage-main-screen.md)): id, name, schema version, boards[], components[], wires[], body (assembly), finishes (customization per target: body, wheels, boards, wires, decals), last arena, sketches[], settings, created/modified |
| `ComponentInstance` | id, definition id, transform, mount (target part + mount point or tape/breadboard position), settings (jumpers, knob angle), state (damaged, temperature), pins[] → net ids |
| `ComponentDefinition` (content) | id, display name, category, tier, dimensions (mm), mass (g), mesh, pins[] {name, role, position, group}, mount points[], electrical model {type, params}, behaviour {type, params}, datasheet ref |
| `Net` | id, pins[] (component id + pin), kind (digital/analog/power/ground/mixed), solver node index, voltage, is-floating |
| `Wire` | id, endpoints (pin/hole/terminal refs), type, colour, length, control points |
| `Breadboard` | definition (rows, strips), hole → strip mapping; strips are nets |
| `Body` | parts[] (primitives/meshes + material), mount points[], joints[] (axle, hinge, caster), computed mass/CoM, colliders |
| `Board` (Uno/Nano/Mega) | is a ComponentDefinition with an `Mcu` behaviour: firmware (hex), fuses, USB-serial bridge, on-board LEDs, regulator model |
| `Sketch` | files[] (ino/h/cpp), board fqbn, last compile result (hex, elf path, diagnostics, sizes) |
| `Arena` | id, geometry (static meshes/prefabs), floor texture & reflectance map, obstacles[], lines[], lighting, spawn, measuring aids (distance markings, trigger lines) |
| `TutorialStep` | id, prompt text key (localized), highlight target, completion event, skippable |
| `NotebookCard` | id, kind (datasheet / error help / why it broke), part or failure code, Markdown per language (en, uz, ru), links |

First implementation (2026-09-24, `CoreEngine.Sim.Design`, 126 tests with the rest of the core): `RobotDesign` = `BodyDesign` + `PartInstance[]` (catalogue id, slot or deck x/z/rotation) + `WireInstance[]` (pin to pin, colour). `PartCatalog` holds the slice's parts with sizes, masses and pins in millimetres; `DesignGeometry` places parts and pins in the chassis frame (origin at the chassis centre 50 mm above the floor, y up, +z forward, Unity's rotation convention) and gives mass and centre of mass; `Netlist` and `CircuitAnalysis` turn the wires into a `RobotCircuit` ([06 §3.8](06-electrical-simulation-spec.md)); `DriveMap` turns driver inputs into motor voltages; `StlWriter` writes binary STL. The Garage prototype saves the design inside each robot in `garage.json`; the `.rbp` package of [§7](#7-file-formats) replaces that in Phase 1.

## 7. File formats

- **Project package `.rbp`** (zip): `project.json`, `circuit.json` (components, nets, wires, breadboard placement), `body/*.glb` + `body.json` (parametric primitives kept editable; imported meshes stored as glb), `sketches/<board>/*.ino`, `eeprom/<board>.bin`, `arena.json` (reference or embedded), `thumbnail.png`, `meta.json` (schema version, app version, hashes).
- **Component definition** `component.json` (+ mesh `.glb`, icon `.png`, datasheet `.md`) in `Assets/Content/Components/<id>/`. Workshop components (v1.x) use the same format.
- **Arena** `arena.json` + optional meshes. **Tutorial** `tutorial.json`. **Notebook cards** as Markdown per language (`en`, `uz`, `ru`). UI strings in Unity Localization string tables.
- JSON via `System.Text.Json` with source generators (AOT/IL2CPP friendly). Every file carries `schemaVersion`; migrations live in `Serialization/Migrations`.
- Export bundle for the real build: `<name>-realbuild.zip` = `.ino`, wiring table (CSV: from pin → to pin, wire colour), BOM (CSV with quantities and example part names), `body/*.stl`, `README.md` with step-by-step assembly.

## 8. Component definition and behaviour model

```json
{
  "id": "hc-sr04",
  "name": "HC-SR04 Ultrasonic Sensor",
  "category": "sensors",
  "tier": 1,
  "dimensions_mm": [45, 20, 15],
  "mass_g": 8.5,
  "mesh": "hc-sr04.glb",
  "pins": [
    {"name": "VCC",  "role": "power_in",  "pos_mm": [-3.81, -8, 0]},
    {"name": "Trig", "role": "digital_in","pos_mm": [-1.27, -8, 0]},
    {"name": "Echo", "role": "digital_out","pos_mm": [1.27, -8, 0]},
    {"name": "GND",  "role": "ground",    "pos_mm": [3.81, -8, 0]}
  ],
  "mounts": [{"type": "screw_m2", "pos_mm": [...]}, {"type": "tape_face", "face": "-y"}],
  "electrical": {"type": "LogicDevice", "vcc_min": 4.5, "vcc_max": 5.5, "i_quiescent_mA": 15, "input_threshold": "ttl", "output_drive": "push_pull_5v"},
  "behaviour": {"type": "Ultrasonic", "min_cm": 2, "max_cm": 400, "cone_deg": 15, "trigger_min_us": 10, "timeout_ms": 38, "echo_us_per_cm": 58.0},
  "datasheet": "hc-sr04.md"
}
```

Behaviour and electrical model types are C# classes registered in a factory (examples: `Ultrasonic`, `DcMotor`, `HBridgeL298`, `Servo`, `Battery`, `Led`, `Resistor`, `Potentiometer`, `Ldr`, `LogicDevice`, `LineSensorTcrt5000`, `Imu6050`, `Hd44780` (with the PCF8574 backpack), `Ssd1306`, `Ws2812`, `BuzzerPassive`, `PushButton`, `Regulator`, `Fuse`, …; the full lists are in [06 §5](06-electrical-simulation-spec.md) and [07 §4–5](07-physics-world-sensors-spec.md), and [09](09-components-catalog.md) names the type used by every part). Each implements: `Attach(nets)`, `OnPinEvent(cycle, pin, level)` (digital), `Stamp(solver)` (analog contribution), `Step(dtMs, ctx)` (dynamics, physics queries via `ISensorQuery`), `CheckLimits(ctx)` (failure), `Serialize/Restore`. Full contract in doc 06.

## 9. Compile pipeline (summary; details in doc 05 §8)

1. Write sketch files to `%LOCALAPPDATA%/CoreEngine/sketches/<projectId>/<board>/`.
2. Run `tools/arduino-cli/arduino-cli.exe compile --fqbn arduino:avr:uno --config-file tools/arduino-cli/arduino-cli.yaml --output-dir <out> --warnings default --format json <sketchDir>`.
3. Parse JSON result + compiler stderr → diagnostics (file, line, column, severity, message) and sizes (flash/RAM usage, shown like the IDE).
4. Load `<out>/<sketch>.ino.hex` → flash image; keep `.elf` for future symbolic debugging.
5. Reset the MCU; if the Serial Monitor is open, emulate DTR reset.

The `arduino:avr` core and toolchain are installed into a bundled data directory at build time (`tools/fetch-core.ps1`) so end users compile offline. Licence handling: [12-business-steam-legal.md](12-business-steam-legal.md).

## 10. Rendering

- Unity 6 **URP**, forward+, DX12 (DX11 fallback). Target 60 fps at 1080p on integrated GPUs; the scene is small (< 200k triangles typical).
- Wires: procedural tube meshes, laid by `WireRouter` (core, 2026-09-25) so that none passes through a part or the body:
  - The robot is a distance field: how far a point is from the nearest part block (`PartDef.Solids`: a part's box, or for the L298N its board, heatsink, terminals, header and capacitors, so jumpers can come down to its header beside the tall heatsink; a motor's wheel as a cylinder), body shape (exact for boxes and round cylinders, a safe underestimate for the others; a hole takes material only from the solids of its group, as Manifold cuts them) or the floor. A 20 mm lookup grid lists what lies near each point.
  - A* crosses a grid of 5 mm cells round the robot (30 mm of room at the sides, 45 mm above); each move is checked against the field (the field changes no faster than a point moves, so a segment whose ends are each farther than half its length from everything is clear; otherwise its halves are checked). A step costs more the less room it has under 4 mm (up to three times its length with 1.4 mm, the least a wire may keep), so a route keeps room for round bends where it can and still takes a narrow way, such as a hole, rather than a long way round.
  - The path starts 8 mm out along each pin's exit (from the top of a Dupont housing, 2.5 mm out of a terminal, or a lead's end). It is pulled straight where it can be without coming closer to anything than it was, and a jog (a side under 6 mm, which A*'s first cell often makes beside the pin) is taken out where the wire can go straight past it.
  - Every corner becomes an arc (the owner, 2026-09-25: wires "should be smooth like real life"): up to 16 mm radius, as far as its two sides allow. Two bends close together share the side between them so both get the same radius, and what one cannot use goes to the other; a side from a pin goes wholly to its bend, since a jumper bends as soon as it is out of its housing. An arc that would touch something (closer than 0.9 mm, the wire's radius and a hair) shrinks by 30 % at a time. The wire is then given a point every 2 mm and slack upward (up to a tenth of its length), as a jumper arches. A wire that must turn right back as it leaves its pin, such as a battery lead going back over its holder, bends at about 4 mm radius; everywhere else bends are wider. A core test holds every wire of the kit and of a robot built like the owner's to at most 32° from one 2 mm step to the next (about 3.6 mm radius), clear of every solid.
  - The tube has 12 sides, so a wire stays round in a close-up.
  - **Points the player gives a wire** (`WireInstance.Points`, `WirePoint`; the owner, 2026-09-25). A free point is in the chassis frame; a glued one belongs to its part (in the part's frame, mm) or body shape (as shares of the shape's half sizes in its own frame, so it stays on its face when the shape is resized) and moves with it. `RobotDesign.RemovePart` and `DropLooseGlue` take away glue on what is gone.
  - `WireRouter.Path` lays such a wire in stretches, pin to point to point to pin, each laid as a wire of its own (A*, pull, jogs out, arcs, slack). A stretch ends at a free point running along the direction halfway between the way in and the way out (along a surface when the point is within 6 mm of one), so the two stretches meet there smoothly; the slack of a stretch ending at a point starts with no slope (sin²) and is half a jumper's. A free point inside or too near something is pushed out up the field's slope. A glued wire lies flat on its surface, 0.85 mm up, for 8 mm under the glue, and each stretch leaves it rising at 15°; the Garage draws a blob of hot glue there (12 × 6 × 4.4 mm). A stretch with no way round is a plain curve.
  - For the mouse, the router also walks a ray through its distance field (`Pick`, sphere tracing: each step is the distance to the nearest solid, so it never steps into one) to find the plate or part under the mouse and its normal, finds the surface nearest a point (`NearestSurface`), and pushes a point clear (`Clear`). Routes are cached by `KeyFor(wire)`: the router's key, the pins and the points. Eight core tests check points and glue: through a bend point, flat under glue on the deck, glue following its part and staying on its face, glue taken away with its part or shape, a point inside the battery holder pushed out, rays finding the deck and a wheel, keys and clones, and a wire through three points, smooth and clear.
  - Routes are kept by the design's shapes and the wire's pins (`WireRouter.Key`), so adding a wire lays only that one. While a part is dragged in the Studio its wires are plain arches, laid properly when it is let go. The kit's 16 wires take about 20 ms in .NET and about 50 ms in the Mono player from nothing.
- Boards: PBR materials with high-resolution silkscreen textures (labels must be readable at 1080p when zoomed).
- LEDs: emissive intensity driven by modelled current (with PWM averaged per frame, plus optional true flicker at low duty).
- Overlays: net voltage tint, current dots (particle system fed from solver), heat glow, sensor cones (transparent meshes), CoM marker.
- Text labels: TextMeshPro world-space labels for pins on hover.
- Prototype settings (2026-09-24, `SpikeSetup`): URP with 4× MSAA and HDR; soft shadows (medium quality) from a 2048 map in two cascades over 4 m (about 1 mm per shadow texel near the robot), and shadows from spot lights; screen-space ambient occlusion at half resolution with a 3 cm radius (the scale of the parts) and interleaved-gradient noise, the same every frame (2026-09-25: its blue noise changes every frame for a temporal filter to average, which the game does not use, so the shading at edges and creases flickered like black specks, about 5,700 pixels a frame at 1920 × 1080; now none, and the benchmark checks that a still showroom stays still); post-processing with a neutral tone curve, a little more contrast (+12) and saturation (+8) and a soft bloom above 1.1 (its light vignette is off since 2026-09-25). The arena has no fog (D24, 2026-09-25): a procedural sky with a thin atmosphere (0.55) tinted blue, an 80 m pale ground under the 3 m floor out to the horizon, and a three-colour ambient light (sky, horizon, ground) so shadows come out neutral grey, not the blue of the sky. Smooth shapes are made in code: tyres and hubs turned on a "lathe" with 64–96 segments (Unity's cylinder has 20), jumpers as tubes along cubic curves that leave each pin along its exit direction, with Dupont housings, the arena floor as 30 cm laminate tiles from a generated texture. Measured with `FrameTimingManager` in the IL2CPP player on the owner's laptop (GTX 1650, 1280 × 720): the GPU needs 1.8 ms a frame in the Garage and 1.5 ms in the arena (worst 3.3 ms), so both run at the display's 144 fps and there is room for the Phase 1 graphics settings (higher shadow resolution, full-resolution ambient occlusion) on stronger cards.
- The photographed Garage (2026-09-24, D19) was a first step: a Poly Haven HDR panorama of a real lab for the sky, the ambient light and reflections, scanned props placed by `SpikeLab`, a key light matched to the photo's windows. `tools/fetch-lab-assets.ps1` still fetches those files, but the Garage no longer uses them (D20).
- The bright engineering lab (2026-09-24, D20, [03 §3.1](03-game-design.md#31-the-garage-main-screen)).
  - **Built in the editor.** `WhiteLab` (`app/Assets/Spike/Editor/WhiteLab/`, run by `SpikeSetup.ConfigureProject`) models the room with the part toolkit below and saves every mesh (with lightmap UVs from `Unwrapping.GenerateSecondaryUVSet`), texture and material under `Assets/Spike/Lab/`. Like the bake in `Assets/Spike/Garage/`, that folder is not committed: after a fresh clone, run Configure Project before opening or building the Garage. The painted textures (floor tiles, the pegboard's holes, the measuring mat, the instruments' panels and glowing screens, the whiteboard, the poster, the bin labels) are `Raster` pictures saved as PNG, the normal maps imported as normal maps.
  - **Light.** Four ceiling LED panels and the window are baked area lights; the lamp over the turntable and a daylight key are mixed lights (direct light and soft shadows on the robot in real time, their bounce baked). The progressive lightmapper bakes three bounces into two lightmaps at 28 texels per metre in about a minute, plus 321 light probes (dense over the bench, where the robot turns) and a box-projected reflection probe of the room. The sky is a neutral grey gradient, so anything lit outside the room, such as the robots' thumbnails, sees no coloured sky.
  - **Camera.** The `GaragePost` profile: ACES, a cool white balance (−6), exposure +0.4 and a weak bloom above 1.15. Its depth of field, vignette and film grain are off (2026-09-25, D24; the owner found the lab "dizzy like fog"): `WhiteLab` saves them off and `GarageSpike`/`GarageCamera` turn them off at run time for a lab saved before. The depth of field had been Gaussian and far only, softening the room from 3 cm behind the robot; before that a lens's focus (Bokeh at f/2.8) blurred parts of the robot in close-ups.
  - **Physics.** `RobotPhysics` builds the robot's rigid bodies (docs/07 §3) for the arena and for the showroom. There `RobotSettle` lets the robot go on the turntable (a convex disc of its radius, or as wide as the robot itself if that is wider, standing on the desk; a flat floor for the robot bar's pictures and the arena card) in a physics scene of its own (`LocalPhysicsMode.Physics3D`, simulated by hand in 10 ms steps until it has been still for 0.2 s, 2.5 s at most) and the showroom plays its poses back on the turntable; the robot bar's pictures take its last pose. Building the model and settling it takes about 20 ms in the release player. Build and Wire show the design's own pose.
  - **Arena picture.** `ArenaBuilder` builds the obstacle field for the arena and, on a stage far from the lab, for the arena picker's render (the chosen robot on its start pad in its settled pose, 480 × 270), so the picture is the arena's own geometry from the same seed.
  - **Balance view.** `GarageBalance` draws, ten times a second in the Studio, the centre of mass (`DesignGeometry.CentreOfMass`) as an on-top sphere with a plumb line, and the convex hull of the tyres' contact lines and the casters' balls as a see-through patch, green or red from `RobotStance`.
  - **Balance.** `RobotStance.Of(design)` (core, `Design/Stance.cs`) stands a two-wheeler on its wheels and turns it about their axle toward its centre of mass, the wheels rolling under it, until a caster's ball or a corner of a part's solid blocks or of a body shape's box reaches the floor; the first one decides whether it rolls or drags, which way it tips and how far. The robot card, Check & repair and the arena's event log report it; the arena's physics shows the same lean (16.1° against 16.2° for the owner's robot).
  - **Pieces.** `RobotPieces.Of(design)` (core, `Design/Pieces.cs`) finds which pieces hold together: the boxes of the parts (a motor's without its wheel) and of the body's solid shapes, touching within 1.5 mm, joined by union-find; the group with the most wheels, then the heaviest, is the robot. `Keep(design, group)` gives a copy with that group's pieces only, which the balance check, the Balance view and the robot's mass and centre of mass use; `SonarTilt` measures how far an HC-SR04 is tipped. `RobotPhysics` gives each loose group a `Rigidbody` of its own (mass and centre of mass from its pieces, their colliders moved onto it, a sphere for a loose wheel) and `RobotVisuals.MovePieces` hangs the pieces' models on it; in the showroom `RobotSettle` records the loose bodies' poses with the robot's and plays them back; a group that comes to rest with its centre of mass past the turntable's edge lies on the desk and is taken off the turntable, so it stays put as it turns. Two guards keep the solver in hand: the chassis spins at most 30 rad/s, and when only part of the design holds, its inertia is at least twice a wheel's about every axis (a lone motor left to drive its wheel was spun up by the wheel's geared inertia and flew 300 m).
  - **Pictures.** `GaragePartPictures` renders, once per session when the Garage opens, a 160 px picture of every catalogue part and Studio shape and a 320 × 160 picture for each action card: part models laid out on an empty stage (Build, Wire, Body), or the lab's own monitor and multimeter photographed where they stand, in the lab's light (Code, Check & repair); Customize shows the selected robot's thumbnail. Parts and shapes are drawn larger on black and on white, the difference giving their transparency, cropped to the part and scaled down smoothly. Until a picture exists its card shows the line icon.
  - **View.** `GarageCamera.cs`: one set of controls in the showroom, Wire and the Studio (turn, pan with Shift or the middle button, zoom toward the mouse, double-click pivot), a view cube (`UI/ViewCube.cs`, drawn with the vector painter from the camera's turn, its faces split into face, edge and corner zones) and Home and Fit, each gliding there; they show only in Wire and the Studio, on the left (beside the robot's card, or in the top left corner of the Studio's free view). From below the bench (Wire and the Studio only) the camera leaves out the lab's layers: the shell on layer 8 and, from start-up, everything else of the lab on layer 9. In Wire and the Studio the turntable is hidden and the robot stands on the mat.
  - **Cost.** Measured in the IL2CPP player on the owner's laptop (GTX 1650, 1280 × 720): the Garage runs at 135 fps with 2.5 ms of GPU time a frame, the arena at 141 fps. Its few slow frames (21 ms) came every 0.26 s in the Mono player too, and not in the morning's runs, so they came from a program running beside the game, not from the lab.
- Part models (2026-09-24, [09 Appendix B](09-components-catalog.md#appendix-b--3d-asset-production-notes)), in `app/Assets/Spike/Scripts/Parts/`:
  - `MeshKit` builds meshes in millimetres: rounded boxes (a face grid projected onto an inner box plus a radius, so rounded edges get smooth normals), surfaces of revolution with creases kept sharp above an angle, extrusions of outlines (ear clipping), swept tubes; one submesh per material slot, under a transform stack, every triangle wound from its normals, tangents for normal maps.
  - `Raster` paints on the CPU: shapes are anti-aliased from their signed distance to each pixel; text uses `StrokeFont`, a single-stroke font like a PCB tool's; `Grain` adds unevenness. It keeps colour, metal, smoothness and height, and turns them into URP Lit's base map, metallic map (metal in red, smoothness in alpha) and a normal map from the softened height.
  - `PartLooks` makes the materials once: plain ones (gold, tin, nickel, black plastic…) and painted ones copied from `PartTextured.mat`, which `SpikeSetup` saves with the normal map, metallic map and emission turned on so those shader variants are in the build. LED lenses switch between an off and a glowing material.
  - `PartModels` builds each catalogue part once per session (Uno, L298N, HC-SR04, TT motor and wheel, 4×AA holder, caster, SG90 and its horn, LED module) and `RobotVisuals` places them; the wheel and the servo's horn are separate meshes because they turn.
  - **Cost.** Painting is most of the work: the Uno's board is a 1168 × 1112 picture. `Raster` keeps it quick: each shape is a struct with its own compiled loop (no delegate call per pixel), each row of a shape covers only where the shape can reach (a long diagonal trace no longer measures its whole box), fully covered pixels are written without blending, and the normal map's blur is two passes. In the IL2CPP player the Uno takes 143 ms the first time and all eight parts 430 ms. `PartModels` and `PartLooks` log each time in the player log. If this grows with more parts, the pictures can be painted by `SpikeSetup` instead and shipped compressed.

## 11. UI technology

- **UI Toolkit** for all panels (dockable layout, virtualized lists for the parts bin, inspector property drawers, console).
- **Screens** ([ADR-0009](adr/ADR-0009-garage-main-screen.md)): the **Garage** is the main screen and its own scene (room, turntable, robot bar, robot card, action column, Customize, Check & repair). Build, Wire, Body and Code open the workbench space with the selected project; START loads the chosen arena with it. The project travels between screens with its state (battery charge, part damage and temperatures in `ComponentInstance.state`), and returning to the Garage saves it. Robot thumbnails are rendered from the model with an off-screen camera and stored as `thumbnail.png`.
- **Code editor**: v1 implements a custom editor on UI Toolkit: line numbers, syntax colouring via a small tokenizer, auto-indent, bracket matching, find/replace, error markers. Since 2026-09-25 the prototype edits as any code editor does: a selection (drawn under the see-through rows), Windows' clipboard through `GUIUtility.systemCopyBuffer`, undo and redo as snapshots of the text (typing a word undoes as one step), word moves, line indenting and a right-click menu; the benchmark checks copy, paste, cut, undo and typing over a selection through real key events. The Phase 0.6 spike (`app/Assets/Spike/Scripts/UI/CodeEditor.cs`) proved the base: each line is a rich-text row in a virtualised `ListView`, colouring is incremental (block-comment state carried from line to line), and every glyph is pre-loaded into the font atlas at start-up. Evaluate a Monaco/WebView-based editor (Vuplex or similar) only if the custom editor proves inadequate; keep the editor behind an `ICodeEditor` interface. "Open in external editor" + file watching is the escape hatch for power users from day one.
- Localization via Unity Localization package string tables.
- **Scaling** (2026-09-25, research [R5](research/R5-game-ux-research.md)): the panel settings (`SpikePanel.asset`, set by `SpikeSetup.CreatePanelSettings`) scale with the screen from a 1280 × 720 reference with `Expand`, so the logical canvas is never smaller than the layout; `PanelSettings.scale` is the player's interface size. Mouse positions go through `RuntimePanelUtils.ScreenToPanel`, and the arena's camera rectangle and the Studio's projection shift are ratios of the panel, so neither depends on the scale. `Preferences` keeps the language and the size in Unity's player preferences; benchmark runs neither read nor write them.

## 12. Physics integration (summary; details in doc 07)

- PhysX via Unity; `ArticulationBody` for the robot (base + wheels + servo joints) for stability; `Rigidbody` for loose objects.
- Motor model → torque on wheel joints each step; servo model → joint drive target with modelled speed/torque limits.
- Sensors query the physics world through `ISensorQuery` (raycasts/spherecasts for ultrasonic cone, ground reflectance sampling for line sensors, body kinematics for IMU/encoders) — implemented in Unity, consumed by core behaviours.
- Fixed timestep 0.01 s; solver iterations tuned for small, light bodies (masses 5–500 g); collision scale: scene units are metres, robots ~0.2 m.

## 13. Performance budgets (min-spec: 4-core 2016 CPU, integrated GPU, 8 GB RAM)

| Item | Budget per 10 ms step |
|---|---|
| AVR emulation, 1 MCU (160 000 cycles) | ≤ 1.5 ms (≥ 110 M cycles/s) |
| Electrical solver (≤ 60 analog nodes) | ≤ 0.3 ms |
| Component behaviours (≤ 100 parts) | ≤ 0.3 ms |
| Physics (PhysX) | ≤ 1.0 ms |
| Total sim (2 MCUs) | ≤ 4 ms → leaves ≥ 12 ms per 16.7 ms frame for rendering/UI |

Emulator implementation rules for speed: pre-decoded instruction table (one decoded record per 16-bit word, invalidated on flash load), no allocations in the run loop, arrays over lists, `switch` on opcode class, lazy peripheral catch-up with a next-event cycle (peripherals are advanced only when the CPU reaches their next scheduled event or accesses their registers). Measured on IL2CPP release builds; Mono editor performance is not the target. If C# cannot meet the budget, the CPU core is ported to C++ behind the same interface (plan B in ADR-0002).

## 14. Save, cloud, Workshop

- Autosave every 2 min and on mode change; crash recovery from the last autosave.
- Steam Cloud syncs `%USERPROFILE%/Documents/CoreEngine/Projects/*.rbp` (≤ 100 MB quota target).
- Workshop items: `.rbp` (robot), `body` packages, `arena` packages. Upload via ISteamUGC with thumbnail and tags; download into a `Workshop/` folder; content is **data only** (JSON, glb, ino text) — never executable. Sketch text from Workshop is compiled only when the user presses Upload, exactly like their own code.

### 14.1 Entitlements and paid packs ([ADR-0007](adr/ADR-0007-monetization-free-to-play-dlc.md))
- **All pack content ships in the base depot.** A Steam DLC is only an ownership flag, so previews, "Try" mode and seeing other players' cosmetics need no download.
- The app layer owns an `IEntitlements` service: `bool Owns(PackId)`. Implementations: Steam (`SteamApps.BIsDlcInstalled(dlcAppId)`, refreshed on the `DlcInstalled_t` callback; Steam caches ownership, so it works offline), school/direct build (a signed licence file that unlocks everything), development build (everything).
- Content declares availability: component definitions and cosmetics carry `"availability": "free"` or `"availability": "pack:<packId>"` ([09](09-components-catalog.md)).
- **The simulation core never checks ownership.** A robot with a Mega or a premium finish simulates identically for everyone; headless verification and Workshop "try" mode depend on this.
- Gating happens only in the app: the Parts Bin shows pack parts with a lock badge; placing one enters "Try" mode (simulate freely, but saving or exporting the project is disabled while it contains unowned paid parts, with a "Buy pack" button). Cosmetics apply only if owned, except when viewing someone else's Workshop robot, where they always render.
- Projects never lose data: an unowned finish falls back to the default look with a badge; the reference stays in the file.
- Purchases: "Buy" calls `SteamFriends.ActivateGameOverlayToStore(dlcAppId)`; no microtransaction API and no server.

## 15. Testing strategy

| Layer | Tests |
|---|---|
| AVR core | Per-instruction unit tests (flags, cycles, PC), interrupt latency tests, timer mode tests (all WGM modes), UART framing/baud tests, ADC timing, EEPROM, watchdog. Golden tests: compile real sketches (Blink, Fade, tone, Servo sweep, Serial echo, I2C scanner, NeoPixel bit-bang) and assert pin waveforms and Serial output against recorded expectations. Differential fuzzing against simavr as an oracle in CI (test-only dependency, never shipped). |
| Electrical | Known-circuit tests (voltage divider, LED + resistor current, pot, H-bridge states, brown-out scenario) with tolerances. |
| Components | Behaviour tests with synthetic pin sequences (HC-SR04 echo width, servo decode, L298N truth table). |
| Integration (headless) | `simcli` runs test projects stored in the repository for N seconds and asserts expected pin waveforms, Serial output, events and robot poses (`expect.json`). Every component and every Notebook claim about behaviour has at least one test project. |
| App | Unity Test Framework play-mode tests for placement/wiring/serialization; smoke test builds in CI. |
| Fidelity | Manual protocol: 20 tutorials run on the real Uno kit with a logic analyser vs the sim; recorded in `docs/fidelity-log.md`. |

## 16. Build and CI

- Git (GitHub or similar). Branches: `main` (stable), `dev`, feature branches. Conventional commits.
- CI: .NET build + tests for `core/` on every push; Unity build (Windows x64, IL2CPP) nightly via GameCI or a self-hosted runner with a Unity licence; artifacts: installer-free folder + Steam depot upload via `steamcmd`/SteamPipe on tagged releases.
- Versioning: SemVer for the app; content schema versions separately.

## 17. Diagnostics and telemetry

- Local logs: `%LOCALAPPDATA%/CoreEngine/logs/` with rotation; "Copy diagnostics" button bundles logs + project.
- Crash reporting: Unity Cloud Diagnostics or Sentry (opt-in dialog on first run).
- Analytics: opt-in only; tutorial completion funnel, error categories and crash rates; no personal data.

## 18. Security

- User sketches are compiled by GCC (no sandbox needed for compile-only; no execution of host code). The compiled program executes only inside the emulator.
- Workshop content is data only; JSON parsing uses strict schemas; meshes are validated (triangle count, bounds).
- No network services in v1 other than Steam APIs.

## 19. Development environment (Windows)

- Unity Hub with Unity 6.6 now and Unity 6.7 LTS once released, each with the Windows Build Support (IL2CPP) module, Visual Studio Community (workloads "Game development with Unity" and "Desktop development with C++", which also provides MSVC and CMake) or Rider, .NET 10 SDK, CMake + MSVC for `native/`, Blender 4.x for content, arduino-cli for tools, Git LFS for binary assets.
- Coding standards: C# nullable enabled, analyzers on, `CoreEngine.Sim` warnings-as-errors; tests required for core changes.

## 20. Open technical questions
See [13-open-questions-and-risks.md](13-open-questions-and-risks.md) §2.
