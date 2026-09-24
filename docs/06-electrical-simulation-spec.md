# 06 — Electrical Simulation Specification

Status: DRAFT v0.1 (2026-09-22) · Pillar P2 · Decision: [ADR-0004](adr/ADR-0004-electrical-simulation.md) · Facts: [research/R4](research/R4-electrical-physics-facts.md) · Related: [05 §4, §6](05-arduino-emulation-spec.md), [07](07-physics-world-sensors-spec.md), [09](09-components-catalog.md)

---

## 1. Goals and scope

The electrical layer answers one question every millisecond: **what is the voltage on every net and the current through every component**, given the MCU pin states, the battery state and the mechanical state of motors. From those numbers the game derives everything the player sees: LED brightness, motor torque, sensor supply validity, regulator temperature, brown-out resets and "magic smoke".

| Modelled | Not modelled (documented deviation) |
|---|---|
| DC operating point of every analog island at 1 kHz | Transients faster than 1 ms (ringing, inductive kick-back spikes), except where a behavioural model adds them explicitly (H-bridge decay, motor L/R) |
| Event-driven digital nets at cycle resolution (62.5 ns) | AC analysis, RF, EMI, crosstalk |
| Behavioural models for motors, drivers, servos, batteries, regulators, fuses, sensors, displays | Transistor-level SPICE models; the 2N2222 is a switch model, not Ebers–Moll |
| Failure and damage from over-current, over-voltage, reverse polarity, thermal stress | Aging, ESD, mechanical damage |
| Temperature of power parts (regulator, L298N, motors, servos) with first-order thermal models | Room-temperature effects on LEDs/resistors |

Fidelity targets: node voltages within 5 % of a SPICE reference for the validation circuits in §9; LED currents within 10 %; brown-out and fuse events reproduce the real Uno-plus-L298N behaviour described in tutorials and forums; every warning card the game shows corresponds to a datasheet limit that a real part violates in the same situation.

Principle: **if it works in the game, it works on the desk** — and the reverse: if a real circuit fails (9 V into 5 V, LED without resistor for minutes, TT motors on a 9 V block), the game shows the same failure with the measured number and the datasheet limit.

## 2. Netlist model

### 2.1 Pins and roles
Every component definition lists pins with a role (doc 04 §8). Roles decide default electrical behaviour and the wiring-check rules:

| Role | Meaning | Default electrical element |
|---|---|---|
| `power_in` | Supply input of a module (VCC, +5V, Vs) | Current sink defined by the model (quiescent + active) |
| `power_out` | Regulated output (Uno 5V/3V3 pins, L298N +5V) | Voltage source with series resistance and current limit |
| `ground` | GND | Reference node (all grounds of a board are one net) |
| `digital_in` / `digital_out` / `digital_io` | Logic pins | Input threshold / push-pull driver / MCU pin driver |
| `open_drain` | I2C SDA/SCL, some sensor outputs | Pull-low switch, needs pull-up |
| `analog_in` | ADC inputs | High-impedance sampler |
| `analog_out` | Sensor analog outputs (LDR divider, TCRT5000) | Voltage source with output resistance from the model |
| `motor` | Motor terminals, H-bridge outputs | Two-terminal element defined by the model |
| `passive` | Resistor, LED, capacitor, diode legs | Two-terminal stamp |
| `signal` | Servo PWM input, WS2812 DIN | Digital input with a decoder |
| `mechanical` | No electrical function (mount tabs) | None |

### 2.2 Nets
A **net** is the set of pins connected by wires, breadboard strips and internal board copper. Nets carry a kind that the solver uses to choose the evaluation method:

| Kind | Evaluated by | Examples |
|---|---|---|
| `digital` | Driver-strength resolution on events (§3.1) | D13 → resistor → LED anode (the LED side becomes analog; see mixed) |
| `analog` | MNA at 1 kHz (§3.2) | Voltage divider, pot wiper, LED cathode node, motor terminals |
| `power` / `ground` | MNA with the power model (§4) | 5V rail, VIN, battery terminals |
| `bus` | Digital with open-drain rules and a pull-up check | SDA/SCL, one-wire (DHT) |
| `mixed` | Digital events trigger an immediate MNA re-solve of the island | A GPIO driving a resistor into an LED: the pin is a digital driver stamped as a source in the analog island |

A net whose only members are inputs (no driver, no pull) is **floating**; the MCU reads deterministic noise on it ([05 §4](05-arduino-emulation-spec.md)) and the wiring check flags it.

### 2.3 Breadboards
- **Half-size (400 tie-points)**: 30 columns × 2 halves of 5-hole terminal strips (a–e, f–j) = 60 strips of 5, plus 2 × 2 power rails of 25 holes (some models split rails at the middle — the definition JSON carries a `railSplit` flag; default: not split for half-size).
- **Full-size (830 tie-points)**: 63 columns × 2 halves, rails of 50 holes; many boards **split the rails in the middle** (two rails of 25 each side) — the classic beginner trap; default `railSplit: true` with the visible marking gap so the X-ray view shows it.
- Each strip is a net member; inserting a component leg into a hole joins the pin to that strip's net. The centre channel separates a–e from f–j (DIP parts straddle it).
- Contact resistance is ignored (0 Ω); an optional "worn breadboard" mode adds 0.05–0.2 Ω per contact for advanced players (after release).

### 2.4 Wires and connectors
| Item | Model |
|---|---|
| Dupont jumpers, solid-core wire | 0 Ω; optional 0.05 Ω per 10 cm in "realistic wire" mode (26 AWG ≈ 0.13 Ω/m [VERIFY]) |
| Screw terminals | 0 Ω; a wire end can only be in one terminal |
| Battery snap / holder leads | 0 Ω; polarity is a property of the wire end (red/black) so reversed insertion is possible |
| USB cable | Creates the `USB_5V` and `USB_GND` nets between the virtual PC port and the board; the PC port is a 5.0 V source with 0.3 Ω series resistance and a 0.5 A polyfuse on the board side |
| Motor leads | 0 Ω; TT motors with pre-soldered leads by default |

### 2.5 Board-internal nets (Uno R3)
The Uno definition contains its own internal netlist so that real quirks emerge instead of being scripted:

| Board net | Contents |
|---|---|
| `5V` | NCP1117 output, USB source switch (FDN340P), ATmega328P VCC/AVCC (via the 5V rail and the ferrite/AVCC filter as 0 Ω), 16U2 VCC, ICSP, the 5V header pin, 47 µF + 100 nF lumped decoupling |
| `3V3` | LP2985 output (150 mA device, 50 mA board rating), header pin |
| `VIN` | Barrel jack through the reverse-protection diode (Schottky-like drop 0.5 V [VERIFY]), VIN header pin, NCP1117 input |
| `GND` | All ground pins |
| `D0 (RX)` / `D1 (TX)` | ATmega328P PD0/PD1, header pins, and **1 kΩ series resistors** to the 16U2 bridge (so a device on the header wins against the bridge and the Serial Monitor shows garbage — real behaviour) |
| `D13` | PB5, header pin, and the L LED through an LMV358 buffer (no load on D13 on R3 boards) |
| `RESET` | 10 kΩ pull-up to 5V, reset button, 100 nF from the 16U2 DTR for auto-reset |
| `AREF` | ADC reference pin with 100 nF |
| `IOREF` | Tied to 5V |

The Nano and Mega carry equivalent internal netlists (Nano: no source switch — USB and VIN diode-OR'd [VERIFY]; Mega: same power path as the Uno).

## 3. Solver design

### 3.1 Digital net resolution (event-driven)
Each driver on a digital net contributes a **strength**: `Strong` (push-pull output, MCU pin, logic output), `Weak` (pull-up/pull-down resistor 1–100 kΩ, MCU internal pull-up), `OpenDrainLow` (I2C pull-down, DHT), `HiZ` (input). Resolution per event:

1. If any `Strong` driver exists: level = its level. If two `Strong` drivers disagree → **contention**: the net takes the level of the lower-impedance driver (roughly the midpoint, computed by MNA on that island), and the failure model accumulates the contention current ((5 V − 0 V)/(R_oh + R_ol) ≈ 90 mA per pair — far above 40 mA) — a hard-failure path.
2. Else if any `OpenDrainLow` is active: level = low.
3. Else if a `Weak` driver exists: level = its level (with the pull-up's current limited by its resistance when something else loads the net).
4. Else: floating → noise generator.

Events are cycle-stamped (from the MCU or from device timers) and processed in order; the net's new level is delivered to all inputs on the net at the same cycle (propagation delay 0; device-specific delays are inside the devices). Threshold conversion for inputs uses the device's VIH/VIL (MCU: 0.6·VCC / 0.3·VCC with hysteresis; TTL devices: 2.0/0.8 V; CMOS 5 V: 3.5/1.5 V).

Digital nets that also touch analog stamps (a GPIO driving an LED through a resistor) are `mixed`: the pin event marks the analog island dirty and it is re-solved immediately, not at the next 1 ms tick, so LED/motor-enable changes are cycle-accurate too.

### 3.2 Modified nodal analysis (1 kHz)
Analog islands are solved by MNA: unknowns are node voltages plus currents through voltage sources; equation `G·x = b`. Stamps:

| Element | Stamp |
|---|---|
| Resistor R between a, b | G[a,a]+=1/R, G[b,b]+=1/R, G[a,b]−=1/R, G[b,a]−=1/R |
| Voltage source V with series Rs (batteries, regulators, MCU pin drivers, logic outputs) | Thevenin: resistor Rs plus an extra current unknown, or Norton (V/Rs current source with conductance 1/Rs) — Norton is used everywhere Rs > 0, which avoids extra unknowns |
| Current source I into node a | b[a] += I |
| Ideal switch / button | Closed: 0.01 Ω resistor (50 mΩ contact [VERIFY]); open: 100 MΩ |
| Potentiometer | Two resistors (k·R, (1−k)·R) with the wiper node; k from the knob angle |
| LDR | Resistor whose value comes from the illuminance query (§5.3) |
| Diode / LED | Piecewise-linear companion: below Vf: conductance 1/1 MΩ; above: Norton equivalent of the exponential model linearised at the previous solution (Shockley with n·Vt = 0.05 V for LEDs [VERIFY]); at most 5 Newton iterations per solve, then accept (bounded for determinism and speed) |
| Capacitor C | Backward Euler: conductance C/Δt in parallel with current source (C/Δt)·V_prev; Δt = 1 ms |
| Motor (electrical side) | Norton source from the behavioural model: back-EMF Ke·ω as a voltage source with the winding resistance R (inductance handled inside the model at 1 kHz; see §5.10) |
| H-bridge output | Depends on state: source/sink through VCEsat model (§5.11) or high-Z |
| Regulator output | Voltage source 5.0 V with Rs = 0.05 Ω while in regulation, else Vin − Vdropout (§4.2) with current limit clamp |
| Fuse (polyfuse) | 0.1 Ω when cold; trips → 10 kΩ ("high resistance state"); resets after the condition clears (§4.2) |
| Logic device supply | Current sink I_quiescent + I_active (stamped as a current source to ground) |

Solution method: islands are typically 3–30 nodes, worst case 60 (a full breadboard with a sensor bar). Dense LU with partial pivoting (double precision) per island; the matrix is rebuilt only when a stamp changes (dirty flag), otherwise the factorisation is reused and only `b` changes (sources, capacitor history, motor back-EMF). Nonlinear elements (diodes, regulators in dropout, current limits) trigger a bounded Newton loop (≤ 5 iterations) with a 1 mV convergence check; if not converged, the last iterate is accepted and a diagnostic counter increments (visible in the debug overlay).

### 3.3 Island partitioning
Nets are grouped into islands by union-find over stamps that connect nets (resistors, sources, switches). Ground is shared; islands that touch the same power rail are merged (a `5V` rail island usually contains most of the circuit). Each island has a dirty flag set by: pin events on mixed nets, stamp changes (knob turned, switch pressed, component added/removed, damage), power-model state changes (fuse trip, regulator mode change). Islands without dirty flags and without time-dependent elements (capacitors, motors, batteries under load) skip the solve.

### 3.4 MCU pin model in the solver
From [05 §4](05-arduino-emulation-spec.md): output-high = source 5 V (VCC net voltage, not a constant) with R_oh = 30 Ω default (45 Ω worst case: VOH ≥ 4.1 V at −20 mA per the ATmega328P datasheet, [R4 §2](research/R4-electrical-physics-facts.md)); output-low = sink with R_ol = 25 Ω default (40 Ω worst case: VOL ≤ 0.8 V at 20 mA); input pull-up 35 kΩ (datasheet 20–50 kΩ); input floating = 100 MΩ. Pin current is read back from the solve for the failure model (§6). The MCU VCC is the `5V` net voltage, so the pin's source voltage sags with the rail, and the ADC reference (AVCC) sags too — `analogRead` results then drift exactly as on a real Uno on a weak battery.

### 3.5 ADC sampling
On ADC conversion start ([05 §5](05-arduino-emulation-spec.md)), the emulator asks the net's current voltage; the sample-and-hold is instantaneous. The reference is the AVCC net voltage (default), the internal 1.1 V bandgap, or the AREF net. Input impedance is not modelled except for a warning when the source impedance exceeds 10 kΩ (datasheet recommendation) — the LDR-with-no-divider mistake shows up as a wiring-check warning and as a slightly noisy reading (+2 LSB noise), not as a wrong voltage.

### 3.6 Buses (I2C, SPI, UART) on nets
I2C: SDA/SCL are `bus` nets; the MCU TWI and each device drive `OpenDrainLow` or release; the level is high only if a pull-up exists (module pull-ups 4.7–10 kΩ on most breakout boards, or the MCU's internal 35 kΩ when `Wire` enables it — which works marginally at 100 kHz and is flagged as a warning, matching real practice). Without any pull-up the bus reads low/undefined → `Wire` reports NACK/timeouts, the I2C scanner finds nothing. SPI and UART are push-pull digital nets; bit timing is produced by the emulator's USART/SPI ([05 §5](05-arduino-emulation-spec.md)). A UART device receiving at the wrong baud produces framing errors and garbage bytes.

### 3.7 Determinism and performance
No wall clock; fixed iteration bounds; deterministic ordering of nets and stamps (sorted by id). Budget: ≤ 0.3 ms per 1 ms tick for 60 nodes on the min-spec CPU — a 60×60 LU is ≈ 70 k flops (< 50 µs); the budget is dominated by bookkeeping and the Newton loop, so islands are kept small and factorisations cached.

### 3.8 First implementation: the digital slice (2026-09-24)

Until the solver of §3.1–3.7 exists, `CoreEngine.Sim.Design.CircuitAnalysis` reads the Garage's wires at the level the obstacle-avoider class of robots needs. It is the same netlist idea (union-find over part/pin keys, the Uno's GND pins joined inside the board) with fixed rules instead of MNA:

- **Power**: the L298N has its motor supply when +12V reaches battery + and GND battery −; its 78M05 then feeds the +5V terminal (5V-EN jumper fitted). The Uno runs from VIN on battery +, or from its 5V pin on the L298N's +5V, and needs a GND on battery −. The HC-SR04 needs 5 V (the Uno's 5V or the L298N's +5V) and a ground. From 4×AA the 78M05 gives a little under 5 V (an information note, as on the real kit, [§4.3](#43-l298n-module-power)).
- **Damage at power-on** ([§6](#6-failure-and-damage-model)): battery + on the Uno's 5V pin burns the board (F7), battery + on the sensor's VCC burns the sensor (F26). The arena marks the part burnt when the robot is switched on with a charged battery; Check & repair replaces it.
- **Signals**: which Uno pin reaches each L298N input and the sensor's TRIG and ECHO; two Uno pins joined by wires are reported. A floating input reads low. ENA/ENB keep their jumpers (channel enabled) unless a wire takes the pin to the Uno; then the channel follows that pin (PWM on enable is sampled once per 10 ms step until the solver arrives).
- **Motors**: a motor is driven when M+ and M− sit on OUT1/OUT2 or OUT3/OUT4 of a powered driver; M+ on OUT2 or OUT4 reverses it. The right motor is mounted turned round, so the same voltage turns its wheel the other way (`DriveMap.MountSign`): the kit swaps the right motor's leads, and wiring both motors alike makes the robot spin. A motor on an Uno pin is reported (40 mA at most against up to 1 A).
- Findings carry a code and arguments (`noBattery`, `shortCircuit`, `driverUnpowered`, `noBoard`, `board5vOvervoltage`, `boardUnpowered`, `noGround`, `driver5vLow`, `pinsJoined`, `sonarOvervoltage`, `sonarUnpowered`, `sonarPin`, `inputFloating`, `motorOnGpio`, `motorNotConnected`); the UI words them in English, Uzbek and Russian.

## 4. Power model

### 4.1 Batteries
A battery pack = N cells in series (and optionally P in parallel). Per chemistry: open-circuit voltage curve `OCV(SoC)`, internal resistance `R_i(SoC)`, capacity `C` (Ah) at a reference rate, and a Peukert-style rate factor. State: `SoC ∈ [0,1]`, temperature (ignored in v1).

Per 1 ms tick: `I` from the solve → `SoC −= I·Δt / (3600·C_eff)`; `V_terminal = OCV(SoC) − I·R_i(SoC)` is the Norton source for the next solve (one-tick lag, acceptable at 1 kHz).

| Chemistry (cell) | OCV full → empty | R_i fresh → depleted | Capacity | Notes / sources |
|---|---|---|---|---|
| AA alkaline | 1.60 → 0.90 V (plateau ≈ 1.25 V mid) | 0.15 → 0.6 Ω (rises steeply near empty) | ≈ 2.8 Ah at 25 mA, ≈ 1.2 Ah at 500 mA [VERIFY exact curve] | Energizer E91 datasheet: nominal IR 150–300 mΩ; capacity falls sharply with drain rate ([R4](research/R4-electrical-physics-facts.md), https://data.energizer.com/pdfs/e91.pdf) |
| 9 V alkaline (6 cells) | 9.5 → 5.4 V | 1.0 → 3 Ω | ≈ 0.55 Ah at 25 mA; collapses at > 0.5 A | https://data.energizer.com/pdfs/522.pdf; IR 1–2 Ω blog-grade [VERIFY]. This is why a 9 V block cannot run motors: 1.5 A × 1.5 Ω = 2.3 V sag |
| AA NiMH | 1.40 → 1.00 V (flat 1.2 V) | 0.03 → 0.06 Ω | 2.0–2.3 Ah | Energizer NH15: 30–40 mΩ |
| 18650 Li-ion | 4.20 → 2.50 V (3.6 V nominal) | 0.018–0.026 Ω | 2.5–3.0 Ah | Samsung 25R/30Q datasheets |
| LiPo 2S pack | 8.40 → 6.00 V | ≈ 0.02 Ω total [VERIFY] | 0.5–2 Ah; max current = C-rating × capacity | Over-discharge below 3.0 V/cell flagged as damage (§6) |

USB from the PC: ideal 5.0 V with 0.3 Ω plus the board's 500 mA polyfuse; USB power bank: 5.0–5.1 V, 1–2 A limit, shuts off below ≈ 50 mA load after 30 s (real power banks do this — a classic "my robot turned off" lesson, flagged in the event log).

### 4.2 Uno power path
```
USB_5V --[polyfuse 0.5 A]--+--[FDN340P source switch]--> 5V rail
                           |
VIN(jack) --[diode 0.5 V]--+--> VIN --[NCP1117 LDO]------> 5V rail --[LP2985 LDO]--> 3V3
```
- **Polyfuse (MF-MSMF050-2)**: hold 0.5 A, trip ≈ 1.0 A; model: thermal accumulator `E += (I² − I_hold²)·Δt` when I > I_hold; trips when E exceeds the energy for a 1 s trip at 1 A [VERIFY]; while tripped, resistance 10 kΩ; resets when the current falls below 0.1 A for 5 s (real polyfuses cool in seconds to minutes).
- **NCP1117 5 V**: regulation when `VIN − 5.0 ≥ V_drop(I)` with `V_drop ≈ 1.07 V at 0.8 A` (≈ 1.2 V max) scaling with current (0.9 V at 0.1 A [VERIFY]); otherwise `V_out = VIN − V_drop` (dropout region); current limit 1.0 A (datasheet 1.0–2.2 A; use 1.2 A); thermal: `P = (VIN − V_out)·I`, `T_j += (P·θJA − (T_j − T_amb))·Δt/τ` with θJA = 160 °C/W (SOT-223 on the Uno's pad, datasheet) and τ ≈ 20 s [VERIFY]; **thermal shutdown at 175 °C**, restart at 155 °C (hysteresis assumed [VERIFY]). Consequence: at VIN = 12 V and 0.5 A the dissipation is 3.5 W → 560 °C rise → shutdown after seconds — matching the Uno's "may overheat above 12 V" warning ([R4](research/R4-electrical-physics-facts.md)).
- **Source selection**: the 5V rail is fed by USB only when VIN < 6.6 V (the LMV358 comparator turns the FDN340P off above that); both present → VIN wins, as on the real board.
- **LP2985 3.3 V**: 150 mA device limit, 50 mA board rating (the input comes from the 5V rail); above 150 mA → current limit, above 50 mA → warning.
- **Decoupling**: a lumped 47 µF on the 5V rail (the board's electrolytics + ceramics) as a backward-Euler capacitor, so a 1–2 ms dip is partially filtered as on hardware.
- **Brown-out**: the ATmega's BOD compares the 5V rail (VCC) against 2.7 V (Uno fuse E=0xFD, [R1 §5](research/R1-avr-emulation-and-toolchain-facts.md)); VCC < 2.7 V for > 2 µs (evaluated at the electrical tick, so ≥ 1 ms in practice, plus sub-tick events from the fuse/regulator transitions) → BORF reset in the emulator; recovery after the datasheet start-up delay (≈ 65 ms with the default SUT fuses [VERIFY]) → the sketch restarts (plus the bootloader delay). Between 2.7 V and 4.5 V the chip runs but is **out of spec at 16 MHz** (datasheet requires 4.5 V for 16 MHz [VERIFY]); the game does not corrupt execution but shows an "out of spec" warning and an ADC reference error (AVCC ≠ 5 V makes `analogRead` values wrong — exactly what happens on hardware).

Nano: 5 V from USB through a Schottky diode (0.3 V drop → 4.7 V on USB power on many clones [VERIFY]) or from VIN through a 78M05-class regulator (dropout 2 V, 0.5 A [VERIFY]). Mega: same as the Uno.

### 4.3 L298N module power
- `+12V` (Vs) terminal: 5–35 V into the bridge; `GND`; `+5V` terminal: with the **5V-EN jumper** fitted, the on-board 78M05 regulates Vs down to 5 V (max 0.5 A) and the +5V terminal is an **output** that can power the Uno; with the jumper removed, +5V is an **input** (logic supply 4.5–7 V) and must be fed from the Uno's 5 V.
- Rule (module documentation, [R4](research/R4-electrical-physics-facts.md)): remove the jumper when Vs > 12 V or the 78M05 overheats (dissipation (Vs − 5)·I_logic; the L298 logic draws 24–36 mA quiescent, so at Vs = 20 V that is 0.5 W plus whatever the Uno draws through it).
- Thermal model for the 78M05: θJA 65 °C/W [VERIFY], shutdown 150 °C.
- Failure: Vs applied with the jumper fitted and Vs > 16 V → the regulator overheats within tens of seconds ([R4](research/R4-electrical-physics-facts.md) forum reference); Vs and +5V both driven (Uno 5V connected to the module's +5V with the jumper fitted) → two regulators fight → contention warning, small circulating current, no damage (real outcome).

### 4.4 Other supplies
- **LM2596 buck module**: input 4.5–40 V, output set by the pot (1.25–37 V), 2 A continuous (3 A with heatsink), efficiency ≈ 85 % [VERIFY]; modelled as an ideal source with 0.05 Ω, a current limit and an input current `I_in = V_out·I_out/(η·V_in)`; dropout: needs V_in ≥ V_out + 1.5 V.
- **Power switches** (slide/rocker on holders): ideal switches (§3.2).
- **Barrel jack 9 V adapter**: 9.0 V ideal with 0.2 Ω, 1 A limit.

### 4.5 Worked example — why the Uno resets when the motors start
Setup: 4×AA alkaline pack → Uno VIN and L298N Vs (jumper fitted) → two TT motors (R ≈ 4 Ω [VERIFY, from 6 V / 1.5 A stall]) started from stall with `analogWrite(255)`.

| Step | Fresh cells (OCV 1.55 V, R_i 0.2 Ω each) | Half-used cells (OCV 1.40 V, R_i 0.4 Ω each) |
|---|---|---|
| Pack no-load | 6.2 V, R_pack 0.8 Ω | 5.6 V, R_pack 1.6 Ω |
| Stall inrush (first ≈ 20 ms, no back-EMF): each motor draws (V_motor)/4 Ω after the bridge drop ≈ 1.8 V at 1 A | Solve: pack ≈ 6.2 − 0.8·I; I_total ≈ 2 × 0.9 A + 0.1 A ≈ 1.9 A → pack ≈ **4.7 V** | I_total ≈ 2 × 0.6 A + 0.1 A → pack ≈ **3.5 V** |
| NCP1117 input | 4.7 − 0.5 (diode) = 4.2 V < 5.0 + 1.0 → dropout: 5V rail ≈ **3.3 V** | 3.0 V → 5V rail ≈ **2.1 V** |
| ATmega BOD (2.7 V) | Not triggered; chip runs out of spec; ADC reference wrong; HC-SR04 (needs 4.5 V) misreads | **Brown-out reset** → sketch restarts → motors stop → voltage recovers → sketch starts motors again → **reset loop** (the classic symptom) |
| Event log | "5V rail sagged to 3.3 V for 22 ms when M1/M2 started (pack 4.7 V under 1.9 A; battery internal resistance 0.8 Ω)" | "Brown-out reset at t = 3.210 s: VCC 2.1 V < 2.7 V. Cause: pack voltage 3.5 V under 1.3 A. Fix: separate motor supply, fresher cells, or a lower-resistance pack" |
| Same circuit with 2×18650 (8.2 V, 0.04 Ω) | Pack sags 0.1 V; regulator input 7.6 V; 5V rail stays 5.0 V; no event | — |
| Same circuit with a 9 V block (9.4 V, 1.5 Ω) | I ≈ 1.6 A → pack ≈ 7.0 V, regulator OK, but motors see ≈ 5 V and the block is empty in ≈ 15 min; event: "9 V battery: 2.4 V lost in internal resistance" | — |

(The steady-state after the inrush is lower: as the motors spin up, back-EMF reduces current to ≈ 0.2–0.3 A each. The reset loop happens only if the inrush dips below 2.7 V; the game reproduces both regimes.)

## 5. Component electrical model catalogue

Each model type is a C# class implementing `IElectricalModel` (§7). Per-part parameter values are in [09](09-components-catalog.md); this section defines the equations, stamps and monitored limits.

### 5.1 Resistor
Parameters: R, tolerance (5 %), power rating P_max (0.25 W). Stamp: conductance. Limits: P = V²/R > 2·P_max → thermal accumulator (τ 10 s) → "burnt resistor" (open circuit) — rare in normal builds but real (220 Ω across 12 V = 0.65 W).

### 5.2 Potentiometer
R_total (10 kΩ), knob angle k ∈ [0,1] (linear taper; log taper option). Two stamps around the wiper. Limits: wiper current > 20 mA warning (wiper track damage) [VERIFY].

### 5.3 LDR (GL5528)
R = R_10lux · (E/10 lux)^(−γ) with R_10lux = 10 kΩ (datasheet 8–20 kΩ), γ = 0.7 [VERIFY], clamped to ≥ 1 MΩ dark and ≤ 200 Ω at 10 000 lux [VERIFY]; illuminance E from the world query ([07 §5](07-physics-world-sensors-spec.md)); first-order response τ_rise 45 ms / τ_fall 55 ms ([R4](research/R4-electrical-physics-facts.md)).

### 5.4 Capacitor
C, voltage rating, polarity (electrolytic). Backward-Euler stamp. Limits: reverse voltage > 1.5 V on an electrolytic or V > 1.2·rating → accumulator (τ 5 s) → "vented capacitor" (open, smoke). Ceramic: no polarity.

### 5.5 Diode (1N4007, 1N4148)
Piecewise-linear: Vf 0.7 V (Si) with dynamic resistance 0.1 Ω [VERIFY]; reverse leakage 1 µA; reverse breakdown at V_RRM (1000 V / 100 V) not reachable in this game. Limits: I > I_F(AV) (1 A / 0.2 A) → thermal accumulator.

### 5.6 LED
Parameters per colour (5 mm): Vf at 20 mA — red 1.8–2.0 V, yellow 2.1 V, green 2.2 V (older) / 3.2 V (InGaN), blue 3.2 V, white 3.3 V ([R4](research/R4-electrical-physics-facts.md)); dynamic resistance ≈ 5–10 Ω [VERIFY]; I_nominal 20 mA; I_max 30 mA continuous (datasheet absolute max; pulsed 100 mA at 10 % duty); reverse max 5 V.
Stamp: as diode with the colour's Vf. Brightness for rendering: L = (I / 20 mA)^0.8 clamped, PWM-averaged per frame with optional true flicker below 100 Hz.
Limits: damage accumulator `D += max(0, I/I_max − 1)² · Δt / τ_led`, τ_led = 10 s → at 45 mA (1.5×) failure after ≈ 40 s, at 60 mA (2×) after ≈ 10 s, at 90 mA after ≈ 2.5 s. Failure = open circuit, "burnt" texture, event log with the measured current and the 30 mA limit. Reverse voltage > 5 V (e.g., reversed across 9 V) → immediate failure. Note: the true failure time of a real LED is statistical; the time constants are a **teaching choice** and are documented as such in the Notebook ("real LEDs may survive longer or die faster").

### 5.7 Push button / switches
Ideal switch, contact resistance 50 mΩ; tactile buttons have a 5 ms bounce burst (3–8 transitions, deterministic sequence) when pressed/released so that debouncing lessons work. Limits: none (rated 50 mA; a switch across 5V/GND is a short — handled by the source's limits).

### 5.8 NPN transistor as switch (2N2222 simplified)
Base current I_b = (V_b − 0.7)/R_b (external base resistor required; without one, base current is limited only by the pin: pin over-current path). Collector current I_c = min(hFE·I_b, I_c,sat) with hFE = 100 [VERIFY], Vce_sat = 0.2 V when saturated, else linear region Vce = Vcc − I_c·R_load. Limits: I_c > 800 mA → thermal accumulator (τ 3 s); P > 0.5 W → same.

### 5.9 Relay module (5 V, 1 channel)
Input pin: logic input (active low on most modules [VERIFY]) driving an optocoupler + transistor; coil current 70 mA from the module's VCC ([VERIFY], typical SRD-05VDC); contact: ideal switch with 10 ms operate/release delay and a click sound. Limits: coil powered from the Uno's 5 V pin adds 70 mA to the regulator budget.

### 5.10 DC motor (electrical side)
Parameters: R (winding), L, Ke (V·s/rad, motor shaft), gear ratio N, mechanical model in [07 §4](07-physics-world-sensors-spec.md). Electrical equation integrated at 1 kHz: `di/dt = (V_terminal − R·i − Ke·ω_motor)/L`, semi-implicit Euler (stable for τ = L/R ≈ 0.1–1 ms with Δt = 1 ms when integrated implicitly: `i_new = (i + Δt·(V − Ke·ω)/L) / (1 + Δt·R/L)`). The motor appears in the MNA as a Norton source (Ke·ω, R) — the inductive term is handled inside the model. Torque `T = Kt·i` (Kt = Ke) is passed to physics. Derived values for the TT motor (6 V, 1:48): R ≈ 4 Ω, Ke ≈ 4.3 mV·s/rad at the motor shaft, L ≈ 1 mH [VERIFY] ([R4 §2, §4](research/R4-electrical-physics-facts.md)).
PWM: the bridge applies V_s or 0/−V_s at the PWM rate (490/980 Hz). Because τ = L/R ≈ 0.25 ms < 1/980 Hz, the current is discontinuous at low duty in fast decay and continuous in slow decay ([R4 §4](research/R4-electrical-physics-facts.md), TI SLVA321). v1 rule: **slow decay (L298 with one input high and ENA PWM) → V_avg = D·V_s applied at 1 kHz**; fast decay (both inputs toggled) → `V_avg = D·V_s − (1−D)·(V_s + 2·V_D)` clamped, giving the weaker low-duty response that users observe. Sub-tick PWM edges are not simulated individually; the duty is measured from the pin waveform over the last PWM period.
Limits: stall heating — `T_wind += (i²·R·θ − (T_wind − T_amb))·Δt/τ` with θ = 40 °C/W, τ = 60 s [VERIFY]; > 120 °C → winding failure (open). Brushes/gears are not modelled. Implemented in `core/CoreEngine.Sim/Components/MotorWinding.cs` (2026-09-24). With these parameters a TT motor stalled at 1.05 A (6 V pack through the L298N) burns after about 50 s, while the F18 example in §6 says 4 minutes; which is closer to a real TT motor needs a careful measurement (θ and τ from the winding temperature of a stalled motor, stopping well before damage).

### 5.11 H-bridge L298 (module)
Inputs IN1, IN2 (per channel), ENA (jumper high by default or PWM from the MCU); logic thresholds V_IL ≤ 1.5 V / V_IH ≥ 2.3 V ([R4](research/R4-electrical-physics-facts.md), ST datasheet). Truth table per channel:

| ENA | IN1 | IN2 | OUT1/OUT2 |
|---|---|---|---|
| 0 | x | x | High-Z (coast; motor free-wheels through the flyback diodes) |
| 1 | 0 | 0 | Both low → brake (slow decay) |
| 1 | 1 | 1 | Both high → brake |
| 1 | 1 | 0 | OUT1 = Vs − V_source(I), OUT2 = V_sink(I) → forward |
| 1 | 0 | 1 | Reverse |

Saturation drops (datasheet, [R4](research/R4-electrical-physics-facts.md)): source 1.35 V typ at 1 A (2.0 V at 2 A), sink 1.2 V typ at 1 A (2.3 V at 2 A) → total 1.8 V typ at 1 A, up to 4.9 V worst case at 2 A. Model: `V_drop(I) = V0 + k·I` fitted to the typicals: source 0.95 + 0.4·I, sink 0.8 + 0.4·I (V, A) [VERIFY fit]; stamped as a voltage source with series resistance per leg. Quiescent: 13–50 mA depending on input states (datasheet Is values), plus the module's 78M05 losses.
Thermal: `P = V_drop_total·I` per channel; `T_j` with Rth j-amb 35 °C/W (with the module's heatsink [VERIFY]) and τ = 30 s; thermal shutdown at 130 °C (Tj max), automatic restart at 110 °C. Current limit: none in the chip — > 2 A continuous triggers the thermal path; > 3 A → immediate damage (bond wires). Flyback diodes on the module are modelled as ideal for coast/brake behaviour.
The 2 V drop is the single most important fact players learn about motor drivers: "12 V in, ≈ 10 V to the motor; 6 V in, ≈ 4 V to the motor".

### 5.12 MOSFET bridges (TB6612FNG, DRV8833)
Same truth table (with STBY/nSLEEP), drop `R_ds(on)·I` with R_ds(on) ≈ 0.5 Ω total per channel (TB6612) / 0.36 Ω (DRV8833) [VERIFY]; current limits 1.2 A (3.2 A peak) / 1.5 A (2 A peak) with built-in over-current shutdown; thermal shutdown. Logic 2.7–5.5 V; motor supply 4.5–13.5 V (TB6612) / 2.7–10.8 V (DRV8833) [VERIFY].

### 5.13 Servo (electrical side; mechanics in doc 07)
Supply 4.8–6 V (SG90 max 6.6 V); current: idle 10 mA, moving 100–250 mA, stall 650–700 mA (SG90, blog-grade [VERIFY]); MG996R: idle 10 mA, run 500–900 mA, stall 2.5 A ([R4](research/R4-electrical-physics-facts.md)). The model draws `I = I_idle + (I_stall − I_idle)·|T_load|/T_stall` (plus a 30 ms inrush at I_stall when a move starts), stamped as a current sink. Signal decode: on the signal net, pulse width is measured at cycle resolution; valid frames are 500–2500 µs at 40–200 Hz; the Servo library default 544–2400 µs maps to 0–180°; pulses outside range are ignored and the last target is kept; no pulses for > 100 ms → the servo goes limp (no holding torque) — real cheap servos vary here [VERIFY]. Powering a servo from the Uno's 5V pin adds up to 0.7 A to the regulator/polyfuse budget — the USB polyfuse trips when a servo stalls (a real and common failure).

### 5.14 Generic logic device
Parameters: Vcc range (min/max/abs max), I_quiescent, I_active, input threshold family (TTL 0.8/2.0 V; CMOS 0.3·Vcc/0.7·Vcc), output type (push-pull with R_out ≈ 50 Ω, or open-drain), output high level (= Vcc). Below Vcc_min the device is "unpowered" (outputs high-Z, behaviour model paused); above Vcc_abs_max (e.g., 5.5 V for 5 V modules, 3.6 V for 3.3 V-only parts) → damage accumulator (τ 1 s). Used by HC-SR04 (5 V, 15 mA active, < 2 mA idle), IR obstacle module (3.3–5 V, 20 mA), TCRT5000 bar (5 V, IF 20 mA per channel), DHT11/22 (2.5 mA), HC-05 (30–40 mA pairing, 8 mA connected [VERIFY]), encoders, KY-series modules, buzzers (active: 25–36 mA at 5 V).

### 5.15 I2C devices
PCF8574 LCD backpack (0x27 / 0x3F; backlight LED 20–30 mA [VERIFY] + 1 mA logic), SSD1306 OLED (0x3C; 8–20 mA depending on lit pixels [VERIFY]), GY-521 MPU6050 (0x68/0x69; 4 mA; 3.3 V regulator on the module so 5 V VCC is correct; 4.7 kΩ pull-ups on the module [VERIFY]). Pull-up presence per module is a parameter; the bus rule of §3.6 applies.

### 5.16 WS2812B strip/ring
Vdd 3.5–5.3 V; 60 mA per LED at full white (20 mA per colour) [R4 datasheet]; current = Σ over LEDs of (r+g+b)/765 × 60 mA; 8-LED stick full white = 0.48 A, 12-ring = 0.72 A — enough to trip the USB polyfuse when powered from the Uno 5V pin (real lesson). DIN decode at cycle resolution (0.4/0.85 µs ± 150 ns); a first LED fed 5 V logic from a 3.3 V source would fail (not v1). Missing 470 Ω data resistor and 1000 µF cap are warnings, not failures.

### 5.17 Stepper 28BYJ-48 + ULN2003
Coil resistance ≈ 50 Ω [VERIFY]; 5 V → 100 mA per energised coil, ≈ 240 mA average in half-step; ULN2003 is a Darlington sink (1.0 V drop); the board's inputs IN1–IN4 are logic inputs. Step sequence decoded into the mechanical model ([07 §4](07-physics-world-sensors-spec.md)).

### 5.18 Regulators and fuses
See §4.2–4.4 (NCP1117, LP2985, 78M05, LM2596, polyfuse). Parameterised by dropout(I), current limit, θJA, shutdown/restart temperatures, hold/trip currents.

## 6. Failure and damage model

Two accumulator types: **instant** (a threshold crossing kills or trips at once) and **thermal/I²t** (`D += f(overload)·Δt/τ`, failure at D ≥ 1, slow recovery `D −= Δt/τ_cool` when not overloaded). All thresholds come from datasheets ([R4](research/R4-electrical-physics-facts.md)); time constants are design choices marked as such. Hard failures require "Replace part"; soft failures recover when the condition clears.

| # | Part | Condition | Model | Effect | Player feedback (event log) | Kind |
|---|---|---|---|---|---|---|
| F1 | LED | I > 30 mA | I²t, τ 10 s (§5.6) | Open circuit, burnt texture, smoke | "LED burnt: 62 mA through a 30 mA LED for 9.8 s. Add a 220–330 Ω resistor." | Hard |
| F2 | LED | Reverse bias ≤ 5 V | — | Dark, no damage | Hover: "LED reversed: no current flows" | — |
| F3 | LED | Reverse > 5 V | Instant | Open circuit | "LED destroyed by 9 V reverse voltage (max 5 V)" | Hard |
| F4 | MCU pin | I_pin > 40 mA (abs max) | I²t, τ 30 s at 1.5× | Pin "weak" (R_oh/R_ol ×4) at D = 0.5, then dead (high-Z) at D = 1 | "Pin D9 damaged: 58 mA (abs. max 40 mA) for 31 s — motor connected directly to a GPIO" | Hard |
| F5 | MCU port group | ΣI_OL > 100 mA or ΣI_OH > 150 mA per group (datasheet groups) | I²t, τ 20 s | Whole group weakens/dies | "Port group B0–B5/D5–D7 overloaded: 130 mA sink" | Hard |
| F6 | MCU total | ΣI_VCC/GND > 200 mA | I²t, τ 10 s | Chip dead (board dark, no reset possible) | "ATmega328P destroyed: 240 mA total through GND pins" | Hard |
| F7 | MCU / 5V rail | V_5V > 5.5 V (e.g., 9 V into the 5V pin) | Instant above 6.0 V; τ 2 s between 5.5–6.0 V | Chip dead; 16U2 dead; USB port "damaged" | "9.1 V applied to the 5V pin (max 5.5 V): board destroyed" | Hard |
| F8 | Board | 5V/GND reversed (battery leads swapped on the 5V pin) | Instant | Chip dead | "Reverse polarity on 5V/GND" | Hard |
| F9 | Board | VIN reversed | — | No power (protection diode) | "VIN reversed: the protection diode blocks; nothing happens" | Soft |
| F10 | USB polyfuse | I > 1 A (trip) | Accumulator (§4.2) | Fuse high-resistance → board browns out/resets; recovers after ≈ 5 s below 0.1 A | "USB fuse tripped: 1.3 A drawn from USB (limit 0.5 A). Servo/motors need their own supply." | Soft |
| F11 | NCP1117 | T_j > 175 °C | Thermal (§4.2) | 5V rail off until 155 °C | "5 V regulator overheated: VIN 12 V at 0.6 A = 4.2 W. Use a lower VIN or a buck converter." | Soft |
| F12 | NCP1117 | I > 1.2 A | Current limit | Rail sags | "5 V regulator current limit reached (1.2 A)" | Soft |
| F13 | LP2985 3V3 | I > 150 mA | Current limit; τ 10 s thermal | Rail sags; overheat | "3.3 V pin overloaded: 180 mA (board rating 50 mA)" | Soft |
| F14 | L298N | T_j > 130 °C | Thermal (§5.11) | Channel off until 110 °C | "L298N thermal shutdown: 2.1 A per channel, drop 3.9 V = 8 W" | Soft |
| F15 | L298N | I > 3 A | Instant | Channel dead | "L298N destroyed: 3.4 A (max 3 A peak)" | Hard |
| F16 | L298N 78M05 | Vs > 12 V with jumper, or T > 150 °C | Thermal (§4.3) | Module 5 V off (Uno powered from it resets) | "L298N on-board regulator overheated at Vs = 16 V. Remove the 5V-EN jumper." | Soft |
| F17 | Servo | Stalled (load torque ≥ stall torque) | Thermal τ 60 s [VERIFY] | Servo dead (stripped gears/burnt motor) | "SG90 stalled for 65 s at 0.7 A: gears stripped" | Hard |
| F18 | DC motor | Winding > 120 °C | Thermal (§5.10) | Motor dead | "TT motor overheated after 4 min at stall (1.1 A)" | Hard |
| F19 | Battery | SoC < 0 (alkaline: V < 0.9 V/cell) | Instant | Pack empty, V → 0 | "Battery empty after 23 min" | Soft (replace) |
| F20 | Li-ion/LiPo | V_cell < 3.0 V | Instant | Pack damaged (capacity −50 %) | "LiPo over-discharged: 2.9 V/cell" | Hard |
| F21 | Battery | Shorted (I > 10 A alkaline / 30 A Li-ion) | τ 3 s | Pack heats; alkaline: leaks (dead); LiPo: "fire" VFX | "Battery short circuit: 7.8 A" | Hard |
| F22 | Any two push-pull outputs | Driving opposite levels on one net | Contention current via F4 | Pin damage on both | "Output contention on net N12: D3 high vs sensor OUT low" | Hard |
| F23 | I2C bus | No pull-up | — | Bus non-functional | "I2C bus has no pull-up resistors: SDA cannot go high" | Soft |
| F24 | Capacitor (electrolytic) | Reverse or over-voltage | τ 5 s | Vented, open, smoke | "100 µF capacitor reversed on 9 V" | Hard |
| F25 | Resistor | P > 2·P_max | τ 10 s | Open, burnt | "220 Ω resistor burnt: 0.65 W across 12 V (rating 0.25 W)" | Hard |
| F26 | HC-SR04 / 5 V logic device | Vcc > 5.5 V | τ 1 s | Dead | "HC-SR04 destroyed: 9 V on VCC" | Hard |
| F27 | 3.3 V-only device (v1.x) | 5 V logic on an input | τ 1 s | Dead | — | Hard |
| F28 | Power bank | Load < 50 mA for 30 s | — | Turns off | "Power bank switched off (load too small)" | Soft |

Every hard failure emits a smoke VFX (scaled to the dissipated power), a dark "burnt" material and a Notebook link. A "Replace part" button restores it (always free).

## 7. Interfaces (C#, `CoreEngine.Sim.Electrical`)

```csharp
public interface IElectricalModel
{
    void Attach(ModelContext ctx, IReadOnlyList<Net> pinNets);   // called on placement/wiring change
    void OnPinEvent(long cycle, int pinIndex, DigitalLevel level); // digital nets only; cycle-stamped
    void Stamp(MnaBuilder mna);                                  // called when the island is (re)built or the model is dirty
    void Step(double dtMs, SimContext ctx);                      // 1 kHz: integrate dynamics, read currents, update sources
    void CheckLimits(FailureContext f);                          // 1 kHz: accumulators, emit warnings/failures
    void WriteState(IStateWriter w); void ReadState(IStateReader r); // temperatures, SoC, damage, timers
}

public sealed class Net { public int Id; public NetKind Kind; public double Voltage; public bool Floating; public IReadOnlyList<PinRef> Pins; }
public enum DigitalLevel { Low, High, HiZ, Unknown }
public enum DriveStrength { HiZ, Weak, OpenDrainLow, Strong }

public sealed class MnaBuilder {  // per island
    public int NodeOf(Net n); public void Conductance(int a, int b, double g);
    public void CurrentInto(int a, double i); public void NortonSource(int a, int b, double v, double rs);
    public void Nonlinear(INonlinearStamp s); }

public interface ISolverEvents {   // consumed by the app layer for UI/telemetry
    void NetSnapshot(ReadOnlySpan<NetSample> samples);           // per render frame
    void Damage(ComponentId id, FailureCode code, string detail, double measured, double limit);
    void Warning(ComponentId id, WarningCode code, string detail);
    void FuseEvent(ComponentId id, bool tripped); }
```

`ElectricalWorld` owns nets, islands, models and the event queue; `Advance(1 ms)` = apply queued pin events (with immediate re-solves of mixed islands at the event cycle) → `Step` all models → solve dirty islands → `CheckLimits` → publish samples. Telemetry sampling: any net voltage or model quantity (current, temperature, SoC) can be subscribed at 1 kHz or decimated.

## 8. Measurement tools
| Tool | Model |
|---|---|
| Voltmeter | 10 MΩ input resistance stamped between probes (so it loads high-impedance nodes realistically) |
| Ammeter (series) | Inserted into a wire: 0.1 Ω shunt; the wire is split into two nets around it; range 2 A |
| Virtual clamp | Reads the solver's branch current for the hovered wire/pin without stamping anything; labelled "virtual" |
| Ohmmeter | Sim paused; injects 1 mA between probes into a copy of the island with sources zeroed; reports V/I |
| Continuity | Same as ohmmeter with a beep below 10 Ω |
| Logic probe | Reads the digital level and detects pulsing (edge count over 100 ms → frequency) |
| Logic analyser (v1.x) | Subscribes to pin events at cycle resolution; decoders for UART, I2C, SPI, PWM duty, servo pulse; 8 channels, 10 s buffer |

## 9. Validation tests (`CoreEngine.Sim.Tests/Electrical`)
| Test | Expected | Tolerance |
|---|---|---|
| Divider 10 kΩ / 10 kΩ on 5.00 V | 2.500 V | 1 mV |
| Red LED (Vf 1.9 V) + 220 Ω on D13 high (R_oh 30 Ω) | I ≈ (5 − 1.9)/250 = 12.4 mA; D13 pin voltage ≈ 4.63 V | 5 % |
| Same with 330 Ω | ≈ 8.6 mA | 5 % |
| LED directly on D13 | I ≈ (5 − 1.9)/(30 + 7) ≈ 84 mA → F4 pin warning at once, F1 after ≈ 3 s | event order |
| Pot sweep 0→1 with 5 V | Wiper 0→5 V linear | 1 % |
| L298N truth table at Vs = 12 V, 1 A load | Forward: OUT1 − OUT2 ≈ 12 − 1.8 = 10.2 V; brake: 0 V; coast: high-Z | 0.2 V |
| PWM 50 % on ENA (slow decay) | V_avg = 0.5·(Vs − V_drop) | 3 % |
| 4×AA half-used + 2 stalled TT motors | Brown-out reset within 50 ms of motor start (§4.5) | event |
| Servo stall on USB power | Polyfuse trips within 2 s; board resets; fuse recovers after ≈ 5 s of no load | event |
| I2C scanner with MPU6050 module (pull-ups on module) | Finds 0x68 | — |
| I2C scanner with bare PCF8574 chip, no pull-ups, `Wire` internal pull-ups | Finds 0x27 with the "weak pull-up" warning | warning |
| Floating D2 read 1000 times | Not constant; deterministic across runs with the same seed | — |
| 9 V block on L298N with two motors at stall | Pack terminal < 7 V; empty within 20 min of sim time at 4× | 20 % |
| Capacitor 100 µF across 5V rail during a 10 ms 1 A dip | Dip depth reduced vs no capacitor | qualitative |

## 10. Performance and determinism
- Islands ≤ 60 nodes; dense LU; factorisation cached until stamps change; Newton loops bounded (≤ 5). Typical projects: 1–3 islands, 10–40 nodes → < 50 µs per tick.
- All accumulators and noise generators are deterministic (seeded per run); no allocation in `Advance`; state fully serialisable for replays and save/load mid-run.
- Debug overlay shows per-tick solver time, iteration counts and non-converged solves.

## 11. Open questions
1. Diode/LED linearisation: piecewise-linear (fast, robust) vs bounded Newton on the exponential (more accurate near threshold). Recommendation: exponential with 5 iterations; fall back to PWL if convergence issues appear in playtests.
2. Should contention (F22) be a hard failure by default? Real AVR pins often survive brief contention. Proposal: hard only after the I²t accumulator (≈ 30 s), warning immediately.
3. Failure time constants (LED τ 10 s, pin τ 30 s) need playtest tuning: too fast feels arbitrary, too slow hides the lesson.
4. Model the Uno's 16U2 as a load (≈ 20 mA) and its own damage path? Proposed: yes, as a generic logic device on the 5V rail.
5. Worn-breadboard contact resistance mode: v1.x or never?
6. Do we simulate the sub-tick PWM waveform for LEDs (true flicker) or only the duty? Proposed: duty for the solver, waveform only for rendering/audio.
7. Should a "quiet mode" disable floating-input noise and ADC noise for fully reproducible runs, for example in classrooms? (Also raised in doc 13 §2.)
