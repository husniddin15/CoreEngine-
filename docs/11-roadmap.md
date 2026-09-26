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

Progress (2026-09-25): the owner could not copy or paste code, saw wires going through parts and the body, struggled to see the robot from the angles they wanted, found the turntable in the way while building and the parts blurred in close-ups (D22).
- **Wires** go round everything ([04 §10](04-technical-design.md)): `WireRouter` in the core finds each jumper's way (A* over 5 mm cells against a distance field of the part blocks and body shapes, then pulled straight, rounded and given slack), over the parts, round the plates' edges or through a hole, never through material. Seven core tests check routes against the solids independently, including a plate with and without a hole.
- **Camera** (Tinkercad's navigation): a view cube with Home and Fit, a pivot that Shift+drag or the middle button moves and a double-click sets, the wheel toward the mouse, limits round the robot; the showroom stays above the bench, Wire and the Studio may look from below with the room left out.
- **Turntable** only in the showroom: in Wire and the Studio the robot stands on the bench's measuring mat.
- **Blur** only behind the robot: the depth of field starts behind the robot's far side, so a close-up is sharp all over.
- **Code editor**: selection by mouse and keys, Ctrl+A/C/X/V with Windows' clipboard, undo and redo, word moves, line indenting, a right-click menu.
- **The owner's first own robot did not move** (afternoon). Two reasons:
  - its Uno was fed from the L298N's +5V on VIN, which needs 7–12 V; the check now says exactly that (`fiveVoltOnVin`) instead of only "no power";
  - its sketch set the speed with `analogWrite` on ENA (D5) and ENB (D10), and the emulator did not drive PWM pins at all. `AvrTimer` now models all three timers with their output-compare pins, Timer1 included, and the arena averages each pin over the 10 ms step, so a motor gets 180/255 of the voltage for `analogWrite(ENA, 180)`.
  - The benchmark runs a robot built like the owner's, with the Uno on 5V and the owner's sketch: it drives about a metre in three seconds on the averaged PWM voltage. 164 core tests pass, among them the owner's robot and sketch.
- **Smooth wires** (evening; the owner: wiring "with sharp edges... should be smooth like real life"). The router rounded corners by cutting them (Chaikin), which left kinks wherever two corners were close, most of all where a wire leaves its pin and turns right back. Now every corner is an arc of up to 16 mm radius; two bends close together share the side between them; A* keeps room for bends where it can; jogs beside a pin are taken out ([04 §10](04-technical-design.md)). In the owner's three saved robots the sharpest bend went from 71° to 30° per 2 mm (a radius of about 4 mm). The tube has 12 sides instead of 8. 166 core tests pass; the router's timed tests run alone, after the others, so the emulator's tests cannot slow them.
- **Shaping wires by hand** (night; the owner: "add function that user can control wires ... where it will rotate, where it will pass, where it will be glued"; D23). In Wire a chosen wire shows its points as handles; a drag on the wire pulls out a bend point it passes through; Glue and a click glue it to a plate or part under a blob of hot glue; handles drag points (glue slides over the surfaces); Del removes a point; everything undoes ([03 §6.2](03-game-design.md#62-interaction), [04 §10](04-technical-design.md)). The router lays the wire in stretches between the points, each round everything. 174 core tests pass; the benchmark shapes a wire with the mouse (bend, glue, slide, Del, Ctrl+Z, undo) and photographs the glue close up.
- **Symmetric motors and the view cube** (night; the owner: the second motor was hard to put on the other side and made the robot lopsided; the view cube belongs to building and wiring, on the left). A TT motor's wheel goes on either end of its double shaft (**Wheel to the other end** in the Studio), so the right motor mirrors the left without being turned round; its wheel, box, balance and the wire router follow, and + on M+ still drives it forward. Motors are named by the side their wheel is on (they were all "Left TT" in new robots). The view cube, Home and Fit show only in Wire and the Studio, on the left. 175 core tests pass; the benchmark checks the cube is gone from the showroom and on the left in Wire, and clicks the wheel button.
- **The real TT motor, and pictures of the parts** (night; the owner, with a photo of a real TT motor: "that white rotation part must be center not in side"; "use more images icons in design").
  - The TT motor's shaft now comes out of the middle of the gearbox's side, halfway up it (it was 8.5 mm higher, near the top edge), so a 65 mm wheel's centre is 11 mm under its plate, as on a real robot. The ball caster got 8.5 mm longer brass spacers (43.5 mm from plate to floor) so robots stand level. The kit robots stand 8.5 mm taller.
  - Design version 4 (`DesignMigration.CentreTheShafts`): a saved robot's casters move down by 8.5 mm with their spacers, then the whole robot rises until it stands on the floor again, its free wire points with it; the owner's three robots come out standing as before.
  - Every catalogue part is rendered once into a 160 px picture (`GaragePartPictures.cs`): drawn at 384 px on black and on white, the difference giving its transparency, cropped round the part and scaled down smoothly. The Studio's library tiles, its list of parts and inspector, Wire's Look at buttons and Check & repair's rows show them. Sections and buttons got icons, four of them new (glue, bend, route, question).
  - 176 core tests pass, one of them a version-3 robot standing level after the upgrade; the benchmark builds the scratch robot 8.5 mm higher and passes every check.
- **Viewpoints in the arena** (night; the owner: "add view point to arena mode now it only shows from back"). `ArenaCamera` gives six: Follow, Free (orbit round the robot with the mouse), Top, Side, Robot's eye (from the HC-SR04, beams hidden) and Arena, from a bar over the 3D view, the keys 1 to 6 or C; the wheel zooms, a drag turns the view into Free; each glides in ([03 §8.2](03-game-design.md#82-cameras)). The bar wraps when the view is narrow; drags on the panels and splitters are left alone. The benchmark photographs each viewpoint (-view-follow … -view-arena).
- **A clear picture, and cards with real pictures** (night; the owner, with screenshots of a hazy arena and of the Garage: "why around is so dizzy like fog we need clear game like counter strike 2 ... easy understandable texts and images and easy to select things like cs2 design ... we need real images"; D24).
  - The Garage is sharp all over: its depth of field, film grain and vignette are off. The arena has a clear blue sky, a pale ground out to the horizon (it ended in a grey band under a hazy sky), no fog or darkened corners, and neutral grey shadows (the blue sky had made them deep blue).
  - The six actions are cards with a real picture on top ([03 §3.1](03-game-design.md#31-the-garage-main-screen)), rendered from the game's models when the Garage opens: the kit's parts laid out (Build), an Uno and an L298N joined by six jumpers (Wire), the lab's monitor with the sketch (Code), the bare chassis (Body), the robot itself (Customize), the multimeter (Check & repair). Their lines are short and plain ("Put parts on your robot"), and so is the hint under the view. Three rows of cards fit at 1280 × 720 in all three languages, and the robot bar no longer shrinks under a tall column. The Studio's shape tiles show the shapes themselves; material names wrap onto two lines instead of being cut short ("Cardbo…").
  - The benchmark checks that nothing is blurred and that every card has its picture. The pictures of 8 parts, 8 shapes and 5 cards take 1.2 s once per session in the release player (1.6 s in Mono), spread over the first frames; the benchmark now waits for them before it times the Garage's frames. Every check passes in both players. Frame rates varied from run to run with other programs sharing the laptop; on a quiet run the Garage kept 96 fps and 2.5 ms of GPU time in the Mono player, as before the change.
- **A robot that would not turn** (night; the owner, with screenshots: "it is moving to the forward but when turning right (one motor forward second back) it is just stuck but motors turning slow").
  - Their robot has no ball caster: its weight is ahead of the wheels, so it tips 16° onto the front edge of its plate and drags it. Driving straight, the edge moves as far as the wheels; turning on the spot, it swings round a circle 2.4 times as wide as theirs, so the wheels need far more push, and the motors get only about 3 V, since the L298N drops about 2 V of the four AA cells' 4.9 V.
  - The arena made it worse than a desk would: plastic scraping the floor combined with the floor's friction by averaging (0.65 instead of 0.4), and PhysX's patch friction applies it about twice on an edge ([07 §2](07-physics-world-sensors-spec.md#2-physics-engine-setup-unity-physx)). Now the chassis combines by minimum at half the table's value; the same robot turns at 255° a second instead of 7°, against 374° with a caster.
  - The **balance check** (`RobotStance`, [03 §5.2](03-game-design.md#52-placement-rules-physical-realism)) finds what touches the floor when a two-wheeler tips: the robot card, Check & repair and the arena's event log say "Tips onto its front: the body drags on the floor, so it turns slowly. Put a ball caster there." A stalled motor is now reported whatever the supply: the event took 0.9 A, which a TT motor on an L298N and four AA cells (0.75 A at 3 V) never reaches.
  - Tests: 183 core tests pass, among them the owner's robot tipping 16.2° (as in their screenshot), a caster holding it level, one behind the wheels not holding it, and their turnRight() driving the wheels opposite ways. The benchmark turns their robot on the spot with and without a caster (`SpinInPlace.ino`; `-spikeTurn` runs only that) and checks the warning in the Garage; every check passes in the release player.
- **Physics in the showroom, and black specks** (night; the owner, with a screenshot of their robot floating level on the turntable: "in main page add physics there, only in building and wiring there will be no physics"; "black spots in my render ... appearing and disappearing as particles").
  - The showroom lets the robot settle on the turntable with the arena's physics (`RobotPhysics`, shared now, and `RobotSettle` in a physics scene of its own): the owner's robot without a caster tips 16.2° onto its front in 0.4 s, as the balance check and the arena have it; the kit stands level. Build and Wire keep the design's level pose ([03 §3.1](03-game-design.md#31-the-garage-main-screen)).
  - The specks were the ambient occlusion's blue noise, which Unity changes every frame for a temporal filter the game does not use. Measured in a still showroom at 1920 × 1080: about 5,700 pixels a frame changed; with interleaved-gradient noise, or without the occlusion, none; bloom, "stop NaN" and post-processing made no difference. The occlusion keeps its soft contact shading with the steady noise ([04 §10](04-technical-design.md)).
  - The benchmark checks that a still showroom stays still (0 pixels) and that the robot without a caster comes to rest tipped as the check says; every check passes in the release player.
- **Game UX research, applied** (night; the owner: "research about game design (main page, arena selector, components, building, 3D …) … learn about all popular games' design tactics … and apply them to our game"; D25). Four parallel reviews of platform guidelines (Xbox XAG, Game Accessibility Guidelines, WCAG, NN/g), studies and games (CS2, Valorant, Rocket League, World of Tanks, KSP, Besiege, Poly Bridge, Factorio, Tinkercad Circuits, Minecraft, Portal and others) are in [R5](research/R5-game-ux-research.md), with sources. Applied first:
  - **Readability:** the UI scales with the screen (it stayed 12 px at 1080p, against XAG's 18 px); every font of 9–12 px became 13 px; panels over the lab 95 % opaque and the card pictures on a solid backdrop, so the monitor's code no longer shows through; START's green darkened to 5.4:1 against its white text; colour swatches 24 px; wider scroll bars; the Studio's palette 232 px wide so material names are not cut mid-word.
  - **Main screen:** the arena dropdown became a picture chip joined to START and a picker of arena cards (a real render, goal, "needs a distance sensor" checked against the robot, difficulty); each warning on the robot card got a Fix that opens its screen; Settings holds the language (each in its own language, asked once at first launch) and the interface size (80–150 %), and the language buttons left the top bar.
  - **Build–test–fix:** ▶ Test (F5) on the Studio's and the code editor's bars; ↺ Restart (R) in the arena.
  - **Building:** the Balance view — the centre of mass as a yellow ball over the patch the robot stands on, green or red — comes on by itself when the robot tips (for the owner's robot: a red strip between the wheels and "Tips onto its front …").
  - The benchmark clicks the arena chip, opens Settings, clicks the no-caster robot's Fix into Build and checks the Balance view and its line; every check passes in the release player, 136 fps in the Garage and 141 in the arena. Next from R5: numbered workflow cards with a "Next" tag, hints at the moment of failure, wiring aids, part cards with search, mirror mode, a first-drive tutorial.
- **Pieces that hold together** (night; the owner, with a screenshot of their robot pushing a box while its serial said "Distance: 400 cm" and a plate lay apart on the floor: "how it is even working where is logic and physics"; D26).
  - Their plate had been dragged 17.5 cm to the left and 8 cm forward in the Studio. The game made one rigid body of the whole design wherever its pieces were, so the Uno, the motors and the sensor floated in the air and drove along with the plate as if on an invisible arm. The sensor also sat on a wedge's slope, looking 34° up, and pressed against the box it got no echo, so it read its 400 cm limit.
  - Now only what touches the robot belongs to it (`RobotPieces`, [03 §5.2](03-game-design.md#52-placement-rules-physical-realism)): loose pieces fall off as bodies of their own on the turntable and in the arena and lie where they fell (their plate, 23 cm from the turntable's middle, falls off its edge onto the desk and stays there as it turns); the robot card and Check & repair name them with a Fix into Build, where each is boxed in red; the card warns when the HC-SR04 is tipped more than 15°. The balance check and the robot's mass count the robot only. A lone motor left driving its wheel had flown 300 m: the chassis's spin is capped and its inertia kept at least twice a wheel's.
  - Tests: 188 core tests pass, among them the kits holding together, the owner's dragged plate leaving every part loose and their sensor's 33.7° tilt. The benchmark shows the dragged-plate robot in the Garage (card, turntable, Build) and drives it in the arena: 6 groups fall off, the plate stays where it fell (0.0 cm) while the rest rolls 75 cm. Every check passes in the release player, 140 fps in the Garage and in the arena.
- **Wires between pieces that fell apart** (2026-09-26; the owner, with a screenshot of a lone TT motor turning by a wall at +3.6 V while its battery lay elsewhere: "how the heck it is rotating without power").
  - Their Robot 3 still had its plate 17 cm away, so every part fell off on its own and the robot was one motor. The wires to the loose pieces were hidden, but the circuit was still worked out from the whole design, as if every wire were always plugged in at any distance.
  - Now a wire between two bodies hangs between its pins (`WireTethers`), as long as it was laid. In the arena, once its pins are farther apart than that plus 5 mm, it pulls out; the event log names it, and the circuit is worked out again without it, so a motor, the board, the driver, a servo or the HC-SR04 (`HcSr04.Live`) that loses its wire stops. A wire with both ends on one loose piece falls with it. The inspector's L298N supply shows 0 V while the driver has no battery wired, instead of the battery's charge.
  - Measured with the dragged-plate robot: all 18 of its wires run between pieces. As the Uno and the L298N fell apart, 8 jumpers pulled out (IN1–IN4, ENA, ENB, TRIG, ECHO) and the motor, which before rolled 75 cm, stopped within 1 cm. The 10 longer wires (battery, motor leads, power) still hang, so the Uno keeps running, as it would on a desk. 189 core tests pass (a sensor whose wire came off never answers); every benchmark check passes in the release player, 131 fps in the Garage and 135 in the arena.
- **Tests.** 152 core tests pass. The benchmark clicks the view cube, pans to the limit, double-clicks a pivot, zooms in as far as it goes, looks from below in the Studio, and copies, pastes, cuts and undoes in the Code window by keys; all pass in the release player, which lays the kit's 16 wires from nothing in 20 ms and keeps 140 fps in the Garage and 141 fps in the arena.

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
