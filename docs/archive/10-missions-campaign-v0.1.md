# 10 — Content: Missions, Arenas and Curriculum

> **ARCHIVED 2026-09-23.** Superseded by the owner decision for a pure sandbox with no missions ([ADR-0008](../adr/ADR-0008-pure-sandbox-full-release.md)). Kept for reference only; links in this file are not maintained.

Status:Accepted DRAFT v0.1 (2026-09-22) · Pillar P5 · Related: [03 §9–11](03-game-design.md), [04 §6, §15](04-technical-design.md), [05 §8.1](05-arduino-emulation-spec.md) (allowed libraries), [06 §6](06-electrical-simulation-spec.md) (failure codes), [07 §6](07-physics-world-sensors-spec.md) (arena physics), [09](09-components-catalog.md) (parts)

---

## 1. Learning-objective map

The campaign follows the order in which real Arduino courses and robot kits introduce ideas: output → input → analog → power and motors → a robot that moves → a robot that senses → a robot that controls itself → open challenges. Each row shows where a concept is introduced (**I**) and where it is required again (r).

| Concept | C1 First Light | C2 Sense | C3 Move | C4 Robot | C5 Sound | C6 Line | C7 Grand |
|---|---|---|---|---|---|---|---|
| `pinMode`, `digitalWrite`, `delay` | **I** | r | r | r | r | r | r |
| `digitalRead`, pull-up/pull-down, `INPUT_PULLUP` | **I** | r | r | r | | | r |
| `Serial` print/read, baud | **I** | r | r | r | r | r | r |
| Functions, variables, `if`/loops | **I** | r | r | r | r | r | r |
| `analogRead`, ADC, voltage divider | | **I** | | | | r | r |
| `analogWrite`, PWM, `map()` | | **I** | r | r | | r | r |
| `millis()` non-blocking timing | | **I** | | r | r | r | r |
| `tone()` | | **I** | | | | | |
| H-bridge, motor direction/speed | | | **I** | r | r | r | r |
| Power supplies, internal resistance, sag, brown-out, decoupling | | | **I** | r | | | r |
| Libraries (`IRremote`, `Servo`, `NewPing`, `Wire`) | | | **I** | r | r | | r |
| Differential drive, dead reckoning | | | | **I** | r | r | r |
| Interrupts, encoders | | | | **I** | | | r |
| Arrays, state machines | | | | **I** | r | r | r |
| Ultrasonic sensing, `pulseIn`, timing rules | | | | | **I** | | r |
| Servo control | | | | | **I** | | r |
| Reflectance sensing, thresholds, calibration | | | | | | **I** | r |
| Feedback control: bang-bang → P → PID | | | | | | **I** | r |
| I2C devices (LCD/OLED), Bluetooth serial | | | | r | | | **I** |
| Power budgeting, efficiency, battery choice | | | r | | | | **I** |
| Body design: mounting, CoM, sensor height | | | | **I** | r | r | r |
| Electronics safety: current limits, polarity, shorts | **I** | r | r | r | r | r | r |

## 2. Mission catalogue

Format per mission: **Objectives** · **Given** (pre-placed) · **Allowed parts** · **Brief** · **Checks** (success-check DSL, §4) · **Stars** · **Hints** (3, progressive) · **Notebook** pages unlocked · **Learn by failing** (the intended mistake) · **Time** (median target) · **Reference solution**.

### Chapter 1 — First Light (Workbench mat)

**C1-M1 · Hello, Blink**
- Objectives: upload a sketch; `pinMode`/`digitalWrite`/`delay`; the on-board L LED.
- Given: Uno on the mat, USB connected. Allowed: none needed.
- Brief: "Every robot starts with a blink. Open Examples → 01.Basics → Blink and press Upload. Then make it blink five times per second."
- Checks: `sequence[ pin_toggle_period(D13, 1000 ms ±2 %, for 6 s), pin_toggle_period(D13, 200 ms ±5 %, for 4 s) ]`, `time_limit(20 min)`.
- Stars: 1 = both phases; 2 = ≤ 10 min; 3 = no hints.
- Hints: (1) "Examples are in the Code Desk menu." (2) "`delay(1000)` waits 1000 ms — the period is on + off." (3) "Change both delays to 100."
- Notebook: *What is a sketch* · *Uno pinout card* · *Upload and reset*.
- Learn by failing: opening the Serial Monitor resets the board (auto-reset explained in a toast).
- Time: 5 min. Reference: official Blink.

**C1-M2 · Your First LED**
- Objectives: breadboard strips; LED polarity; series resistor; Ohm's law at 20 mA.
- Given: Uno, half breadboard. Allowed: red LED, 220 Ω, 330 Ω, 1 kΩ, jumpers.
- Brief: "Move the blink off the board: LED and resistor on the breadboard, D8 to GND. Which leg is which?"
- Checks: `all[ pin_toggle_period(D8, 1000 ms ±5 %, 6 s), net_current_range(led-1, 8 mA, 20 mA), component_not_damaged(led-1) ]`.
- Stars: 1 = pass; 2 = current 12–16 mA (220 Ω); 3 = no hints.
- Hints: (1) X-ray shows which holes are connected. (2) The longer leg is the anode (+). (3) 5 V − 1.9 V over 220 Ω ≈ 14 mA.
- Notebook: *Breadboard anatomy* · *LED and resistor* · *Ohm's law*.
- Learn by failing: LED reversed → dark; LED without resistor → 84 mA → pin warning, LED burns in seconds (F1/F4); the event log shows the numbers.
- Time: 10 min. Reference: Blink with `int led = 8`.

**C1-M3 · Push to Start**
- Objectives: `digitalRead`; floating inputs; pull-down; `INPUT_PULLUP`.
- Given: C1-M2 circuit. Allowed: tactile button, 10 kΩ.
- Brief: "Light the LED only while the button is pressed. First try it without any resistor and watch the Serial Monitor…"
- Checks: `sequence[ serial_matches_regex(/[01]\n/, 20 lines), pin_state_for(D8, HIGH, while button pressed ≥ 1 s), pin_state_for(D8, LOW, while released ≥ 1 s) ]` with the button pressed by the mission script twice.
- Stars: 1 = pass; 2 = uses `INPUT_PULLUP` (check: `net_kind(D2, pullup_internal)`); 3 = no hints.
- Hints: (1) A pin connected to nothing reads noise. (2) 10 kΩ from the pin to GND holds it LOW. (3) `pinMode(2, INPUT_PULLUP)` and wire the button to GND.
- Notebook: *Floating inputs* · *Pull-up and pull-down* · *Button example*.
- Learn by failing: floating input prints random 0/1 (deterministic noise).
- Time: 12 min. Reference: official Button / DigitalInputPullup.

**C1-M4 · Talk to Me**
- Objectives: `Serial.begin`, `print`, baud rate; the Serial Monitor.
- Given: C1-M3. Allowed: —.
- Brief: "Print how many times the button was pressed. Try opening the monitor at the wrong baud and see what a real board does."
- Checks: `serial_contains("Pressed: 3", baud 9600)` after three scripted presses; `serial_garbage_observed(baud 115200)` (the player must switch the monitor to the wrong baud once — tracked as an event).
- Stars: 1 = pass; 2 = also prints with `millis()` timestamps; 3 = no hints.
- Hints: (1) Monitor and sketch must agree on baud. (2) Count edges, not levels (only count when the state changes). (3) Debounce with a 20 ms delay.
- Notebook: *Serial basics* · *Baud rate* · *Debouncing*.
- Learn by failing: baud mismatch garbage; bounce counts 3 presses as 7.
- Time: 10 min. Reference: StateChangeDetection.

**C1-M5 · Traffic Light**
- Objectives: multiple outputs; functions; timing sequences.
- Given: Uno, breadboard. Allowed: red/yellow/green LEDs, 3 × 220 Ω, jumpers, button.
- Brief: "Red 3 s, red+yellow 1 s, green 3 s, yellow 1 s, repeat. Bonus: a pedestrian button that requests green→red."
- Checks: `sequence_of_states([R],[R,Y],[G],[Y], durations 3/1/3/1 s ±5 %, 2 cycles)` on D8/D9/D10; `component_not_damaged(all)`.
- Stars: 1 = cycle correct; 2 = button request works within 5 s; 3 = ≤ 15 min.
- Hints: (1) Write a function `setLights(r, y, g)`. (2) Use an array of pins. (3) Poll the button inside a `millis()` loop, not during `delay`.
- Notebook: *Functions* · *Arrays* · *Chapter 1 recap*.
- Learn by failing: sharing one resistor for three LEDs (brightness changes; wiring check explains).
- Time: 20 min. Reference: common traffic-light tutorial.

### Chapter 2 — Sense the World (Workbench mat)

**C2-M1 · Dimmer**
- Objectives: `analogRead`, potentiometer as a divider, `analogWrite`, `map()`, Serial Plotter.
- Given: Uno, breadboard, LED + 220 Ω on D9. Allowed: 10 kΩ pot.
- Brief: "Turn the knob, dim the LED. Plot the raw value first."
- Checks: `all[ serial_plot_series(1, tracks pot 0–1023 ±3 %), pwm_duty_range(D9, follows pot ±5 %) ]` with a scripted knob sweep.
- Stars: 1 = pass; 2 = LED fully off at 0 and full at 1023; 3 = no hints.
- Hints: (1) Outer pot legs to 5 V and GND, wiper to A0. (2) `analogWrite` takes 0–255. (3) `map(v, 0, 1023, 0, 255)`.
- Notebook: *ADC and analogRead* · *PWM* · *map()* · *Serial Plotter*.
- Learn by failing: wiper wired to a digital pin → constant values; pot across 5 V/GND drawing 0.5 mA is fine (contrast with a 220 Ω "pot" burning).
- Time: 12 min. Reference: AnalogInOutSerial.

**C2-M2 · Night Light**
- Objectives: LDR + fixed resistor divider; thresholds; calibration.
- Given: Uno, breadboard, LED on D9. Allowed: GL5528, 10 kΩ, 1 kΩ, 100 kΩ.
- Brief: "When the room goes dark, the LED comes on. Use the room-light slider to test."
- Checks: `sequence[ arena_light(300 lux) → pin_state_for(D9, LOW, 2 s), arena_light(5 lux) → pin_state_for(D9, HIGH, 2 s) ]`.
- Stars: 1 = pass; 2 = threshold set from a measured calibration (Serial shows both readings); 3 = hysteresis (no flicker at 50 lux ramp).
- Hints: (1) LDR + 10 kΩ make a divider; A0 in the middle. (2) Print the value at bright and dark. (3) Two thresholds prevent flicker.
- Notebook: *Voltage dividers* · *Photoresistors* · *Hysteresis*.
- Learn by failing: LDR alone to A0 (no divider) reads nonsense; the wiring check explains source impedance.
- Time: 15 min. Reference: LDR tutorial pattern.

**C2-M3 · Make Some Noise**
- Objectives: `tone()`, frequency, active vs passive buzzer.
- Given: Uno. Allowed: passive buzzer, active buzzer, 100 Ω, button.
- Brief: "Play a scale, then a melody. Try both buzzers — only one of them can sing."
- Checks: `tone_sequence(D8, [262, 294, 330, 349, 392, 440, 494, 523] Hz ±1 %, ≥ 100 ms each)`; `component_not_damaged`.
- Stars: 1 = scale; 2 = melody from `pitches.h` ≥ 8 notes; 3 = button plays/stops.
- Hints: (1) `tone(pin, freq, duration)`. (2) The active buzzer ignores frequency. (3) Use an array of notes and durations.
- Notebook: *Buzzers* · *tone() and Timer2* (why `tone` conflicts with PWM on 3/11).
- Learn by failing: `tone()` on D11 while `analogWrite(3)` is used → PWM stops (Timer2 conflict) — real behaviour.
- Time: 12 min. Reference: toneMelody.

**C2-M4 · Night Watch**
- Objectives: `millis()` non-blocking timing; combining sensors and outputs.
- Given: C2-M2 circuit. Allowed: buzzer, second LED.
- Brief: "Fade an LED up and down continuously, and beep twice when it gets dark — without ever stopping the fade."
- Checks: `all[ pwm_duty_waveform(D9, triangle 0–100 %, period 2 s ±10 %), sequence[ arena_light(5 lux) → tone_count(D8, 2, within 1 s) ], no_delay_over(50 ms) ]` (the last check detects blocking delays by watching PWM stalls).
- Stars: 1 = pass; 2 = fade stays smooth during beeps; 3 = no hints.
- Hints: (1) BlinkWithoutDelay pattern. (2) Keep `previousMillis` per task. (3) `tone` with a duration is non-blocking.
- Notebook: *millis() and state* · *Chapter 2 recap*.
- Learn by failing: using `delay` stalls the fade — visible in the telemetry graph.
- Time: 20 min. Reference: BlinkWithoutDelay + Fade.

### Chapter 3 — Make it Move (Workbench mat; motor on a stand)

**C3-M1 · Motor 101**
- Objectives: why a GPIO cannot drive a motor; H-bridge basics; L298N wiring and jumpers.
- Given: Uno, TT motor on a stand, 4×AA pack. Allowed: L298N, jumpers, motor leads.
- Brief: "First, try the obvious thing: motor on D9 and GND. Then do it properly with the L298N."
- Checks: `sequence[ component_warning(uno, F4 pin overcurrent) OR skipped, motor_speed_range(m1, > 100 rpm, 2 s forward), motor_speed_range(m1, < −100 rpm, 2 s reverse) ]`.
- Stars: 1 = forward and reverse; 2 = no part damaged; 3 = explains (quiz card) why the drop is ≈ 2 V.
- Hints: (1) The pin can give 20 mA; the motor wants 150+ mA. (2) IN1/IN2 set direction; ENA jumper = full speed. (3) Battery to +12V/GND, Uno GND to the module GND — common ground!
- Notebook: *Why not a GPIO* · *H-bridges* · *L298N card* · *Common ground*.
- Learn by failing: motor on a GPIO → twitch + pin warning; forgetting common ground → nothing works (wiring check hint).
- Time: 20 min. Reference: L298N tutorial (LastMinuteEngineers pattern).

**C3-M2 · Speed Control**
- Objectives: PWM on ENA; measuring voltage with the multimeter; the L298N drop.
- Given: C3-M1 circuit. Allowed: multimeter.
- Brief: "Ramp the motor from stop to full speed over 5 s. Measure the motor voltage at 100 % — where did 2 volts go?"
- Checks: `motor_speed_profile(m1, ramp 0→max over 5 s ±15 %)`; `multimeter_reading_logged(motor terminals, 3.5–4.5 V at duty 100 %)`.
- Stars: 1 = ramp; 2 = measurement logged; 3 = ramp both directions.
- Hints: (1) Remove the ENA jumper and connect ENA to D9. (2) `analogWrite(9, speed)`. (3) Probe OUT1 and OUT2 while running.
- Notebook: *Saturation voltage* · *Multimeter*.
- Learn by failing: PWM with the ENA jumper still fitted (nothing changes).
- Time: 15 min. Reference: same tutorial, PWM section.

**C3-M3 · The 9 V Trap**
- Objectives: internal resistance; why 9 V blocks fail motors; choosing a supply.
- Given: C3-M2 circuit with a 9 V block instead of the AA pack. Allowed: 4×AA, 2×18650, multimeter.
- Brief: "This robot kit came with a 9 V battery. Run the motor at full speed and watch the battery voltage. Then pick a better supply."
- Checks: `sequence[ telemetry_observed(bat.voltage < 7.0 V while motor stalled ≥ 1 s), swap_part(bat-9v → bat-4aa or bat-2x18650), motor_speed_range(m1, > 150 rpm) ]`.
- Stars: 1 = pass; 2 = 18650 chosen with a written reason (quiz); 3 = no hints.
- Hints: (1) Watch the battery graph in Telemetry. (2) Every battery has an internal resistance. (3) Motors need current, not volts.
- Notebook: *Batteries compared* · *Internal resistance*.
- Learn by failing: the 9 V block sags to 6 V and is empty in minutes at 4× speed.
- Time: 12 min. Reference: —.

**C3-M4 · Brown-out**
- Objectives: shared supply problems; brown-out reset; decoupling and separate supplies.
- Given: Uno powered from the half-used 4×AA pack via VIN, two motors on the L298N from the same pack, a sketch that starts both motors from stall every 3 s. Allowed: 470 µF capacitor, second battery pack, 2×18650, multimeter.
- Brief: "The board keeps restarting whenever the motors start. Find out why from the event log, then fix it in at least one way."
- Checks: `sequence[ event_observed(reset.brownout ≥ 2), no_event(reset.brownout, 30 s) AND motors_running ]`.
- Stars: 1 = fixed; 2 = fixed two different ways; 3 = explains the 2.7 V threshold (quiz).
- Hints: (1) Read the event log: what was VCC when it reset? (2) Motors pull the pack below what the regulator needs. (3) Separate supply for the Uno, fresh cells, or a low-resistance pack.
- Notebook: *Brown-out detection* · *Decoupling* · *Regulator dropout*.
- Learn by failing: the reset loop itself; a 470 µF capacitor helps only a little (telemetry shows the dip depth).
- Time: 20 min. Reference: —.

**C3-M5 · Remote Motor**
- Objectives: using a library (`IRremote`); decoding remote codes; control logic.
- Given: C3-M2 circuit. Allowed: VS1838B receiver, IR remote.
- Brief: "Use the remote: ▲ forward, ▼ reverse, ◄► speed, OK stop."
- Checks: `sequence[ remote_key(UP) → motor_speed_range(m1, > 100 rpm), remote_key(DOWN) → motor_speed_range(m1, < −100 rpm), remote_key(OK) → motor_speed_range(m1, |v| < 5 rpm) ]`.
- Stars: 1 = pass; 2 = speed steps work; 3 = repeat-code handling (holding ◄ keeps changing speed).
- Hints: (1) Library Manager → IRremote → example `ReceiveDemo`. (2) Print `IrReceiver.decodedIRData.command`. (3) Handle `0` repeat frames.
- Notebook: *Libraries* · *IR remote protocol (NEC)*.
- Learn by failing: receiver pins reversed (no data), sunlight setting causes bit errors.
- Time: 20 min. Reference: IRremote ReceiveDemo.

Chapter 3 unlocks: chassis kits, wheels, casters, brackets, encoders, Body Studio presets.

### Chapter 4 — Robot Basics (Ramp/Sandbox floor and Table Edge)

**C4-M1 · Assemble the Rover**
- Objectives: mounting parts; centre of mass; wiring a complete rover; first drive.
- Given: empty workbench. Allowed: 2WD round chassis, 2 × TT motors, brackets, 65 mm wheels, caster, Uno, L298N, 4×AA (fresh), breadboard, jumpers, standoffs, tape.
- Brief: "Build the classic two-wheel rover. Check the centre-of-mass marker before you drive — then drive forward for two seconds."
- Checks: `all[ parts_used_subset(rover kit), com_inside_support_polygon(), distance_travelled_range(forward, 0.5–1.5 m in 2 s), robot_never_touches(walls) ]` in the Sandbox floor.
- Stars: 1 = drives; 2 = drives straight within 15 cm lateral drift; 3 = tidy wiring (≤ 12 wires, all reachable).
- Hints: (1) Motors face backwards on a 2WD kit; the caster goes at the front or back. (2) Battery over the drive wheels helps traction. (3) IN1–IN4 to D5–D8, ENA/ENB to D9/D10.
- Notebook: *Rover anatomy* · *Centre of mass* · *Motor mounting*.
- Learn by failing: wheels intersecting the chassis; both motors wired the same way → the rover spins (one motor is mirrored).
- Time: 25 min. Reference: —.

**C4-M2 · Drive Straight 1 m**
- Objectives: dead reckoning by time; motor mismatch; trimming.
- Given: C4-M1 rover (motors have hidden ±6 % differences, seeded). Allowed: as before.
- Brief: "Stop with your front edge on the 1 m line. Real motors are never identical — trim one side."
- Checks: `robot_reaches_zone_within(line-1m ±5 cm, 10 s)`, `robot_stays_in_lane(±10 cm)`; robustness: 3 seeds.
- Stars: 1 = ±5 cm; 2 = ±2 cm; 3 = ±1 cm in all 3 runs.
- Hints: (1) Measure how far it goes in 1 s first. (2) Speed ∝ voltage, roughly. (3) `analogWrite` the faster side a little less.
- Notebook: *Dead reckoning* · *Why motors differ*.
- Learn by failing: the rover curves; a fresh vs used battery changes the distance (the mission runs with two battery states in the robustness runs at 3 stars).
- Time: 20 min. Reference: —.

**C4-M3 · Turn Around**
- Objectives: turning kinematics; state machines; driving a square.
- Given: C4-M2 rover. Allowed: —.
- Brief: "Drive a 50 cm square and come back to where you started, facing the same way."
- Checks: `sequence[ heading_change_range(90° ±10°) ×4, robot_in_zone(start ±10 cm, heading ±15°) ]`.
- Stars: 1 = ends in the zone; 2 = ±5 cm; 3 = uses a function `turn(deg)` with a computed time (quiz + check `heading_change_range` accuracy ±5°).
- Hints: (1) Opposite motor directions spin in place. (2) Time for 90° ≈ π·track/(4·v). (3) Write `forward(cm)` and `turn(deg)`.
- Notebook: *Differential drive* · *State machines*.
- Learn by failing: turning on carpet takes longer (the 3-star run uses the carpet floor).
- Time: 20 min. Reference: —.

**C4-M4 · Count the Wheels**
- Objectives: encoders; `attachInterrupt`; `volatile`; closed-loop distance.
- Given: C4-M3 rover with TT encoder motors and LM393 sensors. Allowed: —.
- Brief: "Count slots instead of guessing time: stop after exactly 1 m, on any floor, with any battery."
- Checks: `robot_reaches_zone_within(line-1m ±2 cm, 10 s)` on laminate and carpet, with fresh and half-used packs (4 robustness runs).
- Stars: 1 = 3 of 4 runs; 2 = 4 of 4; 3 = also straight within ±1° using both encoders.
- Hints: (1) D2/D3 are the interrupt pins. (2) 20 slots per turn; wheel circumference = π × 65 mm. (3) Declare counters `volatile`.
- Notebook: *Interrupts* · *Encoders* · *Chapter 4 recap*.
- Learn by failing: counting in `loop()` misses pulses at speed; non-`volatile` counters never update.
- Time: 25 min. Reference: encoder tutorial pattern.

Chapter 4 unlocks: HC-SR04, servos, brackets, pan-tilt.

### Chapter 5 — See with Sound (Obstacle Field)

**C5-M1 · Echo**
- Objectives: HC-SR04 timing; `pulseIn`; unit conversion; the 60 ms rule.
- Given: rover with a mounted HC-SR04 in front of a wall on a rail (the mission script moves the wall 10→100 cm). Allowed: —.
- Brief: "Print the distance in centimetres ten times a second. Then read as fast as you can and watch what happens."
- Checks: `serial_numeric_tracks(wall distance ±2 cm, 10 samples/s, 5 s)`; `event_observed(hcsr04.retrigger_too_fast)` (the player must try the fast loop once).
- Stars: 1 = pass; 2 = uses `NewPing` or a timeout on `pulseIn`; 3 = median filter of 3.
- Hints: (1) 10 µs trigger pulse. (2) µs / 58 = cm. (3) Wait ≥ 60 ms between measurements.
- Notebook: *Ultrasonic ranging* · *pulseIn* · *Speed of sound*.
- Learn by failing: readings go crazy when triggered every 10 ms; Echo and Trig swapped → always 0.
- Time: 15 min. Reference: HC-SR04 tutorial pattern; NewPing example.

**C5-M2 · Wall Stop**
- Objectives: sensor-driven control; stopping distance.
- Given: C5-M1 rover. Allowed: —.
- Brief: "Drive toward the wall and stop 15 cm away. Every time, from any speed."
- Checks: `robot_stops_at_distance(wall, 15 cm ±3 cm)` from 3 start distances; `robot_never_touches(wall)`.
- Stars: 1 = 2 of 3; 2 = 3 of 3; 3 = slows down before stopping (speed profile check).
- Hints: (1) Read, compare, act — every loop. (2) The rover keeps rolling after you cut power. (3) Reduce speed below 40 cm.
- Notebook: *Closed loop basics* · *Braking vs coasting (L298N brake mode)*.
- Learn by failing: measuring only once at start; using `delay(500)` between readings.
- Time: 15 min. Reference: —.

**C5-M3 · Obstacle Avoider**
- Objectives: a full autonomous behaviour with a state machine; the obstacle field.
- Given: C5-M2 rover. Allowed: IR obstacle modules ×2 (optional).
- Brief: "Explore the field for 60 seconds without touching anything. Boxes, cans, foam — some are harder to see than others."
- Checks: `all[ time_limit(60 s), robot_never_touches(obstacles, walls), distance_travelled_range(≥ 4 m) ]`; robustness: 3 layouts.
- Stars: 1 = one layout; 2 = 3 layouts; 3 = ≥ 8 m travelled in each.
- Hints: (1) States: drive, back up, turn. (2) Cans at an angle return no echo — the sensor view shows why. (3) Side IR sensors catch what the cone misses.
- Notebook: *State machines 2* · *What ultrasound cannot see* · *IR obstacle sensors*.
- Learn by failing: hitting the thin rod and the angled acrylic panel.
- Time: 30 min. Reference: classic obstacle-avoider sketch.

**C5-M4 · Look Around**
- Objectives: Servo library; scanning; choosing the best direction; pan-tilt mounting.
- Given: C5-M3 rover + SG90 + pan bracket. Allowed: MG996R, second battery pack.
- Brief: "Mount the sensor on a servo. When blocked, scan left and right and turn toward the more open side."
- Checks: `all[ servo_sweep_observed(≥ 60°), time_limit(60 s), robot_never_touches(), distance_travelled_range(≥ 6 m) ]`; `component_not_damaged(uno)` — powering the servo from the Uno 5V is allowed but the USB fuse may trip in the "USB power" variant run.
- Stars: 1 = pass; 2 = no fuse/brown-out events; 3 = 3 layouts.
- Hints: (1) `Servo` uses Timer1 → D9/D10 PWM stop; move ENA/ENB to D5/D6. (2) `myservo.write(angle)` then wait for it to arrive. (3) Power the servo from the battery's 5 V (L298N +5V) and share GND.
- Notebook: *Servo library and Timer1* · *Servo power*.
- Learn by failing: the Timer1 conflict (motors stop responding to PWM on 9/10 after `Servo.attach`) — genuine and common.
- Time: 30 min. Reference: Servo Sweep + avoider.

Chapter 5 unlocks: TCRT5000, line bar, Line Tracks.

### Chapter 6 — Follow the Line (Line Track A/B)

**C6-M1 · Black or White**
- Objectives: reflectance sensing; sensor height; calibration and thresholds.
- Given: rover with one TCRT5000 module mounted 8 mm above the floor (deliberately too high). Allowed: standoffs, tape.
- Brief: "Plot the sensor over white board, black tape and the grey patch. If the difference is small, look at how high the sensor sits."
- Checks: `serial_plot_series(1)`, `sensor_reading_contrast(tcrt-1, white vs black ≥ 2.0 V)` after remounting; `mount_height(tcrt-1, 2–5 mm)`.
- Stars: 1 = pass; 2 = threshold computed from min/max in code; 3 = no hints.
- Hints: (1) Analog out to A0. (2) Move the sensor closer to the floor. (3) threshold = (min + max)/2.
- Notebook: *Reflectance sensors* · *Calibration*.
- Learn by failing: the 8 mm mounting halves the signal; sunlight mode saturates it.
- Time: 15 min. Reference: —.

**C6-M2 · Bang-Bang**
- Objectives: two-sensor line following; simple decisions.
- Given: C6-M1 rover with two sensors 25 mm apart. Allowed: —.
- Brief: "Complete one lap of Track A. Left sensor sees black → turn left; right sensor → turn right."
- Checks: `lap_completed(track-a, 120 s)`, `robot_stays_on_track(max deviation 5 cm)`.
- Stars: 1 = lap; 2 = ≤ 60 s; 3 = ≤ 45 s.
- Hints: (1) Slow down first. (2) Turn by stopping one motor. (3) Sensors straddle the tape.
- Notebook: *Bang-bang control*.
- Learn by failing: too fast → overshoots on the curves; sensors too far apart lose the line.
- Time: 20 min. Reference: two-sensor follower.

**C6-M3 · Proportional**
- Objectives: 5-sensor bar; weighted position; P control; Track B with corners and gaps.
- Given: C6-M2 rover with the 5-bar. Allowed: —.
- Brief: "Compute where the line is under the bar (−2 … +2) and steer proportionally. Track B has 90° corners and a gap — remember the last position."
- Checks: `lap_completed(track-b, 150 s)`, `robot_stays_on_track(6 cm)`, `line_gap_crossed()`.
- Stars: 1 = lap; 2 = ≤ 70 s; 3 = ≤ 55 s and no reversing.
- Hints: (1) position = Σ(w_i·s_i)/Σ s_i with weights −2..2. (2) left = base − Kp·pos; right = base + Kp·pos. (3) If all sensors see white, keep the last steering.
- Notebook: *Proportional control* · *Sensor fusion (weighted average)*.
- Learn by failing: Kp too high oscillates (the telemetry graph shows it).
- Time: 30 min. Reference: QTR-style weighted position.

**C6-M4 · PID Gold**
- Objectives: PID; tuning with telemetry; the lap-time leaderboard.
- Given: C6-M3 rover. Allowed: 2×18650 pack, TB6612FNG (faster response), N20 mini chassis.
- Brief: "Add D (and a little I). Tune with the position graph until the line stays centred. Then chase the clock."
- Checks: `lap_completed(track-b)`, `lap_time_under(45 s)` for 1 star; robustness: 3 start offsets.
- Stars: 1 = ≤ 45 s; 2 = ≤ 35 s; 3 = ≤ 28 s and position RMS ≤ 0.5.
- Hints: (1) D term = Kd·(pos − lastPos). (2) Start with Kp only, then add Kd until oscillation stops. (3) Batteries sag over a lap; a fresh pack is faster.
- Notebook: *PID explained* · *Tuning by graph* · *Chapter 6 recap*.
- Learn by failing: integral wind-up in a corner; loop rate too slow because of `Serial.print` every iteration.
- Time: 40 min. Reference: PID line-follower pattern (`PID_v1` optional).

Chapter 6 unlocks: LiPo, TB6612, gripper, HC-05, LCD/OLED, Challenges.

### Chapter 7 — Grand Challenges

**C7-M1 · Maze Runner** (Maze arena)
- Objectives: wall following; combining ultrasonic front + IR sides; robust turning.
- Given: rover from C5-M4. Allowed: 2 × IR obstacle modules, second HC-SR04, encoders.
- Brief: "Reach the exit of a 5×5 maze. Left-hand rule works — if your turns are accurate."
- Checks: `robot_reaches_zone_within(exit, 180 s)`, `robot_never_touches(walls) ≤ 3 touches`; robustness: 2 mazes.
- Stars: 1 = exit; 2 = ≤ 90 s; 3 = 0 touches, both mazes.
- Hints: (1) Keep a constant distance to the left wall with P control. (2) Use encoders to turn exactly 90°. (3) Detect openings by a jump in the side distance.
- Notebook: *Wall following* · *Maze algorithms*.
- Time: 40 min.

**C7-M2 · Sumo** (Sumo Ring)
- Objectives: edge detection; opponent detection; power and traction choices; TB6612/LiPo.
- Given: parts bin open. Allowed: any; robot mass ≤ 500 g and ≤ 10 × 10 cm footprint (mini-sumo rules) enforced by the check.
- Brief: "Push the training bot out of the ring in under a minute without leaving it yourself. Three opponents, increasing difficulty."
- Checks: `all[ robot_mass_under(500 g), robot_footprint_under(100 × 100 mm), sequence[ opponent_out(bot-easy), opponent_out(bot-medium) ], robot_stays_in_ring() ]`.
- Stars: 1 = easy; 2 = medium; 3 = hard opponent.
- Hints: (1) Two downward TCRT sensors at the front see the white border. (2) Low front wedge wins pushing. (3) 18650/LiPo + TB6612 for torque without sag.
- Notebook: *Traction and mass* · *Sumo rules* · *MOSFET drivers*.
- Time: 45 min.

**C7-M3 · Delivery** (Delivery Course)
- Objectives: gripper servo; sequencing; Bluetooth start command; LCD/OLED status.
- Given: parts bin open. Allowed: any.
- Brief: "Wait for the phone to send `go`, drive to the cube, grab it, deliver it to the drop zone, show `Delivered` on the display."
- Checks: `sequence[ bluetooth_send("go"), object_in_zone(cube, drop-zone, within 120 s), display_shows("Delivered") ]`, `object_not_dropped_outside()`.
- Stars: 1 = delivered; 2 = ≤ 60 s; 3 = also returns to start.
- Hints: (1) HC-05 TX/RX crossed; `Serial.read()` for the command (or SoftwareSerial to keep the monitor). (2) Close the gripper slowly; the servo stalls if you squeeze too hard. (3) LCD via I2C at 0x27.
- Notebook: *Bluetooth serial* · *I2C displays* · *Grippers*.
- Time: 50 min.

**C7-M4 · Endurance** (Line Track A, 30 laps)
- Objectives: power budgeting; efficiency; battery choice; measuring consumption.
- Given: C6-M4 rover with a 4×AA alkaline pack at 60 %. Allowed: any battery, any driver, LM2596.
- Brief: "Thirty laps on one charge. Watch the battery graph — where does the energy go?"
- Checks: `laps_completed(track-a, 30, before battery_empty)`, `no_event(reset.brownout)`.
- Stars: 1 = 30 laps; 2 = with the alkaline pack (efficiency required); 3 = ≤ 25 min total.
- Hints: (1) The L298N wastes ≈ 2 V × current — try the TB6612. (2) The Uno's regulator wastes (VIN − 5 V) × 50 mA; a buck converter helps. (3) Lower speed can mean more laps.
- Notebook: *Energy budget* · *Linear vs switching regulators* · *Chapter 7 recap*.
- Time: 40 min (at 4× speed the laps take ≈ 8 min real time).

## 3. Arenas (content specs)

Physical representation and sensor-relevant materials are in [07 §6](07-physics-world-sensors-spec.md); this section fixes the content.

| Arena | Dimensions | Materials | Lighting / climate presets | Start pads and zones | Used by |
|---|---|---|---|---|---|
| **Workbench mat** | Desk 1.2 × 0.6 m, cutting mat 0.6 × 0.45 m, 1 cm grid | Mat rubber (ρ_vis 0.5); desk laminate | Classroom 300 lux; desk lamp 1000 lux toggle; 20 °C / 45 % | Motor stand at the centre; "wall on a rail" prop for C5-M1 | C1–C3, C5-M1, C6-M1 |
| **Line Track A** | 1.5 × 1.0 m white board | Laminate ρ_IR 0.85; 19 mm black tape ρ 0.05, oval with 0.3 m radii; grey calibration patch 10 × 10 cm ρ 0.4 | Classroom; "sunlight" toggle for the calibration lesson | Start pad on the straight; lap line; zones: `lap-line` | C6-M2, C6-M4, C7-M4, Challenge 1 |
| **Line Track B** | 2.0 × 1.5 m | Same; path with two 90° corners, one 45° branch (dead end), a 5 cm gap, one crossing | Classroom | Start pad; `lap-line`; `gap` marker | C6-M3, C6-M4 |
| **Obstacle Field** | 2.0 × 2.0 m, walls 10 cm | Laminate; 3 layouts: boxes 10–20 cm (rough), 2 cans Ø 66 mm (smooth), foam block 20 cm (absorbing), a Ø 10 mm rod, one acrylic panel at 30° | Classroom | Start pad in a corner; zones: none | C5-M2 (wall rail variant), C5-M3, C5-M4 |
| **Maze** | 5 × 5 cells of 30 cm; walls 10 cm high, 1 cm foam board (rough) | Laminate | Classroom | `start` (cell 0,0), `exit` (cell 4,4); 2 layouts | C7-M1, Challenge 3 |
| **Sumo Ring** | Ø 77 cm ring, 2.5 cm white border, 2.5 cm high [VERIFY mini-sumo spec] | Painted MDF; border ρ_IR 0.9 | Gym 500 lux | Two start lines 10 cm from centre; `ring` zone; `out` zone | C7-M2, Challenge 2 |
| **Table Edge** | 1.2 × 0.8 m table, 0.75 m high | Laminate | Classroom | Start at the centre; `fell` trigger below the table | C4 optional, Challenge "edge dancer" |
| **Ramp / Sandbox** | 3 × 3 m floor | Laminate / carpet / tiles selectable; ramp 0–25°; 2 cm step; loose boxes | All presets | `line-1m` at 1 m from the start pad; free build | C4-M1..M3, C4-M4 (laminate + carpet), Sandbox |
| **Delivery Course** | 2 × 2 m | Laminate; one wall; 30 mm cube (20 g) at `pickup` | Classroom | `start`, `pickup`, `drop-zone` (30 × 30 cm) | C7-M3, Challenge 5 |

Sumo opponents (scripted, not emulated): **Easy** — drives forward, reverses at the border, turns 120°; **Medium** — same plus turns toward the player when its front IR sees it; **Hard** — edge-aware, seeks the player with a 30° scan, pushes at full speed with a 400 g body. Opponents obey the same physics (mass, friction) so a lighter player robot loses pushing contests — the intended lesson.

## 4. Success-check DSL

Missions declare checks in `mission.json`; the headless core evaluates them each physics step ([04 §15](04-technical-design.md)). Conditions are pure functions of the simulation state and the mission script (scripted events such as button presses, wall movement, light changes, remote keys).

```json
{
  "id": "C1-M2",
  "checks": {
    "all": [
      { "type": "pin_toggle_period", "board": "uno-1", "pin": "D8", "period_ms": 1000, "tolerance_pct": 5, "for_s": 6 },
      { "type": "net_current_range", "component": "led-1", "min_mA": 8, "max_mA": 20 },
      { "type": "component_not_damaged", "component": "*" }
    ]
  },
  "stars": [
    { "level": 1, "requires": "checks" },
    { "level": 2, "requires": { "type": "net_current_range", "component": "led-1", "min_mA": 12, "max_mA": 16 } },
    { "level": 3, "requires": { "type": "no_hints_used" } }
  ],
  "robustness": { "runs": 1 }
}
```

Condition types (v1):

| Type | Parameters | Notes |
|---|---|---|
| `pin_toggle_period` | board, pin, period_ms, tolerance_pct, for_s | Measures edges on the pin at cycle resolution |
| `pin_state_for` | board, pin, level, for_s, while (scripted condition) | |
| `pwm_duty_range` | pin, min, max or `follows` (another quantity ± tolerance) | Duty measured per PWM period |
| `pwm_duty_waveform` | pin, shape, period_s, tolerance_pct | Triangle/ramp detection |
| `tone_sequence` / `tone_count` | pin, frequencies_hz, tolerance_pct, min_ms | From pin edge timing |
| `serial_contains` / `serial_matches_regex` / `serial_numeric_tracks` / `serial_plot_series` | board, baud, pattern/quantity, tolerance | Monitor stream at the expected baud |
| `net_voltage_range` / `net_current_range` | net or component, min, max | From the electrical layer |
| `component_not_damaged` / `component_damaged` / `component_warning` | component (or `*`), failure code | F-codes from [06 §6](06-electrical-simulation-spec.md) |
| `event_observed` / `no_event` | event id (reset.brownout, fuse.trip, hcsr04.retrigger_too_fast…), min count, window | From the event log |
| `telemetry_observed` | quantity, comparison, for_s | e.g., `bat.voltage < 7.0` |
| `multimeter_reading_logged` | probe target, range | Player must use the tool |
| `swap_part` | from, to | Detects a replacement |
| `mount_height` / `com_inside_support_polygon` / `robot_mass_under` / `robot_footprint_under` / `parts_used_subset` / `budget_under` | geometry/BOM constraints | Evaluated at Play |
| `robot_in_zone` / `robot_reaches_zone_within` / `robot_stays_in_lane` / `robot_stays_in_ring` / `robot_stays_on_table` | zone id, tolerance, time | Robot reference point = chassis centre unless `front_edge` |
| `robot_never_touches` | layer or object ids, max_touches | From contact events |
| `robot_stops_at_distance` | object, distance, tolerance | Uses the true geometric distance, not the sensor |
| `distance_travelled_range` / `heading_change_range` / `motor_speed_range` / `motor_speed_profile` | quantity, min, max, window | From physics/motor models |
| `lap_completed` / `laps_completed` / `lap_time_under` / `line_gap_crossed` / `robot_stays_on_track` | track id, count, seconds, deviation | Track zones |
| `servo_sweep_observed` | servo, min_degrees | |
| `object_in_zone` / `object_not_dropped_outside` | object, zone, within_s | Loose objects |
| `opponent_out` | bot id | Sumo |
| `remote_key` / `bluetooth_send` / `arena_light` / `scripted_press` | scripted stimulus | Not a check; a script step inside `sequence` |
| `display_shows` | display id, text | Reads the display model's buffer |
| `time_limit` / `no_delay_over` / `no_hints_used` / `quiz_passed` | — | |

Composition: `all`, `any`, `sequence` (steps must be satisfied in order; scripted stimuli are steps), `not`. **Robustness runs**: `{"runs": N, "seeds": [...], "vary": ["start_offset", "battery_state", "floor", "layout"]}` — the mission passes only if the required fraction of runs passes; the headless runner executes them in parallel.

Three worked examples:
1. **Timing (C1-M1)**: `sequence[ pin_toggle_period(D13, 1000, 2 %, 6 s), pin_toggle_period(D13, 200, 5 %, 4 s) ]` — the second step starts counting when the first is satisfied and a new upload has happened.
2. **Failure lesson (C3-M4)**: `sequence[ event_observed(reset.brownout, ≥ 2), all[ no_event(reset.brownout, 30 s), motor_speed_range(m1, > 100 rpm, ≥ 20 s) ] ]` — the player must first witness the failure.
3. **Driving with robustness (C4-M4)**: `robot_reaches_zone_within(line-1m, 2 cm, 10 s)` with `robustness: {runs: 4, vary: [floor: [laminate, carpet], battery_state: [1.0, 0.5]]}` and stars by the number of passing runs.

## 5. Notebook content plan

### 5.1 Lessons (≈ 20)
1. **What is a sketch** — setup/loop, upload, what the compiler makes (a `.hex` the chip runs).
2. **Breadboard anatomy** — strips, rails, the split, the X-ray view.
3. **LED and resistor** — Vf, 20 mA, choosing 220/330 Ω; why no resistor burns things.
4. **Ohm's law and dividers** — V = I·R, dividers, pots, sensors as resistors.
5. **Floating inputs, pull-ups and pull-downs** — why the reading is random; `INPUT_PULLUP`.
6. **Serial** — baud, monitor, plotter, garbage when mismatched; auto-reset on open.
7. **Debouncing** — bounce, `millis()` debounce, edge detection.
8. **ADC and PWM** — 10-bit readings, references, `analogWrite` frequencies (490/980 Hz), which timers own which pins.
9. **millis() and non-blocking code** — state, multiple timers.
10. **Why a GPIO cannot drive a motor** — pin current limits, port groups, what happens.
11. **H-bridges** — L298N truth table, saturation drop, MOSFET bridges.
12. **Batteries** — chemistry table, internal resistance, sag, capacity vs current, why 9 V blocks fail.
13. **Brown-out, regulators and decoupling** — the Uno power path, dropout, BOD 2.7 V, capacitors, separate supplies.
14. **Common ground** — every circuit's number-one wiring bug.
15. **Differential drive and dead reckoning** — kinematics, wheel geometry, why motors differ.
16. **Interrupts and encoders** — `attachInterrupt`, `volatile`, counting pulses.
17. **Ultrasonic ranging** — trigger/echo, speed of sound, the cone, the 60 ms rule, what it cannot see.
18. **Servos** — pulses, Timer1 conflict, power.
19. **Reflectance sensors and calibration** — TCRT5000 height curve, thresholds, ambient light.
20. **Control: bang-bang, P, PID** — with the position graph; tuning recipe.
21. **I2C** — addresses, pull-ups, the scanner sketch, LCD/OLED.
22. **Power budgets and efficiency** — where the energy goes; linear vs switching regulators.
23. **From the game to the desk** — exporting the wiring table, BOM and STL; flashing the same sketch with the Arduino IDE.

### 5.2 Datasheet cards
One card per component ([09](09-components-catalog.md)) with: photo render, pinout diagram, absolute maximums, operating ranges, typical values used by the simulation (with the [VERIFY] flag hidden from players but visible to content reviewers), common mistakes, the link to the original datasheet. Card template fields: `name`, `pins`, `supply`, `limits`, `typical`, `mistakes`, `datasheet_url`, `used_in_missions`.

### 5.3 The 30 most common beginner errors (explanation cards)
Compile errors (GCC message → plain language): 1 `expected ';' before` · 2 `'x' was not declared in this scope` (typo/case/scope) · 3 `expected ')' before` / unbalanced braces · 4 `no matching function for call to` (wrong argument types) · 5 `'Serial' does not name a type`/`was not declared` (missing `Serial.begin` is runtime; this is usually a typo like `serial`) · 6 `stray '\302' in program` (pasted smart quotes) · 7 `a function-definition is not allowed here` (function inside `loop`) · 8 `redefinition of 'void setup()'` · 9 `#include expects "FILENAME"` / `No such file or directory` (library not installed) · 10 `invalid conversion from 'const char*' to 'int'` · 11 `expected primary-expression before ')'` · 12 `'else' without a previous 'if'` · 13 `assignment of read-only variable` (const) · 14 `too few arguments to function` · 15 `'class Servo' has no member named` · 16 `lvalue required as left operand` (`=` vs `==`) · 17 `Sketch too big` · 18 `Low memory available, stability problems may occur` · 19 `undefined reference to 'loop'` (missing `loop`) · 20 `variable or field declared void`.
Runtime/wiring symptoms: 21 nothing happens (no upload / wrong board / no power) · 22 Serial shows garbage (baud) · 23 LED dim (wrong resistor / shared resistor) · 24 button reads random (floating) · 25 motor twitches (GPIO drive / no common ground) · 26 board resets when motors start (brown-out) · 27 servo jitters (power) · 28 ultrasonic reads 0 or 3000 (wiring / too-fast trigger) · 29 I2C device not found (address, pull-ups, SDA/SCL swapped) · 30 PWM stopped working after adding `Servo`/`tone` (timer conflicts).

### 5.4 "Why it broke" cards
One per failure code F1–F28 in [06 §6](06-electrical-simulation-spec.md): what happened, the measured value vs the limit, how to fix it, how to prevent it, and the real-world consequence (cost of the part). Written in the same warm voice as the missions; no blame.

## 6. Challenges (unlocked after Chapter 3; Steam leaderboards)

| # | Challenge | Arena | Rules | Score | Leaderboard |
|---|---|---|---|---|---|
| 1 | **Line Time Trial** | Track B | Any parts; robot ≤ 1 kg; 3 laps; start on the pad; leaving the track > 10 cm = void | Best 3-lap time | Global + friends |
| 2 | **Sumo League** | Sumo Ring | Mini-sumo limits (≤ 500 g, ≤ 10 × 10 cm); best of 3 against Easy/Medium/Hard; then "ghost" matches against Workshop-shared robots (deterministic replays) | Wins, then average push-out time | Per opponent tier |
| 3 | **Maze Speed Run** | Maze (5 seeds) | Reach the exit; 3 touches allowed | Average time over 5 mazes | Global |
| 4 | **Battery Endurance** | Track A | One 4×AA alkaline pack at 100 %; laps until empty | Laps | Global |
| 5 | **Cheapest Robot That…** | Delivery Course | Deliver the cube; parts priced from the BOM price list | Lowest BOM cost that passes | Global |
| 6 | **Edge Dancer** | Table Edge | 60 s on the table at ≥ 0.3 m/s average, never falling | Distance | Global |

Leaderboard integrity: submissions include the project and seed; the top 100 are re-verified by the headless runner (Unity batch mode for driving challenges, [07 §7](07-physics-world-sensors-spec.md)) before display.

Fair play with paid packs ([ADR-0007](adr/ADR-0007-monetization-free-to-play-dlc.md)): every challenge has two leaderboards. **Standard** (the default view) accepts only free parts; **Open** accepts any part, including paid packs. Cosmetics are allowed in both, because they never change physics. Every challenge must be winnable with free parts.

## 7. Sandbox starters and templates

| Template | Contents | Purpose |
|---|---|---|
| Blank Uno + breadboard | Uno, half breadboard, USB | Electronics experiments |
| Blank Nano on breadboard | Nano inserted in a full breadboard | Compact experiments |
| 2WD rover (wired) | C4-M1 build with L298N, 4×AA, HC-SR04 bracket empty | Driving experiments |
| Line follower 5-bar | C6-M3 build with TB6612 and 2×18650 | Control tuning |
| Pan-tilt scanner | Uno + 2 × SG90 pan-tilt + HC-SR04 on a stand | Servo/sensor demos |
| OLED dashboard | Uno + SSD1306 + pot + button | Display coding |
| IR remote car | 2WD rover + VS1838B | Remote-control fun |
| Sumo bot base | N20 mini chassis, TB6612, LiPo, 2 × TCRT edge sensors | Challenge 2 start |
| Empty arena editor | Ramp/Sandbox with the editor open | Arena creation |

## 8. Onboarding flow — first 30 minutes (UI script)

1. **Title → New Player**: "Welcome to the workshop." The Workbench loads with an Uno on the mat, USB plugged in, Notebook open on the right showing C1-M1. Top bar highlights **Code** (pulse).
2. **Prompt** (top-left card): "Open the Code Desk (tab **3**) and load Examples → 01.Basics → Blink." Completion when the file opens.
3. **Prompt**: "Press **Upload** (Ctrl+U). Watch the console, then the board." Completion when the L LED toggles twice. Toast: "That is the real Blink program, compiled by the real Arduino toolchain, running on an emulated ATmega328P."
4. **Prompt**: "Change `delay(1000)` to `delay(100)` in both places and Upload again." → C1-M1 star screen (2 s), "Next mission" button.
5. **C1-M2 setup**: the Parts Bin opens (tab **1**) with only LED, resistors and jumpers unlocked. Prompt: "Drag the red LED into the breadboard. Long leg = anode." Placement ghost turns green on the breadboard. Then: "Drag a 220 Ω resistor so one leg shares a strip with the LED's anode (X-ray: **X**)."
6. **Wiring prompts**: "Click D8, then click the resistor's free leg." → wire appears. "Click the LED's short leg, then GND." Wiring check runs automatically: no warnings → "Upload with `int led = 8`" (the editor pre-selects the number). LED blinks → star.
7. **Deliberate failure**: prompt: "Experiment: remove the resistor and wire the LED straight to D8. Upload." The pin warning appears, then smoke after a few seconds; the event log opens: "84 mA through a 30 mA LED". Prompt: "Press **Replace part**, put the resistor back." (Achievement *Magic Smoke*.)
8. **C1-M3**: button placement and the floating-input demo with the Serial Monitor (prompts open the Monitor: "Notice the board reset when the monitor opened — that is what a real Uno does.").
9. **C1-M4** and **C1-M5** run with lighter prompts (only on inactivity > 60 s).
10. **Chapter 1 complete**: unlock animation for Chapter 2 parts; "You can also switch to Sandbox at any time from the top-right menu."

Skip: an "I know Arduino" option in step 1 jumps to C3-M1 with Chapters 1–2 marked complete (stars can still be earned later).

## 9. Localization notes
- English is the source language; string tables for Uzbek (Latin script) and Russian are prepared in v1.1 ([13 D8](13-open-questions-and-risks.md)). Mission briefs, hints, Notebook lessons and error cards are localized; datasheet cards stay English-first with localized field labels; code identifiers, pin names and part numbers are never translated.
- Layout: Russian strings run ≈ 15–25 % longer than English, Uzbek Latin ≈ 10 %; UI cards reserve 30 % slack; hint cards wrap.
- Fonts must cover Latin with diacritics (Oʻ, Gʻ, ʼ) and Cyrillic; avoid all-caps stylistic labels.
- Numbers/units: SI everywhere; decimal separator follows the locale in UI text but never in code samples.
- Voice: warm, second person, no blame; keep sentences short for machine-assisted translation.

## 10. Content production estimates

| Item | Design | Reference solution + wiring | Checks + robustness | Test/tune | Total |
|---|---|---|---|---|---|
| Workbench mission (C1–C3) | 2 h | 2 h | 2 h | 2 h | **8 h** |
| Driving mission (C4–C6) | 3 h | 4 h | 4 h | 5 h | **16 h** |
| Grand challenge (C7) | 4 h | 8 h | 6 h | 8 h | **26 h** |
| Arena | 4 h layout + 6 h art/collision + 2 h sensor tags | | | | **12 h** |
| Notebook lesson | 3 h writing + 1 h figures | | | | **4 h** |
| Datasheet card | 1.5 h | | | | **1.5 h** |
| Error card | 0.5 h | | | | **0.5 h** |
| Challenge | 4 h | | | | **4 h** |
| Template | 2 h | | | | **2 h** |

v1 totals: 14 workbench missions × 8 h = 112 h; 12 driving missions × 16 h = 192 h; 4 grand challenges × 26 h = 104 h; 9 arenas × 12 h = 108 h; 23 lessons × 4 h = 92 h; 55 datasheet cards × 1.5 h = 83 h; 30 error cards × 0.5 h = 15 h; 6 challenges × 4 h = 24 h; 9 templates × 2 h = 18 h → **≈ 750 h ≈ 19 weeks** for one content author (consistent with the Phase 2 allocation in [11 §5](11-roadmap.md) when combined with the tooling estimates).

## 11. Open questions
1. Should Chapter 1 allow skipping for experienced users without losing achievements? (Proposed: yes, stars can be earned later.)
2. Quiz cards for 3-star "explain why" items: multiple choice is easy to grade but weak pedagogically; free-text is not gradable offline. Proposed: 3-option multiple choice with a "learn more" link.
3. The motor-mismatch seed in C4-M2 (±6 %): fixed per player (repeatable) or per run? Proposed: per player profile, so the trim they find keeps working.
4. Sumo opponent behaviours are scripted, not emulated Arduinos; should the Hard opponent be an actual Workshop robot with real code? (v1.x.)
5. Grand challenge C7-M3 needs the display and Bluetooth models complete — if they slip, replace the display check with a Serial message.
6. The 30-error list is English-GCC based; localized explanations must map from the untranslated compiler text.
7. Do we need a "teacher mode" to hide hints and randomize seeds per student for graded homework? (v1.x candidate, see doc 13 D14.)
