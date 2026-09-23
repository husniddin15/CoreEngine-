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
| `Project` | id, name, schema version, boards[], components[], wires[], body (assembly), arena ref, sketches[], settings, created/modified |
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
- Wires: procedural tube meshes along Catmull-Rom splines with sag; rebuilt only on edit; vertex colours; selection outline via render feature.
- Boards: PBR materials with high-resolution silkscreen textures (labels must be readable at 1080p when zoomed).
- LEDs: emissive intensity driven by modelled current (with PWM averaged per frame, plus optional true flicker at low duty).
- Overlays: net voltage tint, current dots (particle system fed from solver), heat glow, sensor cones (transparent meshes), CoM marker.
- Text labels: TextMeshPro world-space labels for pins on hover.

## 11. UI technology

- **UI Toolkit** for all panels (dockable layout, virtualized lists for the parts bin, inspector property drawers, console).
- **Code editor**: v1 implements a custom editor on UI Toolkit/TextMeshPro: line numbers, syntax colouring via a small tokenizer, auto-indent, bracket matching, find/replace, error markers. Evaluate a Monaco/WebView-based editor (Vuplex or similar) only if the custom editor proves inadequate; keep the editor behind an `ICodeEditor` interface. "Open in external editor" + file watching is the escape hatch for power users from day one.
- Localization via Unity Localization package string tables.

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
