# 11 — Roadmap, Milestones and Estimates

Status: DRAFT v0.2 (2026-09-23) · Decisions: [ADR-0001](adr/ADR-0001-game-engine.md) (Unity 6.6, then 6.7 LTS), [ADR-0007](adr/ADR-0007-monetization-free-to-play-dlc.md) (free to play with paid packs), [ADR-0008](adr/ADR-0008-pure-sandbox-full-release.md) (pure sandbox, full release, solo with AI)

---

## 1. Planning assumptions

| Assumption | Value used below |
|---|---|
| Team | **Solo: the owner, working with AI assistance** for code, documentation, translation drafts and some art ([13 D3](13-open-questions-and-risks.md)) |
| Hours | 40 h/week; part-time work (≈ 20 h/week) roughly doubles calendar time |
| Engine | Unity 6.6 now, Unity 6.7 LTS when released (expected at the end of 2026) + C# core ([ADR-0001](adr/ADR-0001-game-engine.md)) |
| Release model | One **1.0 release** on Steam, free to play, with the Mega 2560 Pack, two customization packs and a supporter bundle. No Early Access; Steam Playtest and a demo build provide feedback before release |
| Game | Pure sandbox: no missions, challenges, leaderboards or example robots; a short tutorial and the Notebook help players ([10](10-content-arenas-tutorial-notebook.md)) |
| Languages | English, Uzbek (Latin) and Russian at release |
| Hardware | The owner's Uno kit; a USB logic analyser (≈ $10–15) if the kit has none |
| Estimates | Engineering days of focused work, including a 30 % buffer, **without** assuming any speed-up from AI. Re-estimate after Phase 0 using the real pace |

## 2. Phase overview

```
                                      first plan      re-estimate after Phase 0 (full-time)
Phase 0  Foundations & spikes        months 1–2      done 2026-09-23
Phase 1  Vertical slice               months 2–9      months 1–5    "Obstacle avoider end-to-end"
Phase 2  Breadth & content            months 9–18     months 5–11   "Everything for 1.0"
Phase 3  Steam release 1.0            months 18–22    months 11–13 (plan 15–18 with buffer)
Phase 4  After release                ongoing         ongoing       paid packs and updates
```

Total ≈ 450 working days ≈ 21 months of full-time work (optimistic) → plan 24–28 months to the 1.0 release, **without** any AI speed-up. **Re-estimated after Phase 0: ≈ 235–270 working days, plan 15–18 months** ([§3](#re-estimate-after-phase-0-2026-09-23)); 3D art, testing, translation review and decisions still take the owner's time.

## 3. Phase 0 — Foundations and technical spikes (≈ 7 weeks)

Goal: retire the biggest technical risks with small prototypes before building the real thing.

| # | Spike | Exit criterion | Days |
|---|---|---|---|
| 0.1 | Repository, CI, coding standards | `core/` builds and its tests run in CI; docs indexed | 2 |
| 0.2 | Toolchain packaging | On a clean Windows machine with a Cyrillic user name, the bundled `arduino-cli` + `arduino:avr` core compiles Blink offline in < 3 s warm; artefacts parsed | 4 |
| 0.3 | Emulator core spike (C#) | ATmega328P core runs Blink and a Serial sketch in a console app with a correct 1.000 s period and 9600-baud framing; benchmark ≥ 110 M cycles/s in a .NET release build (record the number; IL2CPP measured in 0.5) | 12 |
| 0.4 | Unity robot spike | URP scene, 2WD robot with `ArticulationBody`, wheels driven by a torque value, HC-SR04 cone raycast visualised, 60 fps on the test laptop | 6 |
| 0.5 | Manifold P/Invoke spike + IL2CPP performance check | Union/difference of two boxes and a cylinder hole in Unity in < 50 ms; emulator loop benchmark inside an IL2CPP build | 5 |
| 0.6 | UI Toolkit panels + code editor + localization check | Dockable panels; a 500-line sketch with highlighting scrolls smoothly; one panel switches between English, Uzbek and Russian with correct fonts | 4 |
| 0.7 | Decision review | ADR-0005 accepted or changed; ADR-0001 re-checked against the performance results; schedule re-estimated | 1 |

Total ≈ 34 days.

**Progress (2026-09-23):**

| # | Status |
|---|---|
| 0.1 | Done: Git repository, C# solution (`core/`), CI workflow (runs once the repository is on GitHub). |
| 0.2 | Mostly done: `tools/fetch-toolchain.ps1` installs arduino-cli 1.5.1 and the AVR core 1.8.8 offline-ready; Blink compiles to the same 924 bytes as the Arduino IDE; GCC errors are mapped to sketch lines. Open: repeat compiles take 4.3–5.3 s against the 3 s target ([13 §2 Q11](13-open-questions-and-risks.md)), and the clean-machine test with a Cyrillic user name. |
| 0.3 | Done: the ATmega328P emulator runs the compiled Blink (LED toggles every 1000.010 ms), Serial at 9600 and 115200 baud with exact frame timing, and `millis()` matching emulated time; 65 automated tests; 130–156 M cycles/s with the .NET 10 JIT. |
| 0.4 | Done: `app/` Unity 6.6 project with URP; a 2WD robot on `ArticulationBody` wheels driven by the TT-motor and L298N models; the real compiled `ObstacleAvoider` sketch runs on the emulator inside the 10 ms physics step and reads a 17-ray HC-SR04 cone. IL2CPP player: 142 fps average (vsync) with one isolated 62 ms frame in 10 s; the emulator costs 0.8 ms per 10 ms step. The robot also found a real HC-SR04 limit: it pushed against a box beside the narrow beam ([07 §5.1](07-physics-world-sensors-spec.md)); the sketch now backs out when the distance stops changing. |
| 0.5 | Done: Manifold v3.5.3 built as one self-contained `manifoldc.dll` and called through P/Invoke from IL2CPP. Two boxes and a hole: 1.6 ms (target < 50 ms); a 51-shape chassis plate: 34 ms (target < 100 ms); results in [ADR-0005](adr/ADR-0005-body-designer-and-csg.md), now accepted. Emulator in the IL2CPP player: 223 M cycles/s (target 110). |
| 0.6 | Done: UI Toolkit panels docked around the running 3D view (code, inspector, console, Serial Monitor with the robot's live output, event log); tabs move between dock areas by dragging; split views resize. The custom code editor shows a 522-line sketch with line numbers, colouring, caret, typing and auto-indent; in the IL2CPP player it scrolls at the display's 144 fps with no frame over 20 ms, and a keystroke costs about 0.1 ms. English, Uzbek and Russian switch live with Segoe UI and Consolas from Windows ([10 §5](10-content-arenas-tutorial-notebook.md)); glyphs are pre-loaded at start-up, because a glyph drawn for the first time mid-scroll caused frames of up to 200 ms. |
| 0.7 | Done: ADR-0005 accepted; ADR-0001 re-checked and kept (its Phase 0 check section); schedule re-estimated below. |

### Re-estimate after Phase 0 (2026-09-23)

Phase 0 was planned at 34 days of focused work. Spikes 0.1–0.6 took about two calendar days of AI-driven work plus the owner's time to install tools. Spikes are the kind of work AI speeds up most (new code, clear goals, automatic checks), so that pace does not carry over one to one. The remaining 422 planned days (including the Garage added on 2026-09-23) are therefore split by kind of work:

| Kind of work | Planned days | Factor | Re-estimate |
|---|---|---|---|
| Code-heavy work packages: emulator, electrical core, components, workbench, Code Desk, world, bench tools, save/load, localization framework, Garage, Body Studio, Mega 2560 profile, Steamworks, shop, performance pass, the code half of customization and arenas, fixes during QA | ≈ 275 | 0.4–0.5 | ≈ 110–138 |
| Art, content, translation review, playtests, testing, store page and trailer, legal and admin, release buffer | ≈ 147 | 0.85–0.9 | ≈ 125–132 |
| **Remaining total** | **≈ 422** | | **≈ 235–270** |

That is about 11–13 months of full-time work, so the plan becomes **15–18 months to the 1.0 release** (was 24–28), at 40 hours a week; part-time work roughly doubles the calendar time. The factors are an assumption to check: at M1 (vertical slice) the real Phase 1 pace replaces them. The owner's own time for 3D models, reviews, playtests and hardware checks is now the main limit, not programming. The original estimate stays as the upper bound without any AI speed-up.

## 4. Phase 1 — Vertical slice (months 2–9)

Goal: one complete path through all pillars with a small parts set, playable by outsiders.

Scope: Uno R3, half breadboard, jumpers, LED, resistor, button, 4×AA holder, L298N, 2 × TT motors, wheels, caster, one chassis, HC-SR04, USB cable. Electrical solver v1 (digital + DC MNA); failure modes: LED burn-out, USB fuse trip, brown-out reset. Code editor v1 + Serial Monitor. Arenas: workbench mat, floor, obstacle field. Tutorial v0 and Notebook v0. Save/load.

| Work package | Days |
|---|---|
| Emulator: complete instruction set, timers 0/1/2, USART, ADC, EEPROM, interrupts, watchdog; instruction and golden tests | 30 |
| Electrical core: nets, breadboard topology, MNA, MCU pin model, battery + Uno power path, failure accumulators | 20 |
| Component behaviours: LED, resistor, button, L298N, DC motor, HC-SR04, battery, USB bridge | 12 |
| Unity workbench: parts bin, placement/snapping, breadboard insertion, wiring tool with routing, inspector, undo/redo | 25 |
| Code Desk: editor, compile pipeline integration, diagnostics, Serial Monitor | 12 |
| World: physics bridge, motor → joint, sensor queries, obstacle arena, cameras, overlays (pin LEDs, cone) | 12 |
| Tutorial v0 + Notebook v0 (datasheet cards for the slice's parts, first 10 error-help cards) | 6 |
| Bench tools v0: voltmeter probe, event log panel, basic telemetry graph | 5 |
| Localization framework from day one: string tables, fonts for Uzbek Latin and Cyrillic; every new UI text goes through the tables | 3 |
| Save/load, autosave, settings, logging | 6 |
| Garage hub v1 ([ADR-0009](adr/ADR-0009-garage-main-screen.md)): robot bar, robot card, action column, Customize with free finishes, Check & repair, START with the arena picker, robot list in the save file. A working prototype exists since 2026-09-24 (`app/Assets/Spike/Scripts/Garage/`) | 6 |
| Playtest with 5 outsiders, fixes | 8 |

Total ≈ 145 days. Exit: 5 testers finish the tutorial and build a working obstacle-avoiding robot using only in-game help; 5 sketches verified against the real Uno with the logic analyser.

Progress (2026-09-24): Build, Wire and Body work in the Garage prototype ([03 §3.1](03-game-design.md)). A robot is now data (`CoreEngine.Sim.Design`: parts with real pins, wires, the circuit they make, the body), edited in the Garage and turned into the arena robot, so a player can build the obstacle avoider from an empty chassis, wire it pin by pin and drive it. Covered in part: the workbench package (parts bin, placement with 5 mm snapping, wiring tool without routing, a simple inspector, undo without redo), the power and pin rules of the electrical core in their digital form ([06 §3.8](06-electrical-simulation-spec.md)), and a first Body Studio kernel with STL export (a Phase 2 package, [08 §7](08-body-designer-spec.md)). Still to do in Phase 1: the breadboard and its insertion, wire routing, the analogue solver, redo, and the `.rbp` project file. After the owner's first try (wires could not be made: pins were small and hidden in the headers, and a drag turned the camera), wiring gained drag-to-wire, visible pin markers, "Look at" with pin names, connecting from lists and the one-jumper-per-pin rule, and the benchmark now drives these through the same mouse code a player uses. Rendering gained soft shadows, ambient occlusion and post-processing ([04 §10](04-technical-design.md)).

Progress (2026-09-24, afternoon): the owner found the Garage "like cartoon" and asked for three things: a real-looking render, a CAD-like Body Studio (simple shapes for beginners, complex models for experts, STL/OBJ upload), and a better menu.
- **Render.** The Garage is now a photographed robotics lab built from CC0 Poly Haven assets (D19, [04 §10](04-technical-design.md#10-rendering)).
- **Menu.** It has icons, a status chip, action tiles and thumbnail cards, and fits English, Uzbek and Russian ([03 §3.1](03-game-design.md#31-the-garage-main-screen)).
- **Body Studio.** It works in the Garage ([08 §3.1](08-body-designer-spec.md)):
  - a shape palette with solids and holes;
  - move, turn and size handles with snapping, and typed values;
  - outlines drawn on the deck;
  - STL/OBJ upload through Windows' Open dialog;
  - duplicate, mirror copy, undo and redo (redo is new everywhere in the Garage).

  The benchmark drives every tool through the same mouse and key code a player uses. Still to do, from the Phase 2 Body Studio package: the workplane on faces, align, groups, hole patterns, measuring and mount points.
- **Speed.** The release player keeps 142 fps in the Garage and the arena.

Progress (2026-09-24, evening): the owner asked for a chassis the player makes, real-looking parts, and a bright lab.
- **Build from nothing** (D21, [08 §3.1](08-body-designer-spec.md)):
  - A new robot is empty. The body is built from shapes in real materials, Tinkercad-style, with groups whose holes cut only their own solids.
  - Parts are placed on the robot with the mouse and turned about all three axes, but never resized. Their mounting, the wheels' drive and the sensor's aim follow from their pose.
  - Old saves are moved to the new model by `DesignMigration`.
- **Tinkercad's handles** replace the Move/Turn/Size tools: corner, edge and top squares, the lift cone, curled arrows with a protractor, dimension lines.
- **Part models** ([09 Appendix B](09-components-catalog.md#appendix-b--3d-asset-production-notes)):
  - The Uno, L298N, HC-SR04, TT motor with its wheel, 4×AA holder and ball caster are modelled from real dimensions, with painted boards.
  - Two new parts: an SG90 servo (its horn follows the pulses on its pin) and an LED module (it lights from its pin).
  - The boards' LEDs show the running sketch: ON, L, TX and PWR.
- **The lab** (D20, [03 §3.1](03-game-design.md#31-the-garage-main-screen)):
  - A bright white engineering lab around the turntable: a measuring mat, instruments with lit screens, a pegboard of tools, parts shelves, a whiteboard, a window with blinds.
  - It is modelled in code and baked with bounced light, with no downloads; the bake takes about a minute.
- **Tests.** 145 core tests pass. The benchmark builds the kit robot from nothing with the mouse, drags every handle, wires it, and drives it in the arena at about 130–140 fps.
- **Speed.** In the release player the Garage runs at 135 fps and the arena at 141 fps. The part models are built the first time they are needed: 0.43 s for all eight, 0.14 s of it the Uno. Making the board painter faster cut this time to a third (the Mono player went from 4.1 s to 1.3 s).

## 5. Phase 2 — Breadth and content (months 9–18)

| Work package | Days |
|---|---|
| Components to ≥ 40 (servo, line sensors, IR, IMU, encoders, LCD/OLED, NeoPixel, buzzer, pot, LDR, relays, transistors, steppers, power variants) with tests | 30 |
| Emulator: I2C/SPI bit-level, Nano board, SoftwareSerial/NeoPixel golden tests, performance pass | 12 |
| Mega 2560 Pack: ATmega2560 profile (timers 3/4/5, USART1–3, ports A–L, extra interrupts, 3-byte PC), board model and 3D asset, golden tests | 12 |
| Customization system: visual finishes separate from physical materials, decals, board and wire skins, workbench themes; art for two launch packs | 14 |
| Body Studio v1: primitives, snapping, workplanes, align, Manifold booleans, hole-pattern helper, import/export, colliders, mass/CoM | 30 |
| Arenas (9) + arena editor + floor reflectance map + measuring tools (tape measure, stopwatch, trigger lines) | 16 |
| Notebook complete in English: ≈ 60 datasheet cards, 30 error-help cards, 28 "why it broke" cards | 15 |
| Tutorial final + achievements | 5 |
| Telemetry panel completion, ammeter/clamp/ohmmeter modes, logic probe, overlays (current dots, heat) | 8 |
| 3D models for ≈ 65 unique parts (Blender, AI-assisted modelling and bought assets where licences allow), audio synthesis, VFX | 40 |
| Uzbek + Russian translation of all text: AI drafts, owner review, in-game check of every screen | 8 |
| Closed test with 30–50 players through Steam Playtest, fixes | 12 |

Total ≈ 202 days.

## 6. Phase 3 — Steam release 1.0 (months 18–22)

| Work package | Days |
|---|---|
| Steamworks: achievements, Cloud, Workshop upload/browse, Families check | 10 |
| Entitlements and Shop: DLC ownership service, lock badges, "Try" mode, Shop panel with previews, Steam overlay store links | 8 |
| Store page and DLC pages in three languages, capsule art, trailer, screenshots, press kit | 12 |
| Public demo or open Steam Playtest (replaces Early Access feedback) | 5 |
| Performance and min-spec pass, accessibility, crash reporting, diagnostics bundle | 8 |
| QA sweep, regression test projects in CI, bug fixing | 18 |
| Legal and admin: licences file, trademark acknowledgements, AI-content disclosure, EULA/privacy for the direct channel, merchant-of-record setup, company bank account | 4 |
| Release buffer | 10 |

Total ≈ 75 days. The Steam "Coming soon" page should go live at least 6 months before release (around month 15–16) to collect wishlists.

## 7. Phase 4 — After release

Paid packs ([09 Appendix C](09-components-catalog.md)): one new customization pack every 1–2 months (art only), part packs (Advanced Sensors, Precision Motion, Display) every 2–3 months, and board packs one at a time (ESP32, Uno R4, Pico; each needs a new CPU core, ≈ 2–4 months [estimate]).

Other work, ordered by expected player value: debugger (breakpoints, watches, GDB server) · schematic view · optional email accounts ([13 D18](13-open-questions-and-risks.md)) · classroom tools for the school licence · block coding (only if players ask for it) · DXF export and fillets · more arenas · Linux/Proton verification.

## 8. Milestone summary

| Milestone | Month, first plan (full-time) | Month, re-estimate after Phase 0 |
|---|---|---|
| M0 Spikes done, schedule re-estimated | 2 | Done 2026-09-23 |
| M1 Vertical slice playable by outsiders | 8–9 | 4–5 |
| M2 Steam "Coming soon" page live | 15–16 | 9–10 |
| M3 Content complete, closed Steam Playtest | 18 | 11–13 |
| M4 **1.0 release**: free to play, Mega 2560 Pack, two customization packs, supporter bundle | 22 (plan 24–28) | 13 (plan 15–18) |
| M5 First post-release pack | release + 2 | release + 2 |

## 9. Sequencing rules
1. The emulator and the compile pipeline come first: every other feature is untestable without them.
2. Fidelity tests against real hardware start in Phase 1, not at the end.
3. Content (art, Notebook cards, arenas) starts as soon as the vertical slice's data formats are stable; formats get a schema version from day one.
4. Steam integration is late, but the store page goes up early: wishlists need at least 6 months of visibility.
5. Nothing enters the release build without a unit test or a headless test project.
6. Every text the player reads goes through the string tables from day one, in all three languages.
7. AI writes first drafts of code, cards and translations; the owner reviews and tests everything before it ships. AI-generated content that ships to players is disclosed on Steam ([12 §5](12-business-steam-legal.md)).
