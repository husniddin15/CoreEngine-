# 07 — Physics, World and Sensors Specification

Status: DRAFT v0.1 (2026-09-22) · Pillar P3 · Decision: [ADR-0006](adr/ADR-0006-time-and-sync-model.md) · Facts: [research/R4](research/R4-electrical-physics-facts.md) · Related: [04 §4, §12](04-technical-design.md), [06](06-electrical-simulation-spec.md), [08](08-body-designer-spec.md), [09](09-components-catalog.md)

---

## 1. Goals and scope

The world layer makes the robot's motors, wheels, body and sensors behave like the real, table-sized things they are, so that code tuned in the game (timings, PID gains, thresholds) transfers to the real robot with at most small re-tuning.

| Fidelity target | Value |
|---|---|
| Drive-straight distance for a commanded time at a known wheel speed | ≤ 2 % error vs the analytic value `d = ω·r·t` (no slip, flat floor) |
| Turn-in-place angle from wheel geometry | ≤ 3 % error |
| HC-SR04 reported distance to a flat perpendicular target | ≤ 1 cm + 1 % of distance (module spec: 3 mm resolution) |
| Line-sensor analog values on the standard track | Same threshold (≈ 500 of 1023 on white/black tape) that public TCRT5000 tutorials use works unchanged |
| IMU at rest | Accel reads 1.00 g ± 0.02 g on the vertical axis; gyro ≤ 2 °/s bias |
| Real-time factor on min-spec | 1.0 with two robots and ≤ 200 rigid bodies |

Out of scope for v1: deformable objects, fluids, cloth, tracks/treads (v1.x), aerodynamics, thermal effects on mechanics.

## 2. Physics engine setup (Unity PhysX)

| Setting | Value | Reason |
|---|---|---|
| Units | metres, kilograms, seconds; robots 0.1–0.3 m, 50–1500 g | Keep PhysX in its comfort range; do not scale the world up |
| Fixed timestep | 0.01 s (100 Hz) | [ADR-0006](adr/ADR-0006-time-and-sync-model.md); 1 cm travel per step at 1 m/s |
| Solver iterations | 12 position / 4 velocity (project default 6/1 is too low for articulated small bodies) [VERIFY by test] | Stable wheel contacts and servo joints |
| Solver type | Temporal Gauss-Seidel (TGS) | Required for `ArticulationBody`; better joint drift behaviour |
| Contact offset / rest offset | 0.002 m / 0.0 m (default 0.01 m is 20 % of a 5 cm part) | Small objects otherwise float |
| Max angular velocity | 200 rad/s per wheel body (Unity default 7 rad/s) | TT motor no-load 250 rpm = 26 rad/s; N20 1000 rpm = 105 rad/s |
| Max depenetration velocity | 1 m/s | Avoid launches when parts overlap after edits |
| Gravity | 9.81 m/s² downward | — |
| Sleep threshold | 0.001 (lowered) | Light parts otherwise fall asleep while a motor is slowly starting |
| Enhanced determinism | On | Same-machine reproducibility for replays and regression tests (§7) |
| Layers | `Robot`, `Arena`, `Loose` (movable objects), `SensorOnly` (trigger volumes), `Wire` (no collision) | Wires never collide; sensor triggers do not push objects |

Physic materials (static/dynamic friction, bounciness) — values to be calibrated in Phase 1 against a real rover on a desk ([VERIFY] all):

| Pair | μ_static / μ_dynamic | Notes |
|---|---|---|
| Rubber tyre — laminate/wood/acrylic floor | 0.9 / 0.8 | TT-motor kit tyres are soft rubber |
| Rubber tyre — carpet | 1.0 / 0.9 | More rolling resistance (modelled as extra motor load, §4.1) |
| Rubber tyre — line tape | 0.8 / 0.7 | Slightly slicker |
| Plastic wheel (N20 kits) — floor | 0.6 / 0.5 | |
| Caster ball — floor | 0.05 / 0.05 | Free-rolling ball approximated by a slippery sphere (Unity's own differential-drive guide uses the same trick, [R4 §3](research/R4-electrical-physics-facts.md)) |
| Chassis/PLA — floor | 0.4 / 0.35 | When the robot drags its belly |
| Sumo ring paint | 0.7 / 0.6 | |

Friction combine mode: minimum for the caster pairs, average otherwise. Bounciness 0 everywhere except loose objects (0.1).

In the prototype (2026-09-25) the chassis and parts also combine by **minimum**: averaged with the floor's 0.9 / 0.8 they slid at 0.65 / 0.58. And PhysX's patch friction (Unity's default) applies a material's value at each of the two friction anchors of an edge or face in contact, about twice over, so the chassis material holds half the pair's value, 0.2 / 0.175. Measured with the owner's robot without a caster turning on the spot, its plate's edge on the floor: its resistance matched 0.33 times the edge's load; with both faults it could not turn at all. Unity's "improved patch friction" was tried and left off: it kept that resistance at about a third of the table's value. A single corner touching gets only the material's own value; a robot mostly drags an edge or a face.

## 3. Robot assembly → physics graph

The Body Studio assembly ([08 §2](08-body-designer-spec.md)) is converted at "Play" into one `ArticulationBody` tree per robot:

| Assembly element | Physics element |
|---|---|
| Chassis part (+ every rigidly mounted component and body part) | Root `ArticulationBody`; compound colliders from the parts' primitive colliders and components' collision boxes; mass = Σ masses; inertia from the compound |
| DC motor + wheel | Revolute `ArticulationBody` child (axis = motor shaft) with the wheel's collider (sphere collider of the tyre radius by default, cylinder/convex option for wide wheels); joint friction 0; drive as in §4.1 |
| Servo + horn/arm/gripper | Revolute child with a position drive (§4.2); limits ±90° (SG90 0–180° travel) |
| Caster | Sphere collider on the root with the caster material (no separate body) |
| Pan-tilt bracket | Two chained servo joints |
| Gripper fingers | Two revolute children driven from one servo (mirrored) or a linkage approximation (v1: direct mirrored drive) |
| Loose objects (cans, cubes) | `Rigidbody` with convex colliders |
| Second robot | Another articulation tree; no joints between robots |

Constraints ([R4 §3](research/R4-electrical-physics-facts.md), Unity manual): no kinematic loops, one root, tree depth ≤ 64 — all satisfied by wheeled robots with arms. Mass ratios (a 30 g wheel on a 900 g chassis) are handled well by the reduced-coordinate solver. Imported body meshes use convex decompositions (≤ 16 hulls) computed once at import ([08 §4](08-body-designer-spec.md)).

Centre of mass and total mass are shown in Body Studio; the run-time tree uses the same numbers. Components contribute their catalogue mass ([09](09-components-catalog.md)); wires are massless.

## 4. Actuators

### 4.1 DC gear motor
Model (per motor, integrated at 1 kHz in the core, [R4 §4](research/R4-electrical-physics-facts.md)):

```
electrical:  di/dt = (V_terminal − R·i − Ke·ω_m) / L          (ω_m = motor-shaft speed = N·ω_wheel)
torque:      T_m   = Kt·i − b·ω_m − T_coulomb·sign(ω_m)       (Kt = Ke in SI units)
gearbox:     T_out = η·N·T_m  (driving)    ; back-driving: T_out = (N/η)·T_m
wheel:       handled by PhysX: the joint receives T_out each 10 ms step; ω_wheel is read back
```

Parameter derivation from datasheet values (worked for the TT motor 1:48 at 6 V, Adafruit 3777 measurements in [R4 §2](research/R4-electrical-physics-facts.md)):

| Quantity | Derivation | TT 1:48 (6 V) | N20 100:1 (6 V, Pololu HP) |
|---|---|---|---|
| R | V / I_stall | 6 / 1.5 = **4.0 Ω** | 6 / 1.6 = 3.75 Ω |
| ω_nl (motor shaft) | rpm_nl · N · 2π/60 | 250 · 48 → 12 000 rpm = **1257 rad/s** | 310 · 100 → 31 000 rpm = 3246 rad/s |
| Ke = Kt | (V − I_nl·R) / ω_nl | (6 − 0.16·4)/1257 = **4.26 mV·s/rad** | (6 − 0.11·3.75)/3246 = 1.72 mV·s/rad |
| T_coulomb (motor side) | Kt · I_nl | 4.26e-3 · 0.16 = 0.68 mN·m | 0.19 mN·m |
| Ideal output stall torque | Kt · I_stall · N | 6.4 mN·m · 48 = 0.307 N·m = 3.1 kg·cm | 2.75 mN·m · 100 = 0.275 N·m = 2.8 kg·cm |
| Datasheet output stall torque | — | 0.8 kg·cm (Adafruit) | 2.4 kg·cm (Pololu) |
| Gearbox efficiency η (fitted) | datasheet / ideal | **≈ 0.26** [VERIFY on hardware — plastic gearboxes lose a lot; Adafruit's figure may be conservative] | ≈ 0.87 (metal gears) |
| L | not on datasheet | 1 mH [VERIFY] | 0.5 mH [VERIFY] |
| Rotor + gearbox inertia (referred to the wheel) | estimate | 2 g·cm² motor-side → ×N² ≈ 4.6 kg·cm² at the wheel [VERIFY] | — |

Since η for the TT motor is uncertain, the catalogue carries two parameter sets: "datasheet" (η = 0.26) and "measured" (to be filled from the fidelity log with a real motor, wheel and a spring scale). The game uses the datasheet set until measurements exist.

Voltage input: from the electrical layer ([06 §5.10–5.11](06-electrical-simulation-spec.md)) — the H-bridge output minus its drop, PWM-averaged with the slow-/fast-decay rule. Rolling resistance and carpet drag are added as an extra load torque `T_rr = C_rr · m·g·r` per wheel (C_rr 0.01 hard floor, 0.05 carpet [VERIFY]).

**Handing torque to PhysX.** Two options were considered ([R4 §3](research/R4-electrical-physics-facts.md), Unity differential-drive guide uses velocity drives):
- (a) Velocity drive with force limit: set `ArticulationDrive.targetVelocity = ω_target`, `forceLimit = T_out`. Robust but the drive's internal damping hides the motor dynamics (current, back-EMF) that the electrical layer needs.
- (b) **Direct joint torque** (`ArticulationBody.jointForce` on the wheel's revolute DOF) = T_out from the model each step, with ω_wheel read back from `jointVelocity`. The motor model is self-limiting (torque falls with speed through back-EMF), so this is stable at 100 Hz with 12 solver iterations.
Decision: **(b)**, with (a) available behind a flag as a stability fallback for v1.x if playtests show jitter. The 10 ms lag of ω_wheel into the electrical model is far below the mechanical time constant of the rover (≈ 100–300 ms), so it is invisible.

Stall: when the wheel is blocked, ω → 0, current → V/R (1.5 A at 6 V for the TT motor), torque saturates at the stall value; the electrical layer heats the winding and the driver (F14/F18 in doc 06).

### 4.2 Servo (SG90 / MG996R)
Signal decoding lives in the electrical layer ([06 §5.13](06-electrical-simulation-spec.md)); this model receives `target_deg ∈ [0,180]` (or "no signal"):

| Parameter | SG90 | MG996R | Source |
|---|---|---|---|
| Max speed | 0.1 s/60° → 600 °/s | 0.17 s/60° → 353 °/s (4.8 V) | [R4 §2](research/R4-electrical-physics-facts.md) |
| Stall torque | 1.8 kg·cm = 0.177 N·m | 9.4 kg·cm = 0.92 N·m (4.8 V), 11 (6 V) | same |
| Travel | 0–180° commanded (mechanical ≈ 0–150° on some SG90 [VERIFY]) | 0–180° | same |
| Deadband | 5 µs ≈ 0.5° [VERIFY] | 5 µs | — |
| Holding | Position hold with full torque while powered and signalled | same | — |

Implementation: the servo's internal controller is emulated as a rate-limited setpoint (`θ_cmd` moves toward `target` at max speed) feeding an `ArticulationDrive` in position mode: stiffness and damping tuned so the arm follows `θ_cmd` critically damped (stiffness ≈ 50 N·m/rad, damping ≈ 2 N·m·s/rad for a 9 g servo with a 10 g arm [VERIFY]), `forceLimit = T_stall`. If the load exceeds T_stall the joint stalls and the load torque is reported back so the electrical layer draws stall current. Lost signal (> 100 ms) → drive off (limp); out-of-range pulses ignored. Voltage dependence: max speed and torque scale linearly between 4.8 V and 6 V (datasheet endpoints); below 4.0 V the servo jitters (deterministic ±2° dither) and drops out below 3.5 V [VERIFY].

### 4.3 Stepper 28BYJ-48 + ULN2003
Half-step sequence decoded from IN1–IN4 (8 states); each valid state change advances the rotor by one half-step; 4076 half-steps per output revolution (63.68:1 gearbox × 64 half-steps, [R4 §2](research/R4-electrical-physics-facts.md)); a revolute drive in position mode tracks the accumulated step angle with a torque limit of 34 mN·m [VERIFY, ≈ 300 g·cm]; if the commanded step rate exceeds ≈ 1000 half-steps/s the rotor misses steps (position stops advancing — the real "stalls at high speed" behaviour) [VERIFY the rate]. Coil currents come from the electrical layer.

### 4.4 Buzzer and audio
Passive buzzer: the pin waveform (from the emulator's pin events) is converted to a square wave with the measured frequency and 50 % duty (or the actual duty), low-pass filtered, and sent to `IAudioSink`; amplitude from the drive voltage. Active buzzer: fixed 2.5 kHz tone while powered above 3 V. Motor audio: pitch = ω_m/(2π) mapped into a whine sample, volume from |T_m|; servo: buzz on movement, hum at stall.

### 4.5 LEDs, displays
Purely visual: the electrical layer provides currents/segments/pixels; the world renders them. No physics.

## 5. Sensors

Common rules: every sensor is a component behaviour ([04 §8](04-technical-design.md)) that (1) reads its supply validity from the electrical layer (unpowered → no output), (2) queries the world through `ISensorQuery` using the pose of its own sub-part at the start of the 10 ms physics step, (3) produces pin levels/voltages/bus registers with datasheet timing at cycle resolution, and (4) adds deterministic, seeded noise. Each sensor has a **sensor view** overlay for the Test mode camera.

### 5.1 HC-SR04 ultrasonic
Physical model ([R4 §2](research/R4-electrical-physics-facts.md), Elecfreaks datasheet): trigger pulse ≥ 10 µs high → after the falling edge the module emits an 8-cycle 40 kHz burst (200 µs) → Echo goes high ≈ 0.45 ms after the trigger (burst + internal delay [VERIFY]) → Echo stays high for the round-trip time `t = 2·d / v` → if no echo arrives, Echo falls after the **38 ms timeout** (≈ 6.5 m round trip). Speed of sound `v = 331 + 0.6·T_air` m/s (343 m/s at 20 °C → 58.3 µs/cm), with the arena temperature.

Geometry: 15° effective beam (±7.5°) approximated by **17 rays**: 1 centre, 8 at 3.5° and 8 at 7° off-axis (two rings), with Gaussian weights `w = exp(−(θ/4°)²)` (Webots' distance-sensor approach, [R4 §3](research/R4-electrical-physics-facts.md)). For each ray: `RaycastCone` returns hit distance, surface normal, material tags (roughness, absorption). Ray acceptance:
- Incidence angle (between ray and surface normal) ≤ θ_max(material): smooth/hard surfaces (acrylic, painted wall, metal) 22.5° [Webots rule]; rough/matte (cardboard, wood, fabric) 45°; soft absorbing (foam, cloth) return only within 15° and at half weight.
- Weighted "echo strength" S = Σ w_i over accepted rays / Σ w_i; the reported distance is the **nearest accepted hit** if S ≥ 0.12, else no echo (timeout). This reproduces the two classic failures: angled smooth walls give no echo or a far reading; thin objects (a 1 cm rod at 1 m) are missed.
- Range clamp 2–400 cm: closer than 2 cm → the module reports an unreliable small value (deterministic 0.5–2 cm reading, matching real behaviour) [VERIFY]; farther than 400 cm → timeout.
- Noise: σ = 3 mm + 0.5 % of distance (seeded), plus a 1-in-40 chance of a spurious "short" reading when S is between 0.12 and 0.25 (edge of the cone) [VERIFY as a design choice].
- Cross-talk: if another HC-SR04 in the same arena is mid-measurement and its cone overlaps, the later sensor may receive the earlier one's echo (deterministic rule: report the other sensor's distance when cones overlap by > 50 %). Off by default; a setting in the arena options turns it on.
- Re-trigger while busy (Echo high or waiting) is ignored; triggering again < 60 ms after a **timeout** measurement makes the next reading unreliable (the previous burst is still echoing): the module returns a random-looking short distance for that measurement. Teaches the ≥ 60 ms cycle rule.
- Timing precision: the Echo edges are scheduled at cycle resolution; `pulseIn` in user code measures them exactly as on hardware (including `pulseIn`'s own 1-cycle granularity). On a real Uno `pulseIn` reads about 0.6 % short because the Timer0 interrupt steals cycles from its counting loop; the emulator reproduces this (measured in the Phase 0 golden tests, 2026-09-23).
- Blind zone beside the beam: at 30 cm the 15° beam is only about 8 cm wide, narrower than most robots. A box beside the beam is invisible, so a robot can push against it with its wheel or corner while the sensor reads the wall behind it. The Phase 0 robot spike hit exactly this (2026-09-23). It is real HC-SR04 behaviour, so the simulation keeps it; the Notebook explains it and the common fixes (a second sensor, a bumper switch, or treating "distance not changing while driving forward" as stuck).

Supply: needs ≥ 4.5 V; between 4.0–4.5 V readings become erratic (+30 % noise); below 4.0 V no output. 5 V Echo level (3.3 V boards not in v1).

Sensor view: draw the cone, accepted rays green, rejected (incidence) rays orange, the reported hit as a sphere, and the echo timeline (trigger, burst, echo width).

### 5.2 IR obstacle module (LM393, 2–30 cm)
Emitter beam ≈ 35°, receiver looks along the same axis. Received intensity `I_r = ρ_IR · cos(α) / d²` summed over a 5-ray fan (0°, ±10°, ±17°) where ρ_IR is the target's IR reflectance (white paper 0.9, cardboard 0.6, black matte 0.08, mirror 1.0 within 5° else 0) and α the incidence angle; the module's potentiometer sets a threshold corresponding to a white target at 2–30 cm; output OUT is **active low** when `I_r > threshold` ([R4 §2](research/R4-electrical-physics-facts.md), SunFounder module). Ambient IR: the arena's "sunlight" setting adds a constant to `I_r` → false triggers near windows (real classroom problem). On-board LED mirrors OUT. Response time 2 ms. Sensor view: fan rays with intensity colour and the threshold indicator.

### 5.3 TCRT5000 reflective sensor (single and 5-channel bar)
Geometry: emitter/receiver pair looking down; optimum distance 2.5 mm, working 0.2–15 mm ([R4 §2](research/R4-electrical-physics-facts.md), Vishay datasheet). The behaviour samples the floor **reflectance map** at the sensor's ground projection over a 3 mm spot (bilinear, 5 samples). Collector current `I_c = I_c,max · ρ · h(z)` where `ρ` is the map value (white board 0.85, black tape 0.05, grey 0.4, aluminium tape 0.95), `h(z)` is the height response: 1.0 at 2.5 mm, 0.6 at 1 mm, 0.35 at 6 mm, 0.1 at 12 mm, 0 at 15 mm (piecewise-linear from the datasheet's relative-current curve [VERIFY exact shape]), `I_c,max` 2 mA at IF = 20 mA. The analog module output is `V_out = Vcc − I_c·R_L` with R_L = 10 kΩ (typical module) → white ≈ 0.4–1 V, black ≈ 4.5 V (or inverted on some modules — a `polarity` parameter). The digital variant adds an LM393 comparator with a pot threshold. Ambient light adds a leakage current (`+0.05·I_c,max` per 1000 lux of IR-rich light [VERIFY]); the "sunlight" setting can saturate the sensor (all white) — real failure. Emitter draws 20 mA per channel (5-bar = 100 mA from the 5 V pin — noted in the power budget).

5-channel bar geometry: sensors at −20, −10, 0, +10, +20 mm across the bar (typical 5-way TCRT5000 module [VERIFY]); height above floor comes from the mounting (Body Studio) — mounting it 8 mm up instead of 3 mm halves the signal, which the tutorial-style code then fails on. Sensor view: five spots coloured by the read value; the map is shown greyscale under the robot.

### 5.4 LDR (GL5528)
`IlluminanceAt(pos, normal)` → lux → resistance ([06 §5.3](06-electrical-simulation-spec.md)) with 45/55 ms rise/fall. Illuminance model: arena ambient (presets: dark 5 lux, evening 50, classroom 300, bright 1000) + Σ point lights (`E = I_cd / d²` with an occlusion raycast) + "sunlight" 5 000 lux directional with shadowing. Sensor view: lux value and a light-cone arrow to the dominant source.

### 5.5 MPU-6050 (GY-521 module)
Kinematics from `BodyKinematics(part)`: accelerometer output = specific force in the sensor frame `a_s = R^T·(a_world − g)` (at rest: +1 g on the up axis), gyro = angular velocity in the sensor frame. Ranges/sensitivity per FS setting: ±2 g → 16384 LSB/g, ±4 → 8192, ±8 → 4096, ±16 → 2048; ±250 °/s → 131 LSB/(°/s), ±500 → 65.5, ±1000 → 32.8, ±2000 → 16.4 ([R4 §2](research/R4-electrical-physics-facts.md)). Noise: accel σ 4 mg, gyro σ 0.05 °/s per sample at 1 kHz [VERIFY]; bias sampled once per run: accel ±20 mg, gyro ±1.5 °/s per axis [VERIFY]; temperature register: arena temperature + 3 °C self-heating, `T = raw/340 + 36.53`. Register subset (I2C via the electrical bus model): `WHO_AM_I 0x75 = 0x68`; `PWR_MGMT_1 0x6B` — the device **starts in sleep mode** (bit 6 set) and outputs zeros until cleared, exactly the real gotcha; `SMPLRT_DIV 0x19`, `CONFIG 0x1A` (DLPF: v1 implements only the sample-rate consequence, filtering ignored), `GYRO_CONFIG 0x1B`, `ACCEL_CONFIG 0x1C`, data registers `0x3B–0x48` (accel XYZ, temp, gyro XYZ, big-endian), `INT_ENABLE 0x38` / `INT_STATUS 0x3A` (data-ready only), `USER_CTRL 0x6A` (FIFO reset accepted, FIFO not implemented), address select via AD0 (0x68/0x69). **Not implemented**: DMP firmware (libraries using `MPU6050_6Axis_MotionApps20` will not get quaternions — documented; the raw-data path used by most tutorials and by `Adafruit_MPU6050`/`MPU6050_light` works). Sensor view: axes triad and live vectors.

### 5.6 Wheel encoders
Slotted disc (20 slots) on the TT motor's second shaft + LM393 optical sensor (or the kit's Hall/magnetic disc variant): pulses per wheel revolution = 20 (disc on the output shaft) → for a 65 mm wheel, 10.2 mm per pulse; the behaviour converts the joint angle into a square wave with 50 % duty at cycle resolution (edges placed by interpolating within the 10 ms physics step, so `attachInterrupt` counting and `micros()`-based speed estimation see smooth timing). Quadrature variant (KY-040-style or dual-sensor discs): two channels 90° apart. Noise: none; dust/misalignment failure not modelled. Sensor view: disc slots and the beam.

### 5.7 Rotary encoder KY-040
Detent-based: the knob is turned by the player in the Inspector or with the mouse wheel over the part; produces the standard 2-bit Gray sequence with 20 detents per turn and a 2 ms bounce burst on each transition (encoder bounce is a known pain point). Push switch to GND.

### 5.8 Bump / limit switches
A trigger collider on the lever: any contact with `Arena`/`Loose`/`Robot` layers closes the switch for the contact duration; 5 ms bounce burst; release when the contact ends. Force needed to press: 0.5 N [VERIFY] — light objects (paper) do not trigger it.

### 5.9 DHT11 / DHT22
Arena temperature and humidity (presets 20 °C/45 %; editable) with slow drift (±0.5 °C over minutes) and sensor lag (τ 10 s for DHT22, 20 s DHT11 [VERIFY]). Resolution/accuracy: DHT11 1 °C / ±2 °C, 1 % RH / ±5 %; DHT22 0.1 °C / ±0.5 °C, 0.1 % / ±2 % ([R4 §2](research/R4-electrical-physics-facts.md)). Protocol timing (18 ms host low, 80/80 µs response, 50 µs + 26–28/70 µs bits) is produced at cycle resolution on the single-wire net by the electrical/logic model; reading faster than 1 Hz (DHT11) / 0.5 Hz (DHT22) returns the checksum-failed/unchanged frame exactly like the real part. Motor heat near the sensor is not modelled.

### 5.10 IR receiver (VS1838B) + remote
The virtual remote (21-key NEC layout, codes 0xFFA25D … as in common kits [VERIFY the key map]) sends NEC frames: 9 ms leader, 4.5 ms space, 32 bits with 562 µs marks and 562/1687 µs spaces, repeat frames every 108 ms while a key is held; the receiver outputs the demodulated signal **active low** at cycle resolution. Line-of-sight check: a raycast from the remote's position (a UI-placed emitter in the arena, default at the camera) to the receiver; sunlight setting adds random 1-bit errors [VERIFY as design choice]. Works with `IRremote` decoding.

### 5.11 HC-05 Bluetooth
Virtual phone panel (text terminal + 8 configurable buttons + a joystick) ↔ serial bytes at the module's baud (9600 default; AT mode 38400 with EN/KEY high; PIN 1234). Pairing takes 2 s after power; STATE pin high when connected; module LED blink patterns (fast = unpaired, double-blink = connected). Latency 20 ms, no packet loss (v1). Range not modelled.

### 5.12 Sound sensor (KY-038 / KY-037)
Analog output = baseline + arena noise level; sources: a "clap" button in the UI (100 ms burst), motor noise proportional to Σ|T_m·ω_m| within 30 cm, buzzer output; digital output via the pot threshold. Sensor view: sound level bar.

### 5.13 Sensor summary
| Sensor | World query | Update rate | Output | Typical use |
|---|---|---|---|---|
| HC-SR04 | RaycastCone (17 rays) | per trigger | Echo pulse (cycle-timed) | Obstacle avoidance, mazes |
| IR obstacle | RaycastCone (5 rays) | 1 kHz | Digital active-low | Obstacle and edge detection |
| TCRT5000 / 5-bar | ReflectanceAt | 1 kHz | Analog voltage / digital | Line following, edge detection |
| LDR | IlluminanceAt | 1 kHz (τ 50 ms) | Resistance | Light seeking, night-lights |
| MPU-6050 | BodyKinematics | 1 kHz | I2C registers | Heading, tilt, balancing |
| Encoder | Joint angle | per edge | Pulses | Distance and speed control |
| Bump switch | ContactState | 100 Hz | Switch | Collision detection |
| DHT | Arena climate | per request | One-wire frame | Weather stations |
| IR receiver | Line of sight | per key | NEC frames | Remote control |
| HC-05 | UI | per byte | UART | Phone control |
| Sound | Noise events | 1 kHz | Analog/digital | Sandbox |

## 6. Arenas (physical representation)

Each arena is a prefab plus `arena.json` ([04 §7](04-technical-design.md)) with: static colliders and meshes, a **floor reflectance map** (RGBA texture: R = IR reflectance for TCRT/IR sensors, G = visible reflectance for the LDR/rendering, B = friction class index, A = unused; 2 mm/px), material tags per surface (roughness class for ultrasonic acceptance, IR reflectance for the IR module), lighting preset, climate preset, spawn pads, optional trigger lines for the stopwatch tool, and loose objects. Content specs are in [10 §2](10-content-arenas-tutorial-notebook.md); this table fixes the physical representation:

| Arena | Size | Floor | Features (physics/sensor relevant) |
|---|---|---|---|
| Workbench mat | 0.6 × 0.45 m cutting mat on a desk | Rubber mat, ρ_vis 0.5, grid lines | Bench tests; the robot can drive off the desk (falls 0.75 m, no damage in 1.0; the fall is logged) |
| Line Track A | 1.5 × 1.0 m board | White laminate ρ_IR 0.85, μ 0.8; 19 mm black tape ρ 0.05 (oval, radius 0.3 m) | Map 750 × 500 px; tape edges anti-aliased over 1 px |
| Line Track B | 2.0 × 1.5 m | Same; 90° corners, a 5 cm gap, a crossing, a 45° branch | — |
| Obstacle Field | 2.0 × 2.0 m, 10 cm walls | Laminate | Boxes 10–20 cm (rough), cans Ø 66 mm (smooth curved → echo only near-normal), foam block (absorbing), a thin rod Ø 10 mm (missed by ultrasonic) |
| Maze | 5 × 5 cells of 0.30 m, walls 0.10 m high, 1 cm foam board | Laminate | Wall material "rough" so the ultrasonic works up to 45° incidence; start and exit cells marked |
| Sumo Ring | Ø 0.77 m black ring, 2.5 cm white border, 2.5 cm high platform (mini-sumo class [VERIFY]) | Painted MDF μ 0.7; border ρ_IR 0.9, ring 0.05 | Players can place two of their own robots |
| Table Edge | 1.2 × 0.8 m table, 0.75 m high | Laminate | Edge detection with downward IR/TCRT sensors; a fall is logged in the event log |
| Ramp / Sandbox | 3 × 3 m floor | Selectable: laminate, carpet (μ 1.0, C_rr 0.05), tiles | Adjustable ramp 0–25°, movable boxes, a 2 cm step (climbability test) |
| Pick-and-place floor | 2 × 2 m | Laminate | 30 mm cubes (20 g), two marked squares, one wall; for grippers |

Lighting model for sensors: ambient lux + up to 4 point/spot lights (intensity in candela, occlusion raycasts) + optional directional "sunlight" (5 000 lux, IR-rich → affects IR/TCRT sensors). Climate: temperature (affects speed of sound and DHT), humidity. Presets per arena; editable in the arena editor.

Arena editor operations write into the same data: the line tool rasterises tape (width, colour → ρ) into the reflectance map; obstacle placement spawns prefabs with their material tags; trigger lines are boxes with ids used by the stopwatch tool. Saved arenas are Workshop-shareable ([04 §14](04-technical-design.md)).

## 7. Determinism, replays and the per-step handshake

```
FixedUpdate (t → t + 10 ms):
  1. bridge.CollectInputs():  for every sensor sub-part: pose (from the articulation tree), joint angles/velocities for wheels/servos, contact states; cache them
  2. core.Advance(10 ms):     10 × 1 ms electrical/behaviour ticks; sensors use the cached poses; motors/servos update their torque/targets
  3. bridge.ApplyOutputs():   wheel joint torques, servo drive targets/limits, stepper positions, audio samples
  4. Physics.Simulate(0.01)   PhysX step (auto-simulation off; stepped explicitly so batch mode and time scaling are exact)
  5. telemetry.Sample(); eventLog.Flush()
```
- Determinism on the same machine/build: PhysX "enhanced determinism" + fixed order of bodies (sorted by id) + explicit stepping gives bit-identical replays in practice [VERIFY in Phase 0]; cross-machine determinism is **not** promised (different CPUs/SIMD paths).
- Regression tests and replays therefore run in two modes: (a) the pure C# `simcli` for emulator and electrical behaviour; (b) the Unity player in `-batchmode -nographics` with the same physics build for anything that drives. Recorded replays store user inputs and the seed, not trajectories.
- Staleness: sensors see poses that are ≤ 10 ms old. At 1 m/s that is ≤ 1 cm of position error for a measurement whose own resolution is 3 mm and whose cycle is ≥ 60 ms (HC-SR04) — acceptable and documented ([ADR-0006](adr/ADR-0006-time-and-sync-model.md)). Line sensors sample the map with the cached pose plus linear extrapolation using the cached velocity for sub-step ticks (1 ms), which removes the 1 cm error for fast line followers.
- Time scaling multiplies the number of fixed steps per rendered frame; pausing stops stepping; nothing in the world layer reads wall time.

## 8. Interfaces (C#)

Defined in `CoreEngine.Sim.Physics` (contracts only) and implemented in `App.World`:

```csharp
public interface IPhysicsWorld {
    RobotHandle BuildRobot(Assembly assembly);                         // creates the articulation tree
    void Destroy(RobotHandle h);
    void Step(double dt);                                              // explicit PhysX step
    IReadOnlyList<ContactEvent> Contacts { get; }
}

public interface ISensorQuery {   // all calls use the pose cache of the current physics step
    ConeHit RaycastCone(Pose origin, float halfAngleDeg, int rayCount, float maxDist, LayerMask mask); // per-ray distance, normal, MaterialTag
    float   ReflectanceAt(Vector3 worldPos, ReflectanceChannel ch);   // bilinear sample of the floor map, 0..1
    float   IlluminanceAt(Vector3 worldPos, Vector3 normal);          // lux, with occlusion
    Climate ClimateAt(Vector3 worldPos);                              // temperature °C, humidity %
    Kinematics BodyKinematics(PartId id);                             // pose, linear/angular velocity, linear acceleration (world)
    bool    ContactState(PartId id);                                  // any contact on the part's trigger collider
    bool    LineOfSight(Vector3 from, Vector3 to);
    JointState Joint(JointId id);                                     // angle, angular velocity
}

public interface IActuatorSink {
    void SetJointTorque(JointId id, float torqueNm);                  // DC motors (direct torque)
    void SetVelocityDrive(JointId id, float targetRadPerS, float torqueLimit); // fallback mode
    void SetServoTarget(JointId id, float targetRad, float stiffness, float damping, float forceLimit, bool enabled);
    void SetStepperAngle(JointId id, float targetRad, float torqueLimit);
}

public interface IAudioSink { void PushSamples(SourceId id, ReadOnlySpan<float> samples, int sampleRate); void SetTone(SourceId id, float hz, float amplitude); }

public readonly struct MaterialTag { public readonly RoughnessClass Roughness; public readonly float IrReflectance; public readonly bool Absorbing; }
```
Threading: v1 calls everything on the main thread inside `FixedUpdate`; the interfaces are pure data in/out so a worker-thread pipeline can be added later ([04 §5](04-technical-design.md)).

## 9. Validation tests

| Test | Setup | Expected | Tolerance |
|---|---|---|---|
| Drive straight | 2WD rover, 65 mm wheels, both motors 6 V (no PWM), 2.0 s | d = ω_ss·r·t with ω_ss from the motor model (≈ 210 rpm loaded → 0.71 m/s) [VERIFY once η is measured] | 2 % |
| Turn in place | Opposite motors, track width 0.14 m, 1.0 s | θ = 2·v·t / track | 3 % |
| Stall | Wheel blocked | Motor current = 6/4 = 1.5 A; torque = stall spec | 5 % |
| Ramp | 15° ramp, 900 g rover | Climbs if 2·T_out/r > m·g·sin(15°) + rolling; else slides back | qualitative |
| Ultrasonic accuracy | Flat rough wall at 5 / 20 / 100 / 300 cm, normal incidence | Reported 5 / 20 / 100 / 300 cm | ≤ 1 cm + 1 % |
| Ultrasonic angle | Smooth wall at 50 cm rotated 0 / 15 / 25 / 45° | 50 cm / 50 cm / timeout / timeout | — |
| Ultrasonic small target | Ø 10 mm rod at 1 m | Timeout (missed) | — |
| Line sensor | TCRT5000 at 3 mm over white, black tape, grey | Analog ≈ 0.6 V / 4.5 V / 2.5 V (module with R_L 10 kΩ) | 15 % |
| Line sensor height | Same over white at 3 / 8 / 12 mm | Signal 100 % / 45 % / 12 % | 15 % |
| IMU static | Rover at rest, level | a_z = 16384 ± 330 LSB; gyro within ±1.5 °/s + noise | — |
| IMU rotation | Rover rotating at 90 °/s | gyro_z ≈ 90·131 LSB | 3 % |
| Encoder | Drive exactly 1.0 m | 1.0 / 0.204 × 20 ≈ 98 pulses | ±2 pulses |
| Bump | Drive into a wall at 0.3 m/s | Switch closes within 20 ms of contact; rover stops when code reacts | — |
| Determinism | Run the same obstacle-avoider test project twice with the same seed | Identical trajectories (bitwise pose log) | 0 |

## 10. Performance budget and open questions

Budget per 10 ms step on min-spec: PhysX ≤ 1.0 ms (2 robots, ≤ 200 bodies, ≤ 40 contacts), sensor queries ≤ 0.2 ms (≤ 4 ultrasonic × 17 rays + 10 line samples + 4 illuminance queries), behaviours ≤ 0.3 ms. Raycasts use Unity's batched `RaycastCommand` when more than 32 rays are needed per step.

Open questions:
1. Direct joint torque (chosen) vs velocity drive: confirm stability with 12 solver iterations on the light N20 chassis in Phase 0/1.
2. TT motor gearbox efficiency: the fitted 0.26 looks low; measure a real motor's stall torque with a spring scale and update the catalogue.
3. Ultrasonic ray count (17) and acceptance angles (22.5°/45°) need calibration against a real HC-SR04 on the fidelity bench (angled wall, cans, foam).
4. Should Line Track maps be vector shapes rasterised at load (resolution-independent, Workshop-friendly) instead of stored textures? Proposed: vector in `arena.json`, rasterised on load.
5. Sub-step pose extrapolation is used for line sensors; extend it to the ultrasonic hit distance (velocity-based) or leave at 10 ms staleness?
6. PhysX cross-machine determinism is not guaranteed: is Unity batch mode enough for regression tests and replays, or do we need a deterministic physics stub for the pure C# CLI (e.g., a simple 2D kinematic rover model for tests only)?
7. Tracks/treads and mecanum wheels: v1.x?
8. Falling off the table: no damage in v1 (parts are robust); add a "cracked part" chance in Sandbox realism mode?
