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
Phase 0  Foundations & spikes        months 1–2      (≈ 7 weeks)
Phase 1  Vertical slice               months 2–9      "Obstacle avoider end-to-end"
Phase 2  Breadth & content            months 9–18     "Everything for 1.0"
Phase 3  Steam release 1.0            months 18–22
Phase 4  After release                ongoing         paid packs and updates
```

Total ≈ 450 working days ≈ 21 months of full-time work (optimistic) → **plan 24–28 months to the 1.0 release**. AI assistance can shorten the programming work noticeably, but 3D art, testing, translation review and decisions still take the owner's time. The plan is re-estimated at the end of Phase 0.

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
| Playtest with 5 outsiders, fixes | 8 |

Total ≈ 139 days. Exit: 5 testers finish the tutorial and build a working obstacle-avoiding robot using only in-game help; 5 sketches verified against the real Uno with the logic analyser.

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

| Milestone | Month (full-time, optimistic) |
|---|---|
| M0 Spikes done, schedule re-estimated | 2 |
| M1 Vertical slice playable by outsiders | 8–9 |
| M2 Steam "Coming soon" page live | 15–16 |
| M3 Content complete, closed Steam Playtest | 18 |
| M4 **1.0 release**: free to play, Mega 2560 Pack, two customization packs, supporter bundle | 22 (plan 24–28) |
| M5 First post-release pack | release + 2 |

## 9. Sequencing rules
1. The emulator and the compile pipeline come first: every other feature is untestable without them.
2. Fidelity tests against real hardware start in Phase 1, not at the end.
3. Content (art, Notebook cards, arenas) starts as soon as the vertical slice's data formats are stable; formats get a schema version from day one.
4. Steam integration is late, but the store page goes up early: wishlists need at least 6 months of visibility.
5. Nothing enters the release build without a unit test or a headless test project.
6. Every text the player reads goes through the string tables from day one, in all three languages.
7. AI writes first drafts of code, cards and translations; the owner reviews and tests everything before it ships. AI-generated content that ships to players is disclosed on Steam ([12 §5](12-business-steam-legal.md)).
