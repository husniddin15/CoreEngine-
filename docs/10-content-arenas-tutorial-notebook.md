# 10 — Content: Arenas, Tutorial and Notebook

Status: DRAFT v0.2 (2026-09-23) · Decision: [ADR-0008](adr/ADR-0008-pure-sandbox-full-release.md) (pure sandbox, full release) · Replaces the mission-based v0.1, archived at [archive/10-missions-campaign-v0.1.md](archive/10-missions-campaign-v0.1.md) · Related: [03](03-game-design.md), [07 §6](07-physics-world-sensors-spec.md) (arena physics), [09](09-components-catalog.md) (parts), [06 §6](06-electrical-simulation-spec.md) (failure codes)

---

## 1. Content principles

- **Pure sandbox.** No missions, challenges, leaderboards, scores, stars, unlocks or ready-made example robots. Players set their own goals: "it just works or doesn't work, same as real life".
- **The world is the teacher.** Real behaviour, the event log ("what happened and why") and the datasheet cards explain every result. Help is always available but never forced.
- **Everything free is available from the first minute.** Paid packs are the only locked content ([12 §1](12-business-steam-legal.md)).
- **Content in this document:** arenas (places to test robots), a short interactive tutorial (controls only), the Notebook (reference), and localization into English, Uzbek and Russian.
- The Code Desk keeps the Arduino IDE's own code examples (01.Basics → Blink and so on, plus library examples), because they come with the Arduino core and libraries. They are code files, not example robots.

## 2. Arenas

Physical representation and sensor materials are specified in [07 §6](07-physics-world-sensors-spec.md). Arenas are environments only: they contain no goals or scoring. Players can edit any arena or build their own with the arena editor and share it on the Workshop.

| Arena | Dimensions | Materials | Lighting / climate presets | Spawn and measuring aids |
|---|---|---|---|---|
| **Workbench mat** | Desk 1.2 × 0.6 m, cutting mat 0.6 × 0.45 m with a 1 cm grid | Mat rubber (ρ_vis 0.5); desk laminate | Classroom 300 lux; desk lamp 1000 lux toggle; 20 °C / 45 % | Motor test stand; "wall on a rail" prop for ultrasonic tests |
| **Line Track A** | 1.5 × 1.0 m white board | Laminate ρ_IR 0.85; 19 mm black tape ρ 0.05, oval with 0.3 m radii; grey patch 10 × 10 cm ρ 0.4 | Classroom; "sunlight" toggle | Start pad on the straight |
| **Line Track B** | 2.0 × 1.5 m | Same; two 90° corners, one 45° dead-end branch, a 5 cm gap, one crossing | Classroom | Start pad |
| **Obstacle Field** | 2.0 × 2.0 m, walls 10 cm | Laminate; 3 layouts: boxes 10–20 cm (rough), 2 cans Ø 66 mm (smooth), foam block 20 cm (absorbing), a Ø 10 mm rod, one acrylic panel at 30° | Classroom | Start pad in a corner |
| **Maze** | 5 × 5 cells of 30 cm; walls 10 cm high, 1 cm foam board (rough) | Laminate | Classroom | Start cell and exit cell marked; 2 layouts |
| **Sumo Ring** | Ø 77 cm ring, 2.5 cm white border, 2.5 cm high [VERIFY mini-sumo spec] | Painted MDF; border ρ_IR 0.9 | Gym 500 lux | Two start lines; players can place two of their own robots |
| **Table Edge** | 1.2 × 0.8 m table, 0.75 m high | Laminate | Classroom | Start at the centre |
| **Ramp / Floor** | 3 × 3 m floor | Laminate, carpet or tiles; ramp 0–25°; 2 cm step; loose boxes | All presets | Floor distance markings every 10 cm |
| **Pick-and-place floor** | 2 × 2 m | Laminate; one wall; 30 mm cubes (20 g) | Classroom | Two marked squares (30 × 30 cm) |

Measuring tools that help players with their own goals, without scores: a tape-measure tool, floor distance markings, and a stopwatch the player starts and stops (optionally from a trigger line they place). Nothing is uploaded or ranked.

## 3. Interactive tutorial (5–10 minutes, skippable)

Teaches the controls only. It starts on first launch, can be skipped at any step, and can be replayed from the main menu. Each step is a prompt card at the top left; the step completes when the player does the action.

1. **Camera.** "Hold the right mouse button and move to look around. Scroll to zoom." Completes after orbit + zoom.
2. **Place a board.** The Parts Bin opens. "Drag the Uno onto the mat." Then: "Drag the USB cable's plug into the Uno's USB port."
3. **Breadboard and parts.** "Place a breadboard, then a red LED and a 220 Ω resistor." Prompt shows the X-ray toggle (**X**) so the player sees which holes are connected. Hint: "The longer LED leg is +."
4. **Wiring.** "Click pin 8 on the Uno, then click the resistor's free leg." Then: "Connect the LED's short leg to GND." The wiring check runs and shows a green tick.
5. **Code.** The Code Desk opens with Blink; the tutorial asks the player to change `LED_BUILTIN` to `8`. "Press Upload (Ctrl+U)." Completes when the LED blinks. Toast: "That is the real Blink program, compiled by the real Arduino toolchain, running on an emulated ATmega328P."
6. **Change and re-upload.** "Change both `delay(1000)` to `delay(200)` and upload again."
7. **Help.** "Right-click the LED and open its datasheet card." Shows the Notebook.
8. **Optional: see a failure.** "Want to see what happens without the resistor?" If yes: the LED burns after a few seconds, the event log explains the measured current against the 30 mA limit, and the player presses **Replace part**. Achievement *Magic Smoke*.
9. **Done.** "The workshop is yours. Everything you build here works like the real thing." The tutorial scene is kept as the player's first project.

Analytics (opt-in only, [04 §17](04-technical-design.md)): which step players quit at, to improve the tutorial.

## 4. Notebook

The in-game reference book, offline, in all three languages. No lessons (owner decision); it answers "what is this part" and "why did this happen".

### 4.1 Datasheet cards
One card per catalogue part (variants such as resistor values or LED colours share a card): about 60 cards at release, including the Mega 2560 Pack. Fields: photo-style render · pinout diagram · supply range · absolute maximums · typical values used by the simulation · a 2–3 sentence "how it works" · common mistakes · link to the original datasheet. Content reviewers also see the [VERIFY] flags from [09](09-components-catalog.md); players do not. Right-click on any part opens its card.

### 4.2 Error help (30 cards)
Each card maps an English GCC message or a runtime symptom to a plain-language explanation in the player's language, with the fix. Compile errors: `expected ';' before` · `'x' was not declared in this scope` · unbalanced braces · `no matching function for call to` · `'Serial' was not declared` (usually a typo such as `serial`) · `stray '\302' in program` (pasted smart quotes) · function defined inside `loop` · `redefinition of 'void setup()'` · missing library (`No such file or directory`) · `invalid conversion from 'const char*' to 'int'` · `expected primary-expression before ')'` · `'else' without a previous 'if'` · assignment of a read-only variable · `too few arguments to function` · `'class Servo' has no member named` · `lvalue required as left operand` (`=` vs `==`) · sketch too big · low memory warning · `undefined reference to 'loop'` · `variable or field declared void`. Runtime symptoms: nothing happens (no upload, wrong board, no power) · Serial shows garbage (baud) · LED dim · button reads random (floating input) · motor twitches (driven from a GPIO, or no common ground) · board resets when motors start (brown-out) · servo jitters (power) · ultrasonic reads 0 or 3000, or misses a box the robot is pushing against (wiring, triggering too fast, or the box is beside the narrow beam, [07 §5.1](07-physics-world-sensors-spec.md)) · I2C device not found (address, pull-ups, SDA/SCL swapped) · PWM stopped after adding `Servo` or `tone` (timer conflicts).

### 4.3 "Why it broke" cards
One per failure code F1–F28 in [06 §6](06-electrical-simulation-spec.md): what happened, the measured value against the limit, how to fix it, how to prevent it, and what it would cost on a real desk. Opened from the event log entry.

### 4.4 Event log and glossary
The event log (resets, damage, fuse trips, warnings) links each entry to its "why" card. The glossary uses [14](14-glossary.md) as its source.

## 5. Localization: English, Uzbek, Russian at release

- Everything the player reads is translated: UI, tutorial, Notebook cards, error help, event log texts, store page. English is the source language.
- Never translated: code, compiler output, pin names, part numbers, library names. Error cards match the English compiler text and show the explanation in the player's language.
- Uzbek uses the Latin script, including Oʻ, Gʻ and the ʼ sign; the UI font must cover these and Cyrillic.
- Fonts (Phase 0.6 result): Segoe UI for the interface and Consolas for code and telemetry, loaded from Windows at run time, so no font files are shipped. Both cover English, Uzbek Latin (U+02BB ʻ, U+02BC ʼ) and Russian completely. Every needed glyph is loaded into the font atlas at start-up (about 30–70 ms), because a glyph drawn for the first time mid-scroll causes a visible hitch. A bundled OFL font (for example Inter and Cascadia Mono) is only needed for a distinct visual style or for Proton/Steam Deck, where Windows fonts are missing.
- Layout: Russian runs about 15–25 % longer than English and Uzbek about 10 %, so UI text boxes keep 30 % spare room.
- Workflow: English source → AI-assisted draft → review by a native speaker (the owner) → in-game check of every screen. A fixed term list per language keeps technical words consistent (e.g., breadboard, jumper, pull-up); it is built during Phase 2.
- The language can be changed at any time in Settings; it follows the Steam language by default.

## 6. Production estimates (solo developer with AI assistance)

| Item | Count | Hours each | Total hours |
|---|---|---|---|
| Arenas (layout, art, collision, sensor tags) | 9 | 12 | 108 |
| Interactive tutorial | 1 | 24 | 24 |
| Datasheet cards | 60 | 1.5 | 90 |
| Error help cards | 30 | 0.5 | 15 |
| "Why it broke" cards | 28 | 0.5 | 14 |
| Uzbek + Russian translation (AI draft, owner review, in-game check) | all text | — | 60 |
| **Total** | | | **≈ 311 h ≈ 8 weeks** |

## 7. Open questions
1. Keep the Arduino IDE code examples in the Code Desk (proposed: yes, as in §1)?
2. Are the stopwatch and trigger-line tools welcome, or should the sandbox have no timing tools at all?
3. Should the Workshop browser highlight popular community robots on the main menu? This gives beginners examples made by players, not by us.
