# 03 — Game Design Document (GDD)

Status: Accepted DRAFT v0.2 (2026-09-23) · Depends on: [01-vision-and-scope.md](01-vision-and-scope.md) · Content (arenas, tutorial, Notebook): [10-content-arenas-tutorial-notebook.md](10-content-arenas-tutorial-notebook.md) · Decisions: [ADR-0007](adr/ADR-0007-monetization-free-to-play-dlc.md), [ADR-0008](adr/ADR-0008-pure-sandbox-full-release.md)

---

## 1. Player fantasy and tone

"I have my own robotics workbench, an unlimited parts bin, and nothing can break for real."

- Tone: warm, practical maker-space. No enemies, no goals set by the game, no timers. Failure is informative ("the LED burned because 40 mA flowed through it; the datasheet limit is 30 mA"), never punishing.
- The world is small and tactile: a desk with a cutting mat (1 cm grid), a drawer of parts, and an arena the size of a table or a classroom floor.
- Every object is a real, buyable part with its real name, real dimensions and a real datasheet available in the in-game Notebook.

## 2. Modes

| Mode | Purpose | Unlock |
|---|---|---|
| **Sandbox** (the game) | Free building and testing. All free parts and all arenas are available from the start; there are no goals, scores or unlocks. Players set their own goals. Save unlimited projects. | From start |
| **Tutorial** | A 5–10 minute interactive introduction to the controls; skippable; can be replayed from the main menu ([10 §3](10-content-arenas-tutorial-notebook.md)) | First launch |
| **Community** | Browse/subscribe to Steam Workshop items (robots, bodies, arenas). Open and "remix" them. | From start (needs Steam online) |
| **Shop** | Browse the paid packs (boards, advanced parts, customization, supporter bundle) with previews; "Try" places a pack's part or finish in the current project without saving it; "Buy" opens the pack's Steam store page in the Steam overlay ([12 §1](12-business-steam-legal.md)) | From start |

## 3. Spaces

All spaces are parts of one continuous 3D scene so switching is instant and the simulation state persists when paused.

| Space | What happens there |
|---|---|
| **Workbench** | Build and wire. Robot sits on a stand or the mat. Parts Bin drawer on the left, Tools rack (multimeter, logic probe, screwdriver, tape, wire cutter) on the right. Camera orbits the desk. |
| **Body Studio** | Sub-mode of the Workbench for designing the chassis (see [08-body-designer-spec.md](08-body-designer-spec.md)). Same camera. |
| **Code Desk** | A dockable IDE panel (half-screen or full-screen). One sketch per board. Serial Monitor/Plotter docked below. |
| **Arena** | "Send to Arena" moves the robot to the chosen environment's start pad. Environments: line track, obstacle field, maze, sumo ring, table edge, ramp/sandbox, empty floor. Environment editing tools are available here. |
| **Notebook** | Datasheet cards for every part, error help, "why it broke" cards, glossary, and the event log ("what happened and why"). Offline, in English, Uzbek and Russian. No lessons ([10 §4](10-content-arenas-tutorial-notebook.md)). |

## 4. Core loop (detailed)

1. **Build**: drag parts from the bin; snap to body mount points or breadboard; rotate; place body parts.
2. **Wire**: pin-to-pin jumpers, breadboard rows, screw terminals, battery clips. Run the wiring check (optional, warnings only).
3. **Code**: write or paste the sketch; press **Upload** (compile + load + reset).
4. **Test**: press **Play**. Watch LEDs, Serial output, telemetry. Move to an Arena to test driving robots.
5. **Diagnose**: hover nets for voltage; use the multimeter; read the event log ("brown-out reset at t=3.21 s"); fix; re-run (state resets deterministically).
6. **Share or build it for real**: when the robot does what the player wanted, they can **Share** it (Workshop) or **Export** it (STL + wiring table + parts list + .ino zip for the real build). The game never judges success; the robot simply works or doesn't, as in real life.

Target iteration time from "edit code" to "robot moving again": **< 3 seconds** (compile of a typical sketch is 1–2 s with a warm toolchain).

## 5. Build mode

### 5.1 Parts Bin
- Categories: Boards · Breadboards & Wires · Passives (R, C, D, LED, buttons, pots) · Power · Motors & Drivers · Sensors · Displays & Output · Communication · Mechanical (chassis, wheels, brackets, standoffs, screws).
- Each part card: photo-like render, name, one-line description, key specs (voltage, current, interface), datasheet button. Parts from paid packs show a lock badge and a **Try** button; all free parts are available from the start.
- Search and filter (by interface: I2C, PWM, analog; by voltage).

### 5.2 Placement rules (physical realism)
- Parts have real dimensions and mass ([09-components-catalog.md](09-components-catalog.md)). They cannot interpenetrate; the placement ghost turns red when colliding.
- **Through-hole parts** (LEDs, resistors, buttons, IC-style modules with 2.54 mm pin rows, Nano) snap into breadboard holes. The breadboard's internal strips define connectivity.
- **Modules** (Uno, L298N, HC-SR04, sensor modules) either sit on the body via **mount points** (screw holes → standoffs/screws), **double-sided tape** (anywhere on a flat face), or **breadboard-friendly headers** (Nano, some sensors).
- **Motors** snap to motor mounts (TT motor bracket, N20 bracket, servo horn/bracket). Wheels snap to shafts (D-shaft or servo horn).
- Body parts are designed in Body Studio or imported; mount points can be added to any face (see 08).
- Overlays: centre of mass marker, total mass, wheelbase, ground clearance.

### 5.3 Interaction
- Drag & drop from bin; **R** rotate 90°, **Shift+R** 15°; **G** grab; **Del** remove; **Ctrl+D** duplicate (with wiring cleared); **Ctrl+Z/Y** undo/redo (all modes).
- Snapping: mount holes, breadboard grid (2.54 mm), body grid (1 mm), face-align.
- Inspector shows: name, position, mount status, pin table (pin → net → voltage when running), part-specific settings (e.g., potentiometer knob angle, DIP switch positions, module jumper caps such as the L298N 5V-EN jumper, HC-05 mode).

## 6. Wiring mode

### 6.1 Wire types
| Type | Notes |
|---|---|
| Dupont jumper M-M, M-F, F-F | Length is flexible: a jumper stretches or shortens to fit the distance between its two ends, so wiring is quick and easy. Colours: red, black, yellow, green, blue, white, orange, purple, grey, brown. The real-build export rounds each wire up to the next standard jumper length (10, 20 or 30 cm) so the parts list can be bought. |
| Solid-core breadboard wire | Cut to length; lies flat; ideal for tidy breadboards. |
| Screw-terminal connection | L298N motor/power terminals, some sensor breakout boards: bare wire ends into terminals. |
| Battery leads / clips | 9 V snap, 4×AA case leads (with or without switch), 18650 holder leads, DC barrel plug, JST. |
| USB cable | Connects the board to the "PC" (virtual). Supplies 5 V (500 mA limit) and the Serial Monitor. Detachable: running on battery only is a real-life step. |
| Motor leads | TT motors come with solder tabs; the kit variant with pre-soldered leads is default. |

### 6.2 Interaction
- Click a pin/hole/terminal → click another → wire created. Pins highlight on hover with name and function ("D9 · PWM · OC1A"). Illegal targets (e.g., wire to plastic) are not selectable.
- Wires auto-route as smooth curves with gravity sag; drag any point to add a control point; colour picker; **Tidy** bundles nearby wires; **Hide wires** toggle; **X-ray** makes boards translucent to see breadboard strips.
- Net inspection: hover a wire/pin → the whole net glows; running sim shows voltage and (where measurable) current on the tooltip.
- **Schematic view** (v1.x): auto-generated 2D schematic/netlist diagram from the 3D wiring; MVP ships a netlist table in the Inspector.

### 6.3 Wiring check (advisory, run manually or on Play)
Warnings never block Play. Examples: VCC↔GND short (blocking is *off* by default — the fuse/regulator model handles it), no power to the board, output pin driven against output pin, LED without series resistor, motor connected directly to a GPIO, 9 V on the 5 V pin, floating input pins used with `digitalRead`, I2C without pull-ups, sensor powered from 3.3 V pin beyond 50 mA, L298N 5V-EN jumper present with Vs > 12 V, HC-SR04 echo to a 3.3 V board (not v1), serial monitor open while D0/D1 are used. Each warning has a "Why?" card linking to the Notebook.

### 6.4 Bench tools
| Tool | Behaviour |
|---|---|
| Multimeter | Probe two points: DC volts; resistance (sim paused); current in **series mode** (insert inline into a wire — realistic) or **clamp mode** (non-invasive; labelled "virtual clamp"). Continuity beep. |
| Logic probe | Shows H / L / floating / pulsing with frequency estimate. |
| Logic analyser / scope (v1.x) | 4 channels, 1 µs resolution, decoders for UART/I2C/SPI/PWM/servo. |
| Thermometer overlay | Shows component temperature (L298N heatsink, regulator, motors). |
| Screwdriver / tape / wire cutter | Mounting actions in Build mode. |

## 7. Code mode

- Editor: Arduino C++ syntax highlighting, line numbers, auto-indent, bracket matching, find/replace, multiple tabs (sketch `.ino` + `.h/.cpp`), examples menu identical in structure to the Arduino IDE (01.Basics, 02.Digital, 03.Analog, …, plus library examples), library manager (bundled offline set; online install when available).
- **Upload** (Ctrl+U): compile with the bundled toolchain → load `.hex` → reset the board. **Verify** (Ctrl+R): compile only. Progress and full compiler output in the Console.
- Diagnostics: errors/warnings parsed from GCC output, shown inline and in a list; click to jump. Plain-language explanation cards for the 30 most common beginner errors (missing semicolon, undeclared identifier, wrong case, missing library include, `Serial` used before `begin`).
- Serial Monitor: baud (300–2 000 000), line ending, autoscroll, timestamps, send box, clear; shows garbage if baud mismatches (real behaviour). Opening the monitor **resets the board** (DTR auto-reset) exactly as the Arduino IDE does — with a tooltip explaining it.
- Serial Plotter: parses numeric CSV/space-separated lines; up to 8 series; pause/zoom.
- Multiple boards: each board has its own sketch and monitor tab.
- External editor: "Open in VS Code / external editor" opens the sketch folder; the game watches for file changes and offers/auto-runs Upload.
- Blocks (v1.x candidate): a Blockly-style editor that generates readable Arduino C++ (one-way), aimed at the youngest learners.

## 8. Test mode (simulation)

### 8.1 Controls
- **Play / Pause / Step** (step = one physics tick of 10 ms; **fine step** = 1 ms; instruction-level stepping arrives with the debugger in v1.x).
- **Time scale**: 0.1× … 4× (fast-forward is capped by available CPU; the HUD shows the achieved real-time factor).
- **Reset button** on the board model (clickable, like the real one). **Power switch** on battery holders. **Unplug USB** by dragging the cable.
- **Restart simulation**: returns everything to t = 0 deterministically (same inputs → same run).

### 8.2 Cameras
Orbit, top-down, follow-robot, chase, free-fly, and **sensor view** (see what the ultrasonic cone or line sensor "sees").

### 8.3 Overlays and telemetry
- On-board LEDs (ON, L, TX, RX) behave like the real board.
- Net voltage colouring, current-flow animation (moving dots, density ∝ current), component temperature glow, sensor visualisation (ultrasonic cone + hit point, line-sensor spots, IR beams, servo target angle).
- Telemetry panel: pick any pin/net/component quantity → live graph (voltage, current, motor RPM, battery state of charge, MCU load %). Export CSV.
- **Event log** (in Notebook and as toasts): resets (POR/external/brown-out/WDT), component damage, fuse trips, serial errors, warnings — each with timestamp and a cause explanation.

### 8.4 Remote inputs
Virtual phone for HC-05 Bluetooth (text/keys over serial), IR remote (NEC codes), keyboard → Serial, gamepad → virtual RC receiver (v1.x).

### 8.5 Arena editing
Place obstacles from a set (walls, boxes, cans, ramps), draw floor lines (width, colour, reflectance), set lighting level (affects LDR/IR sensors), floor material (friction), optional table edges. Save as a custom arena; share on Workshop.

## 9. No missions: a pure sandbox

Owner decision ([ADR-0008](adr/ADR-0008-pure-sandbox-full-release.md)): there are no missions. Players only create and test in the virtual world, and a robot just works or doesn't, the same as in real life. Players set their own goals or simply do whatever they want.

- Not in the game: missions, campaign, chapters, challenges, leaderboards, scores, stars, unlocks, ready-made example robots or templates.
- What helps players instead: the interactive tutorial, the Notebook (datasheet cards, error help, "why it broke" cards), the event log, the wiring check, measuring tools (multimeter, tape measure, stopwatch), and robots shared by other players on the Workshop.
- The Code Desk keeps the Arduino IDE's own code examples (Blink, Fade and library examples); these are code files, not example robots.

## 10. Economy and achievements

- Nothing is unlocked by playing: every free part and arena is available from the first minute.
- **Economy: free to play with paid packs** ([ADR-0007](adr/ADR-0007-monetization-free-to-play-dlc.md), [12 §1](12-business-steam-legal.md)). The whole base game is free. Paid packs add premium real boards (Mega 2560 first), advanced real parts, and customization: body finishes, decals, board and wire skins, workbench themes.
- Basic customization stays free: colour per part, the standard wire colours, the default workbench and mat. Some cosmetics can also be earned through achievements, so free players can personalise their robots too.
- Fair play: nothing paid gives an unfair advantage. Paid parts are real products with real specs, and cosmetics never change physics.
- No loot boxes, virtual currency, timers or boosters.
- Steam achievements (examples): *Hello, World* (first upload), *Magic Smoke* (destroy a part), *It Was Not a Bug* (experience a brown-out reset), *First Drive* (a robot drives 1 m under its own code), *Tinkerer* (use 10 different sensors), *Printed!* (export STL), *Show and Tell* (Workshop upload), *Datasheet Reader* (open 20 datasheets).

## 11. Onboarding — the tutorial

On first launch a 5–10 minute interactive tutorial teaches the controls: camera, placing an Uno and plugging in USB, placing a breadboard, LED and 220 Ω resistor, wiring D8 → resistor → LED → GND (D8 rather than D13, so the on-board L LED does not mask mistakes), uploading Blink, changing the delay, opening a datasheet card, and optionally seeing an LED burn without its resistor. It is skippable at every step and replayable from the main menu. Full script: [10 §3](10-content-arenas-tutorial-notebook.md). After it, the player is in the empty sandbox with their first project saved.

## 12. UI and controls

### 12.1 Layout
- Centre: 3D viewport. Top bar: mode tabs (**Build · Wire · Code · Test**), sim controls (Play/Pause/Step/Reset/time scale/real-time factor), Shop and Notebook buttons.
- Left panel: Parts Bin (Build/Wire) or Arduino code examples/Libraries (Code). Right panel: Inspector (selection properties, pin table, warnings). Bottom panel: Console (compiler output), Serial Monitor/Plotter, Telemetry, Event log — tabbed and collapsible.
- Panels are dockable; layouts saved per mode.

### 12.2 Controls (mouse + keyboard)
| Action | Default |
|---|---|
| Orbit / pan / zoom | RMB drag / MMB drag / wheel |
| Focus selection | F |
| Select / multi-select | LMB / Shift+LMB |
| Move / rotate / scale gizmos | W / E / R (Body Studio) |
| Rotate part 90° | R (Build) |
| Delete / duplicate | Del / Ctrl+D |
| Undo / redo | Ctrl+Z / Ctrl+Y |
| Upload / Verify | Ctrl+U / Ctrl+R |
| Play / Pause / Step | Space / Space / . |
| Mode tabs | 1–4 |

Gamepad support is limited to remote-controlling robots (v1.x).

### 12.3 Accessibility
- Colour-blind-safe wire palette option and always-available text labels on hover.
- UI scaling 100–200 %; font choice with Cyrillic and Latin-with-diacritics coverage (Uzbek Latin uses Oʻ, Gʻ).
- Captions for audio events ("buzzer 440 Hz", "motor stalled").
- Full keyboard navigation of panels; no time-limited interactions.

### 12.4 Localization
English, Uzbek (Latin script) and Russian at release, for everything the player reads: UI, tutorial, Notebook, error help, event log and the store page. Code, compiler output, pin names and part numbers stay untranslated. Details: [10 §5](10-content-arenas-tutorial-notebook.md).

## 13. Art direction

- Realistic proportions and PCB textures with **readable silkscreen** (pin labels are the UI). Slightly stylized lighting and materials to stay legible at desk scale.
- Scale cues everywhere: cutting-mat grid (1 cm), ruler, breadboard hole pitch (2.54 mm).
- Robots are 10–30 cm; arenas are table (1.2 × 0.8 m) or floor (3 × 3 m) sized.
- Damage states: burnt (dark, cracked), smoke VFX, heat shimmer on hot regulators.
- UI: clean, high-contrast, engineering-notebook feel; monospace font in code and telemetry.

## 14. Audio

- Motors: pitch and volume from RPM and load (gear whine for TT motors). Servos: short buzz when moving, hum when holding under load.
- **Passive buzzer/speaker: synthesized from the actual pin waveform** produced by the emulator (square wave at the real `tone()` frequency). Active buzzer: fixed tone when powered.
- Relay click, button click, wire plug, breadboard insert, screw, tape, "magic smoke" crackle.
- Ambient workshop room tone; light music optional.

## 15. Feedback and failure design (educational core)

| Event | What the player sees | Teaching hook |
|---|---|---|
| Compile error | Inline marker + plain-language card | The 30 most common beginner errors have curated explanations |
| Wiring hazard | Yellow triangle on the part and in Inspector | "Why?" opens the relevant Notebook card; Play still allowed |
| Component destroyed | Smoke, burnt texture, event log entry with the measured value vs datasheet limit | "Why it broke" card; "Replace part" (always free) |
| Brown-out reset | ON LED flickers, Serial restarts, event log: "VCC dipped to 3.9 V for 2 ms when motor started" | "Why it broke" card: decoupling, separate motor supply, battery internal resistance |
| Sensor misread | Sensor view shows the cone missing the object | HC-SR04 datasheet card: geometry and timing |
| Stalled motor | Motor stops, current rises, L298N heats up, event log | "Why it broke" card: stall current and heat |

## 16. Out of scope for the 1.0 release (recorded here so they are not forgotten)
Missions, challenges, leaderboards and example robots (owner decision, ADR-0008); player accounts (an optional email account may come after release, [13 D18](13-open-questions-and-risks.md)); multiplayer; VR; block coding (candidate after release); debugger with breakpoints (after release); schematic export (after release); non-AVR boards except as later paid packs; freeform sculpting; mobile ports.
