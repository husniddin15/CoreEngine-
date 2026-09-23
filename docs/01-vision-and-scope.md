# 01 — Vision and Scope

Status: Accepted DRAFT v0.2 (2026-09-23) · Owner: project lead · Product name: **CoreEngine** (owner decision, [13 D2](13-open-questions-and-risks.md); name clearance pending)

---

## 1. One-line pitch

> A Windows 3D robotics workshop where you design a robot body, place **real** electronic parts, wire them **exactly like on a real breadboard**, write **real Arduino code** that is **compiled by the real Arduino toolchain and executed by a cycle-accurate ATmega emulator**, and test the robot in a physics world. If it works in the game, it works on your desk.

## 2. A note on the two meanings of "STEAM / Steam"

The project brief says "a steam application like a game". Both readings are covered and are compatible:

| Meaning | How it applies |
|---|---|
| **STEAM** (Science, Technology, Engineering, Arts, Mathematics) education | The product is a serious educational simulator: every result follows real electronics and physics, and datasheets and explanations are built in. This drives fidelity requirements. |
| **Steam** (Valve's store) | The primary distribution channel for the Windows build. This drives Steamworks integration, Workshop sharing, achievements, and a store page. |

Decision: build a STEAM-education robotics simulator as a **sandbox game** with no missions ([ADR-0008](adr/ADR-0008-pure-sandbox-full-release.md)) and ship it **on Steam** as free to play, plus a direct/educational channel. Details in [12-business-steam-legal.md](12-business-steam-legal.md).

## 3. The problem we solve

1. **Hardware is expensive and fragile.** A starter robot kit (Uno, L298N, 2 motors, HC-SR04, batteries) costs USD 30–60 and a classroom of 20 needs 20 kits, plus replacements when a student connects 9 V to the 5 V pin.
2. **Existing simulators are 2D and/or fake.** Tinkercad/Wokwi have no physics world; robot sims (Webots, VEXcode VR) do not run your actual Arduino binary on your actual wiring; building games (Stormworks, Main Assembly) use fantasy parts and fantasy logic.
3. **Beginners cannot see electricity.** Nothing today shows *why* the motor is slow (the L298N drops ~2 V), *why* the board resets when motors start (brownout), or *why* the ultrasonic reads 0 (wrong trigger timing) — in a place where mistakes are cheap and visible.
4. **Makers cannot test before they buy or print.** The design → print → wire → debug loop takes days; a virtual loop takes minutes and produces the same code, a wiring diagram and STL files for the real build.

## 4. Product pillars (non-negotiable)

| # | Pillar | What it means concretely |
|---|---|---|
| P1 | **Real code, real chip** | The user's `.ino` is compiled with `arduino-cli` + `avr-gcc` into the same `.hex` that would be flashed to a real Uno. The game runs that binary on a cycle-accurate AVR emulator (ATmega328P first). Timers, interrupts, ADC, UART, I2C, SPI, EEPROM and PWM behave per datasheet. No "interpreting" the source. Spec: [05-arduino-emulation-spec.md](05-arduino-emulation-spec.md). |
| P2 | **Real wiring** | Every connection is an electrical net. Breadboards have real internal strips. Pins have voltages and currents. Wrong wiring produces real consequences: dim LEDs, burnt LEDs, motor stalls, brownout resets, blown fuses, "magic smoke". Spec: [06-electrical-simulation-spec.md](06-electrical-simulation-spec.md). |
| P3 | **Real physics** | Motors produce torque from voltage/current; wheels slip; the ultrasonic sensor is a 15° cone with speed-of-sound timing; line sensors read reflectance from the floor texture; batteries sag and run down. Spec: [07-physics-world-sensors-spec.md](07-physics-world-sensors-spec.md). |
| P4 | **Design and build** | An in-app body designer (Tinkercad-style primitives + boolean cuts + mounting-hole helpers) with STL/OBJ import and STL export so the same body can be 3D-printed. Components mount with screws, standoffs or tape and have real dimensions and mass. Spec: [08-body-designer-spec.md](08-body-designer-spec.md). |
| P5 | **Learn by doing** | A pure sandbox: players set their own goals, and a robot works or doesn't, as in real life. Help is built in but never forced: a short tutorial, datasheet cards for every part, plain-language error help, and an event log that explains every failure. Community sharing via Steam Workshop. Spec: [03-game-design.md](03-game-design.md), [10-content-arenas-tutorial-notebook.md](10-content-arenas-tutorial-notebook.md). |

**Fidelity principle:** if the game and reality disagree, reality wins and the game is fixed. The acceptance test for every component is: "a tutorial written for the real part, followed verbatim in the game, works the same way".

## 5. Target users (personas)

| Persona | Needs | What they must be able to do in 30 minutes |
|---|---|---|
| **Dilnoza, 15, school robotics club** (no hardware at home) | Learn Arduino basics, get feedback, feel progress | Finish the tutorial, then build a button-controlled LED and a night-light with help from the datasheet cards |
| **Timur, 20, engineering student** | Prototype a line follower before the competition; tune PID | Load an existing sketch unchanged, wire 5 line sensors + L298N, tune PID in the arena |
| **Ms. Karimova, teacher, 20-PC lab** | Run a semester course with no kits | Share a starter project file with students, let them build and test, export the wiring table and parts list for the one physical kit |
| **Alex, 34, hobbyist/gamer** (Stormworks / Shenzhen I/O player) | A deep builder sandbox with real rules | Design a custom chassis, import an STL, build a sumo bot and share on Workshop |

Primary persona for v1 design decisions: **Dilnoza** (beginner). Secondary: **Timur** (fidelity). The teacher and gamer personas shape v1.x features (classroom mode, Workshop).

## 6. Core loop

```
   +----------+    +----------+    +----------+    +----------+
   |  DESIGN  | -> |   WIRE   | -> |   CODE   | -> |   TEST   |
   |  body +  |    | breadboard|   | .ino in  |    | physics  |
   |  parts   |    | jumpers  |    | editor,  |    | arena,   |
   |          |    | power    |    | compile  |    | sensors  |
   +----------+    +----------+    +----------+    +----+-----+
         ^                                              |
         +------------ iterate (fix wiring / code / body) <--+
                                    |
                  it works -> share on Workshop / export for the real build
```

All four activities happen in one continuous 3D scene (the **Workbench** with an attached **Arena**), so the user can wire while the simulation is paused and re-run instantly.

## 7. Goals and non-goals for the 1.0 release

### Goals
- G1. Arduino **Uno R3** and **Nano** (ATmega328P) emulated cycle-accurately; **Mega 2560** as the first paid pack.
- G2. ≥ 40 components across power, breadboard/passives, motors/drivers, sensors, displays/output, mechanical (tiers in [09-components-catalog.md](09-components-catalog.md)).
- G3. Electrical model with nets, breadboard topology, DC nodal analysis, behavioral component models, and ≥ 10 modelled failure modes.
- G4. Physics arena with ≥ 6 environments (line track, obstacle field, maze, sumo ring, table edge, ramp/sandbox).
- G5. Body designer v1: primitives, snapping, boolean cut, mounting-hole helpers, STL/OBJ import, STL export, compound colliders.
- G6. A 5–10 minute interactive tutorial (controls only) and a Notebook with datasheet cards for every part, error help and "why it broke" cards.
- G7. Sandbox mode; save/load projects; Steam Workshop sharing of projects, bodies and arenas.
- G8. Built-in code editor with syntax highlighting, compile diagnostics mapped to lines, Serial Monitor and Serial Plotter; optional external editor (VS Code) with file watch.
- G9. Runs at 60 fps / 1× real-time with two emulated MCUs on a 2016-era 4-core PC with integrated graphics.
- G10. English, Uzbek (Latin script) and Russian at release.

### Non-goals (explicitly out of 1.0)
- Missions, campaign, challenges, leaderboards and example robots: the game is a pure sandbox ([ADR-0008](adr/ADR-0008-pure-sandbox-full-release.md)).
- Player accounts at release; an optional email account may follow later ([13 D18](13-open-questions-and-risks.md)).
- Non-AVR boards: Uno R4 (ARM), ESP32 (Xtensa/RISC-V), Raspberry Pi Pico (ARM). The architecture keeps a CPU-core interface so these can be added later.
- Full SPICE analog simulation (transistor-level, AC analysis). We model DC and slow transients with behavioral models.
- Freeform sculpting / parametric CAD (sketch–extrude). Import from real CAD instead.
- Multiplayer or shared arenas. (Workshop sharing is asynchronous.)
- ROS, Python or block-based programming (blocks are a v1.x candidate).
- macOS/Linux builds (Linux via Proton is a likely free win; native later).
- VR.

## 8. Platform and distribution

- **OS:** Windows 10 (21H2+) / Windows 11, x64 only. DirectX 11 minimum, DirectX 12 preferred.
- **Distribution:** Steam (primary) as a **free-to-play** game with paid DLC packs: premium real boards such as the Mega 2560, advanced real parts, and customization ([ADR-0007](adr/ADR-0007-monetization-free-to-play-dlc.md), [12 §1](12-business-steam-legal.md)). Plus a DRM-free direct build for schools (site licence, everything unlocked). itch.io optional.
- **Fair play:** nothing paid gives an unfair advantage; paid parts are real products with real specs, and cosmetics never change physics. This protects the fidelity principle in §4.
- **Offline first:** compilation happens locally (bundled toolchain). No account required. Online only for Workshop, updates and optional library installs.

## 9. Success criteria

| Area | Metric | Target |
|---|---|---|
| Learning | A new player finishes the tutorial and builds a working obstacle-avoiding robot using only in-game help | ≤ 2 h median in playtests |
| Fidelity | Standard public tutorials (Arduino Project Hub, official examples) run unchanged | ≥ 95 % of a curated 50-tutorial corpus |
| Fidelity | Emulator timing (millis, PWM freq, servo pulse, tone, UART baud) vs datasheet | 0 cycle error on the timing test suite |
| Performance | 1× real-time, 2 MCUs, 60 fps | Min-spec PC |
| Quality | Crash-free sessions | ≥ 99.5 % |
| Business (first 3 months after release) | Steam reviews | ≥ 100 reviews, ≥ 85 % positive |
| Business (first 3 months after release) | Players and paying players | ≥ 20 000 players; ≥ 3 % buy at least one pack [targets to revisit after launch] |

## 10. Constraints and assumptions

- Solo developer (the owner) working with AI assistance, limited budget; all major third-party code must be free for commercial use (MIT/BSD/Apache/zlib). GPL tools (arduino-cli, avr-gcc) run only as **separate processes** and are shipped with licence texts and a source offer.
- "Arduino" is a registered trademark of Arduino SA; the product name must not contain it and marketing must use "compatible with Arduino® boards" wording (see [12-business-steam-legal.md](12-business-steam-legal.md)).
- The team owns or will buy at least one real Uno R3 starter kit to validate fidelity against hardware (strongly recommended; ~USD 40).
- Windows-only tooling assumptions: `arduino-cli.exe` and the Arduino AVR toolchain run natively on Windows x64.

## 11. Key decisions (summary; full rationale in `docs/adr/`)

| ADR | Decision | Status |
|---|---|---|
| [ADR-0001](adr/ADR-0001-game-engine.md) | Unity 6 + C# for the application (6.6 now, 6.7 LTS when released); simulation core as an engine-independent C# library | Accepted |
| [ADR-0002](adr/ADR-0002-mcu-emulation-approach.md) | Binary-level, cycle-accurate AVR emulation (own C# implementation, informed by the avr8js design); no source interpretation | Accepted |
| [ADR-0003](adr/ADR-0003-compile-pipeline.md) | Local compilation with bundled `arduino-cli` + `arduino:avr` core, invoked as a subprocess | Accepted |
| [ADR-0004](adr/ADR-0004-electrical-simulation.md) | Hybrid solver: event-driven digital nets + DC nodal analysis at 1 kHz + behavioral component models; no SPICE | Accepted |
| [ADR-0005](adr/ADR-0005-body-designer-and-csg.md) | Tinkercad-style primitives with Manifold CSG (native plugin) + STL/OBJ import/export; compound primitive colliders | Proposed |
| [ADR-0006](adr/ADR-0006-time-and-sync-model.md) | Fixed-step lockstep: physics 100 Hz, electrical 1 kHz, MCU cycle-level events; deterministic | Accepted |
| [ADR-0007](adr/ADR-0007-monetization-free-to-play-dlc.md) | Free to play on Steam; paid DLC packs (boards, real parts, customization) as ownership flags; no server, no currency, no pay-to-win | Accepted |
| [ADR-0008](adr/ADR-0008-pure-sandbox-full-release.md) | Pure sandbox with no missions; full release without Early Access; EN/UZ/RU; solo developer with AI | Accepted |

## 12. Document map

| Doc | Purpose |
|---|---|
| [00-README.md](00-README.md) | Index, conventions, how to update docs |
| 01 (this) | Vision, pillars, scope |
| [02-market-research.md](02-market-research.md) | Competitors, gaps, pricing |
| [03-game-design.md](03-game-design.md) | GDD: sandbox, loop, UX, economy, art/audio |
| [04-technical-design.md](04-technical-design.md) | Architecture, modules, data formats, performance, testing |
| [05-arduino-emulation-spec.md](05-arduino-emulation-spec.md) | AVR emulator + compile pipeline |
| [06-electrical-simulation-spec.md](06-electrical-simulation-spec.md) | Nets, solver, component models, failure modes |
| [07-physics-world-sensors-spec.md](07-physics-world-sensors-spec.md) | Physics, actuators, sensors, arenas |
| [08-body-designer-spec.md](08-body-designer-spec.md) | 3D body design, mounting, import/export |
| [09-components-catalog.md](09-components-catalog.md) | Every part with real specs and tier |
| [10-content-arenas-tutorial-notebook.md](10-content-arenas-tutorial-notebook.md) | Arenas, tutorial, Notebook, localization |
| [11-roadmap.md](11-roadmap.md) | Phases, milestones, estimates |
| [12-business-steam-legal.md](12-business-steam-legal.md) | Steam publishing, pricing, licences, trademarks |
| [13-open-questions-and-risks.md](13-open-questions-and-risks.md) | Decisions needed from the owner; risk register |
| [14-glossary.md](14-glossary.md) | Terms |
