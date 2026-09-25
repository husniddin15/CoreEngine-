# 09 — Components Catalogue

Status: Accepted DRAFT v0.2 (2026-09-23) · Pillar P2/P3 · Values from datasheets where cited ([research/R1 §5](research/R1-avr-emulation-and-toolchain-facts.md), [research/R4 §2](research/R4-electrical-physics-facts.md)); **[VERIFY]** marks values from memory or secondary sources that must be checked against the physical part before the 3D model is made. Model types refer to [06 §5](06-electrical-simulation-spec.md) (electrical) and [07 §4–5](07-physics-world-sensors-spec.md) (behaviour); mount types to [08 §2](08-body-designer-spec.md); the JSON format to [04 §8](04-technical-design.md).

---

## 1. How to read this catalogue

**Tiers**: **MVP** = needed for the vertical slice (Phase 1, obstacle-avoider path) · **v1** = the 1.0 release · **v1.x** = after release.

**Availability** ([ADR-0007](adr/ADR-0007-monetization-free-to-play-dlc.md)): every part in sections 2–9 is **free**, except the Mega 2560, which is the first paid board pack. Paid packs are listed in Appendix C. Rule: every part in the base catalogue is free, including every part the tutorial uses.

**Fields per part** (as they appear in `component.json`): `id` · real name and common kit names · category · tier · dimensions L×W×H mm · mass g · pins (name, role, pitch/connector) · interface · supply (V range; current quiescent/active/max) · key parameters · electrical model type + parameters · behaviour type + parameters · accepted mount types · failure modes (F-numbers from [06 §6](06-electrical-simulation-spec.md)) · datasheet URL · 3D-asset notes. Each part also gets a Notebook datasheet card ([10 §4](10-content-arenas-tutorial-notebook.md)).

Pin roles: `power_in`, `power_out`, `ground`, `digital_in/out/io`, `open_drain`, `analog_in/out`, `motor`, `passive`, `signal`, `mechanical`.

### Summary

| Category | MVP | v1 | v1.x | Total |
|---|---|---|---|---|
| Boards | 1 | 2 | 0 | 3 |
| Breadboards & wiring | 6 | 5 | 2 | 13 |
| Passives & basics | 4 | 22 | 1 | 27 |
| Power | 2 | 6 | 1 | 9 |
| Motors & drivers | 2 | 6 | 4 | 12 |
| Sensors | 1 | 11 | 6 | 18 |
| Displays & output | 0 | 8 | 1 | 9 |
| Mechanical | 6 | 11 | 3 | 20 |
| **Total** | **22** | **71** | **18** | **111** |

(Resistor values, LED colours and wheel/motor variants are counted as separate entries because each is a separate catalogue item with its own parameters. MVP+v1 = 93 entries ≥ the G2 goal of 40; the MVP set of 22 matches the Phase 1 scope in [11 §4](11-roadmap.md).)

## 2. Boards

### 2.1 Arduino Uno R3 — `board-uno-r3` — **MVP**
- Dimensions 68.6 × 53.4 mm, height ≈ 15 mm with headers; mass ≈ 25 g [VERIFY]. Mounting holes Ø 3.2 mm at (14.0, 2.5), (15.3, 50.7), (66.1, 7.6), (66.1, 35.5) mm from the lower-left corner (USB jack at the left) [VERIFY against the official mechanical drawing]. USB-B jack and barrel jack overhang the left edge.
- MCU ATmega328P @ 16 MHz; flash 32 KB (0.5 KB bootloader → 32 256 bytes max sketch), SRAM 2 KB, EEPROM 1 KB ([R1 §5](research/R1-avr-emulation-and-toolchain-facts.md)). Fuses L 0xFF / H 0xDE / E 0xFD (BOD 2.7 V). Bootloader delay ≈ 1 s after reset ([05 §2](05-arduino-emulation-spec.md)).
- Power: USB 5 V through a 500 mA polyfuse; VIN/barrel 7–12 V recommended (6–20 V limit) through the NCP1117ST50T3G; 3.3 V from LP2985-33 (50 mA board rating); ATmega16U2 USB bridge; auto-reset via DTR. Board current ≈ 45 mA idle (328P ≈ 15 mA + 16U2 ≈ 20 mA + LEDs) [VERIFY].
- Electrical: `Board` composite ([06 §2.5, §4.2](06-electrical-simulation-spec.md)); behaviour: `Mcu` (ATmega328P profile) + `UsbBridge`.
- Mounts: `screw_m3` ×4, `standoff_m3`, `tape_face`. Failures: F4–F13.

Pin table (header order, left to right as printed):

| Header | Pin | MCU | Functions |
|---|---|---|---|
| Power | IOREF, RESET, 3.3V, 5V, GND, GND, VIN | — | IOREF = 5 V |
| Analog | A0–A5 | PC0–PC5 | ADC0–5; A4 = SDA, A5 = SCL; PCINT8–13; digital 14–19 |
| Digital (low) | D0 RX, D1 TX | PD0, PD1 | USART0; also wired to the 16U2 via 1 kΩ |
| | D2, D3~ | PD2, PD3 | INT0, INT1; D3 PWM = OC2B (490 Hz) |
| | D4 | PD4 | T0 external clock |
| | D5~, D6~ | PD5, PD6 | OC0B, OC0A (976.56 Hz PWM); AIN1/AIN0 on D7/D6 |
| | D7 | PD7 | — |
| Digital (high) | D8 | PB0 | ICP1 input capture |
| | D9~, D10~ | PB1, PB2 | OC1A, OC1B (490 Hz); D10 = SS |
| | D11~, D12, D13 | PB3, PB4, PB5 | MOSI/OC2A (490 Hz), MISO, SCK; D13 drives the L LED via a buffer |
| | GND, AREF, SDA, SCL | — | SDA/SCL duplicate A4/A5 (R3) |
| ICSP | MISO, VCC, SCK, MOSI, RESET, GND | — | 2×3 header |

Note the 0.16" (4.06 mm) gap between D7 and D8 headers ([05 §6](05-arduino-emulation-spec.md)).

### 2.2 Arduino Nano (v3.0, and CH340 clone) — `board-nano` — **v1**
- 45 × 18 mm, ≈ 7 g; 2 × 15 pins at 2.54 mm pitch, rows 15.24 mm apart → **breadboard-mountable** (straddles the centre channel). Mini-USB (official FT232RL) or Mini/Micro-USB (CH340G clones) [VERIFY per variant].
- ATmega328P @ 16 MHz; max sketch 30 720 bytes with the old bootloader (ATmegaBOOT, 57 600 baud) or 32 256 with the new one; A6/A7 analog-only inputs; 8 ADC channels ([R1 §5](research/R1-avr-emulation-and-toolchain-facts.md)).
- Power: USB 5 V via a Schottky diode (≈ 4.7 V on the 5V pin under USB power on many boards [VERIFY]); VIN 7–12 V via a 78M05-class regulator (UA78M05, 0.5 A [VERIFY]); 3.3 V from the USB chip (50 mA).
- Models as Uno with the `Nano` profile; mounts: `breadboard` (pins), `tape_face`, `screw_m2` (two Ø 1.8 mm holes [VERIFY]). Typical use: compact builds such as a mini N20 rover.

Pin table: D0–D13 and A0–A7 with the same MCU functions as the Uno (pins 3, 5, 6, 9, 10, 11 PWM; A4/A5 I2C; 2/3 interrupts); power pins VIN, 5V, 3V3, GND ×2, RST ×2, AREF.

### 2.3 Arduino Mega 2560 R3 — `board-mega-2560` — **v1**, paid: **Mega 2560 Pack** (`"availability": "pack:mega2560"`)
- 101.5 × 53.3 mm, ≈ 37 g; holes at the Uno positions plus (90.2, 50.7) and (96.5, 2.5) mm [VERIFY]. Same power path and 16U2 bridge as the Uno.
- ATmega2560 @ 16 MHz; 256 KB flash (8 KB bootloader → 253 952 bytes), 8 KB SRAM, 4 KB EEPROM; 54 digital (15 PWM: 2–13, 44–46), 16 analog; 4 USARTs (0: D0/D1, 1: D19/D18, 2: D17/D16, 3: D15/D14); INT on 2, 3, 18–21; SPI on 50–53 (+ ICSP); I2C on 20/21 ([R1 §5](research/R1-avr-emulation-and-toolchain-facts.md)).
- Models: `Mcu` (ATmega2560 profile, [05 §2](05-arduino-emulation-spec.md)). Typical use: large builds that need more pins, serial ports or memory.

## 3. Breadboards and wiring

| id | Part | Tier | Dimensions / mass | Electrical | Notes |
|---|---|---|---|---|---|
| `bb-half-400` | Half-size breadboard, 400 tie-points | MVP | 82 × 55 × 9 mm, ≈ 40 g [VERIFY] | `Breadboard` topology: 30 columns × (a–e, f–j) strips; 2 rails per side, 25 holes each, **not split** | Adhesive back → `tape_face` mount; colour white |
| `bb-full-830` | Full-size breadboard, 830 tie-points | v1 | 165 × 55 × 9 mm, ≈ 85 g [VERIFY] | 63 columns; rails 50 holes each, **split in the middle** (`railSplit: true`) with the printed gap | Rail split is the classic trap; X-ray view shows it |
| `bb-mini-170` | Mini breadboard, 170 tie-points | v1.x | 47 × 35 mm | 17 columns, no rails | For compact N20 rovers |
| `wire-jumper-mm` | Dupont jumper M-M (flexible length; 10 colours) | MVP | 26 AWG [VERIFY]; 0 Ω | `Wire`; length stretches to fit ([03 §6.1](03-game-design.md)); export rounds up to 10/20/30 cm | Breadboard ↔ breadboard/board headers |
| `wire-jumper-mf` | Dupont jumper M-F | MVP | same | `Wire` | Module header pins ↔ breadboard/board sockets |
| `wire-jumper-ff` | Dupont jumper F-F | MVP | same | `Wire` | Module header pins ↔ module header pins (e.g., HC-SR04 to a sensor shield); with a male header strip as an adapter into breadboards |
| `wire-solid-22awg` | Solid-core hookup wire, cut to length | v1 | 22 AWG, 0.05 Ω/m [VERIFY] | `Wire` | Tidy breadboards; flat routing |
| `wire-motor-lead` | Motor leads with bare ends | MVP | 2 × 15 cm | `Wire` | For screw terminals |
| `conn-screw-terminal-2` | 2-way screw terminal block (5.08 mm) | v1 | 10 × 8 mm | Terminal | Standalone terminal for battery leads |
| `cable-usb-ab` | USB A-B cable (Uno/Mega) | MVP | 1 m | `UsbSource` 5 V / 0.5 A through the board fuse ([06 §2.4](06-electrical-simulation-spec.md)) | Detachable at the board; provides the Serial Monitor link |
| `cable-usb-mini` | USB Mini-B / Micro-B cable (Nano) | v1 | 1 m | same | — |
| `conn-dc-jack-adapter` | DC barrel jack (5.5 × 2.1 mm) to screw terminals | v1 | 15 × 10 mm | Terminal | Connect battery packs to the Uno jack |
| `conn-jst-xh2` | JST-XH 2-pin lead | v1.x | — | Terminal | LiPo balance/main leads |

## 4. Passives and basics

| id | Part | Tier | Dims / mass | Pins | Key parameters | Models | Failure |
|---|---|---|---|---|---|---|---|
| `r-220` … `r-100k` | Resistor 1/4 W: 220, 330, 1 k, 4.7 k, 10 k, 100 k Ω (6 entries; one JSON with a `value` parameter) | MVP (220, 10 k) / v1 (rest) | body 6.3 × 2.4 mm, leads 2.54 mm-friendly, 0.3 g | 2 `passive` | ±5 %; 0.25 W; colour bands rendered from the value (4-band) | `Resistor` | F25 (> 0.5 W) |
| `c-100n` | Ceramic capacitor 100 nF | v1 | 5 × 3 mm | 2 `passive` | 50 V, no polarity | `Capacitor` | — |
| `c-10u`, `c-100u`, `c-470u` | Electrolytic 10 / 100 / 470 µF | v1 | Ø 5–8 × 11 mm | 2 `passive` (marked −) | 16–25 V; polarised | `Capacitor` | F24 (reverse/over-voltage) |
| `d-1n4007` | Rectifier diode 1N4007 | v1 | DO-41 5.2 × 2.7 mm | 2 `passive` | Vf 0.7 V (1.1 V at 1 A), 1 A, 1000 V | `Diode` | thermal |
| `d-1n4148` | Signal diode 1N4148 | v1 | DO-35 | 2 | Vf 0.7 V, 200 mA | `Diode` | thermal |
| `led-5mm-red` / `-green` / `-yellow` / `-blue` / `-white` | 5 mm LED (5 entries) | MVP (red) / v1 | Ø 5 × 8.6 mm; leads 25 mm (anode longer); 0.3 g | 2 `passive` (A, K) | Vf 20 mA: red 1.9 V, yellow 2.1 V, green 2.2 V (GaP) / 3.2 V (InGaN, `variant`), blue 3.2 V, white 3.3 V; If nominal 20 mA, max 30 mA; reverse max 5 V ([R4 §2](research/R4-electrical-physics-facts.md)) | `Led` | F1–F3 |
| `led-module-5mm` | LED module, 5 mm red LED with its 220 Ω resistor on a 20 × 14 mm board (in the prototype since 2026-09-24) | MVP | 20 × 14 × 14 mm, 2 g | 2 pins: `S` (signal), `−` (GND) | lights at about 13 mA from a 5 V pin through the resistor | `Led` behind a fixed resistor | F1 |
| `led-rgb-cc` | RGB LED 5 mm common cathode | v1 | Ø 5 mm, 4 leads | 4 (R, K, G, B) | Vf R 2.0 / G 3.2 / B 3.2 V; 20 mA each | 3 × `Led` | F1 |
| `btn-tactile-6mm` | Tactile push button 6 × 6 × 5 mm (4 legs) | MVP | 6 × 6 × 5 mm; legs 2 pairs at 6.5 mm | 4 `passive` (pairs internally joined) | 50 mA; 5 ms bounce | `PushButton` | — |
| `sw-slide-2p3t` | Slide switch (SS12D00) | v1 | 8.6 × 3.7 mm | 3 | 0.3 A | `Switch` | — |
| `pot-10k` | Potentiometer 10 kΩ linear (with knob) | v1 | Ø 16 mm body [VERIFY]; 3 legs | 3 (`passive`, wiper `analog_out`) | 10 kΩ ±20 %; 0.1 W | `Potentiometer` | wiper > 20 mA warning |
| `ldr-gl5528` | Photoresistor GL5528 | v1 | Ø 5 mm | 2 `passive` | 8–20 kΩ at 10 lux, ≥ 1 MΩ dark; γ 0.7 [VERIFY]; τ 45/55 ms ([R4 §2](research/R4-electrical-physics-facts.md)) | `Ldr` | — |
| `q-2n2222` | NPN transistor 2N2222 (TO-92) | v1 | 4.7 × 4.2 mm | 3 (E, B, C) | hFE ≈ 100 [VERIFY], Ic max 800 mA, Vce_sat 0.2 V | `NpnSwitch` | thermal |
| `relay-5v-1ch` | 5 V relay module, 1 channel | v1 | 50 × 26 × 18 mm, 15 g [VERIFY] | VCC, GND, IN (`digital_in`, active-low [VERIFY]); COM/NO/NC terminals | Coil 70 mA at 5 V [VERIFY]; contact 10 A/250 V | `RelayModule` | — |
| `buzzer-active` | Active buzzer 5 V | v1 | Ø 12 × 9 mm; 2 pins (+ longer) | 2 | 2.5 kHz; 25–36 mA [VERIFY] | `LogicDevice` (load) + `BuzzerActive` | — |
| `buzzer-passive` | Passive buzzer/piezo | v1 | Ø 12 mm | 2 | Needs `tone()`; 30 mA peak [VERIFY] | `BuzzerPassive` (audio from pin waveform) | — |
| `header-pins` | Male header strip (for wire adapters) | v1.x | 2.54 mm | — | — | — | — |

## 5. Power

| id | Part | Tier | Dims / mass | Terminals | Chemistry parameters ([06 §4.1](06-electrical-simulation-spec.md), [R4 §2](research/R4-electrical-physics-facts.md)) | Models | Failure |
|---|---|---|---|---|---|---|---|
| `bat-4aa-holder` | 4 × AA holder with switch and leads (2 × 2 layout) | MVP | 62 × 58 × 15 mm; 20 g + 4 × 23 g cells [VERIFY] | red/black leads (bare or barrel plug) | Alkaline: 6.2 V fresh (4 × 1.55), R_i 0.8 Ω fresh → 2.4 Ω depleted, ≈ 2.4 Ah at 100 mA / 1.2 Ah at 500 mA; NiMH option: 4.8 V, 0.15 Ω, 2.0 Ah | `Battery` ×4 in series + `Switch` | F19, F21 |
| `bat-2aa-holder` | 2 × AA holder | v1 | 58 × 32 × 15 mm | leads | 3.1 V | `Battery` | — |
| `bat-9v` + `bat-9v-snap` | 9 V alkaline block + snap lead | MVP | 48 × 26 × 17 mm, 45 g | snap | 9.4 V fresh; R_i 1.5 Ω (1–2 Ω [VERIFY]); ≈ 0.55 Ah at 25 mA, collapses above 0.5 A | `Battery` (6 cells) | F19 |
| `bat-2x18650-holder` | 2 × 18650 holder with leads | v1 | 78 × 40 × 20 mm; 15 g + 2 × 47 g [VERIFY] | leads | Li-ion 8.2 V full / 7.4 V nominal / 6.0 V cutoff; R_i 0.04 Ω; 2.5 Ah | `Battery` ×2 | F20, F21 |
| `bat-lipo-2s-1000` | LiPo 2S 1000 mAh 25C with JST-XH/T-plug | v1 | 70 × 35 × 15 mm, 60 g [VERIFY] | T-plug | 8.4 → 6.0 V; R_i 0.02 Ω; 25 A max | `Battery` | F20, F21 |
| `bat-usb-powerbank` | USB power bank 5 V 2 A | v1 | 90 × 25 × 25 mm, 100 g [VERIFY] | USB-A → cable | 5.05 V, 0.05 Ω, 2 A limit; auto-off below 50 mA after 30 s | `UsbSource` | F28 |
| `psu-dc-9v-1a` | 9 V 1 A wall adapter (barrel) | v1 | — | barrel 5.5/2.1 | 9.0 V, 0.2 Ω, 1 A limit | `Source` | — |
| `reg-lm2596` | LM2596 buck converter module | v1 | 43 × 21 × 14 mm, 12 g [VERIFY] | IN+/IN−, OUT+/OUT− screw terminals; trim pot | 4.5–40 V in; 1.25–37 V out; 2 A; η ≈ 85 % [VERIFY] | `Regulator` (buck) | thermal |
| `sw-rocker` | Rocker/slide power switch | v1.x | 19 × 13 mm | 2 | 3 A | `Switch` | — |

## 6. Motors and drivers

| id | Part | Tier | Dims / mass | Pins / terminals | Datasheet values | Model parameters ([07 §4.1](07-physics-world-sensors-spec.md) derivation) | Mounts | Failure |
|---|---|---|---|---|---|---|---|---|
| `motor-tt-1-48` | TT gear motor 1:48, 3–6 V ("yellow motor"), with leads | MVP | 70 × 22 × 18 mm (body + gearbox), 30.6 g; double 5.4 mm D-shaft with 3.6 mm flats [VERIFY]; 2 × M3 holes 17.5 mm apart [VERIFY] | 2 `motor` | 90 rpm at 3 V / 200 rpm at 6 V (measured 250); no-load 150 mA; stall 1.1–1.5 A; stall torque 0.4 / 0.8 kg·cm ([R4 §2](research/R4-electrical-physics-facts.md), Adafruit 3777) | R 4.0 Ω, L 1 mH [VERIFY], Ke 4.26 mV·s/rad, N 48, η 0.26 datasheet-fit (measured set TBD), J_wheel-ref 4.6 kg·cm² [VERIFY] | `tt_motor_bracket` | F18 |
| `motor-tt-1-48-enc` | TT motor with 20-slot encoder disc | v1 | + disc Ø 24 mm on the rear shaft | 2 `motor` + disc | as above | + `Encoder` behaviour, 20 pulses/rev | same | — |
| `motor-n20-100` | N20 micro metal gearmotor 6 V 100:1 (≈ 310 rpm) | v1 | 12 × 10 × 24 mm (+ 9 mm gearbox), 9.5 g; 3 mm D-shaft | 2 solder tabs | 310 rpm, 2.4 kg·cm stall, 1.6 A stall (Pololu HP); free-run 100–120 mA ([R4 §2](research/R4-electrical-physics-facts.md)) | R 3.75 Ω, Ke 1.72 mV·s/rad, N 100, η 0.87, L 0.5 mH [VERIFY] | `n20_bracket` | F18 |
| `motor-n20-30` / `-300` | N20 30:1 (1000 rpm) / 300:1 (100 rpm) variants | v1.x | same | — | Pololu table | scaled | same | — |
| `servo-sg90` | SG90 micro servo 9 g (in the prototype since 2026-09-24: its horn follows the pulses on its signal pin, 544–2400 µs for 0–180°, Arduino's Servo range) | v1 | 22.2 × 11.8 × 31 mm (32.2 with tabs), 9 g; 2 × Ø 2 mm holes 28 mm apart [VERIFY] | 3-pin lead: brown GND, red VCC (`power_in`), orange `signal` | 4.8–6 V; 1.8 kg·cm; 0.1 s/60°; 500–2400 µs; idle 10 mA, moving 100–250 mA, stall 650–700 mA [VERIFY] ([R4 §2](research/R4-electrical-physics-facts.md)) | `ServoElectrical` + `Servo` (rate 600 °/s, T 0.177 N·m) | `servo_horn_sg90`, `screw_m2` | F17 |
| `servo-mg996r` | MG996R standard servo | v1 | 40.7 × 19.7 × 42.9 mm, 55 g; 4 × Ø 4.5 mm holes at 49.5 × 10 mm [VERIFY] | 3-pin | 4.8–7.2 V; 9.4 / 11 kg·cm; 0.17 / 0.14 s/60°; run 0.5–0.9 A, stall 2.5 A ([R4 §2](research/R4-electrical-physics-facts.md)) | `Servo` (353 °/s, 0.92 N·m) | `screw_m3` | F17, F10 (USB fuse) |
| `stepper-28byj48` + `drv-uln2003` | 28BYJ-48 5 V stepper + ULN2003 driver board | v1 | motor Ø 28 × 19 mm, 30 g; board 35 × 32 mm | 5-pin JST to the board; board IN1–IN4 (`digital_in`), VCC, GND | 63.68:1; 2038 full / 4076 half steps per rev; coil ≈ 50 Ω → 100 mA per coil [VERIFY] ([R4 §2](research/R4-electrical-physics-facts.md)) | `Stepper` (34 mN·m [VERIFY], max ≈ 1000 half-steps/s [VERIFY]) | `screw_m3` ×2 (35 mm spacing) | thermal |
| `drv-l298n-module` | L298N dual H-bridge module (red board, heatsink) | MVP | 43 × 43 × 27 mm, 26 g; 4 × Ø 3 mm holes on a 37 × 37 mm square [VERIFY] | Terminals: OUT1, OUT2, +12V (Vs), GND, +5V, OUT3, OUT4; header: ENA, IN1, IN2, IN3, IN4, ENB; jumpers ENA/ENB (fitted), 5V-EN (fitted) | Vs 5–35 V; logic 4.5–7 V; 2 A per channel; drop 1.8 V typ at 1 A (up to 4.9 V at 2 A); quiescent 13–50 mA; 78M05 0.5 A ([R4 §2](research/R4-electrical-physics-facts.md)) | `HBridgeL298` (source 0.95 + 0.4·I, sink 0.8 + 0.4·I [VERIFY fit]; Rth 35 °C/W; Tj 130 °C) + `Regulator78M05` | `standoff_m3` ×4, `tape_face` | F14–F16 |
| `drv-l293d-dip` | L293D DIP-16 | v1.x | 19.3 × 6.4 mm | 16 pins (breadboard) | 600 mA per channel; drop ≈ 1.4 V per leg [VERIFY] | `HBridgeL293` | `breadboard` | thermal |
| `drv-tb6612fng` | TB6612FNG breakout | v1 | 20 × 20 mm, 2 g | VM, VCC, GND, STBY, AIN1/2, PWMA, BIN1/2, PWMB, A01/A02/B01/B02 | 4.5–13.5 V motor; 1.2 A cont / 3.2 A peak; R_on ≈ 0.5 Ω [VERIFY] | `HBridgeMosfet` | `breadboard`, `tape_face` | over-current shutdown |
| `drv-drv8833` | DRV8833 breakout | v1.x | 20 × 15 mm | similar | 2.7–10.8 V; 1.5 A | `HBridgeMosfet` | `breadboard`, `tape_face` | — |

## 7. Sensors

| id | Part | Tier | Dims / mass | Pins | Supply / current | Key parameters | Models ([07 §5](07-physics-world-sensors-spec.md)) | Mounts | Failure |
|---|---|---|---|---|---|---|---|---|---|
| `sens-hc-sr04` | HC-SR04 ultrasonic | MVP | 45 × 20 × 15 mm, 8.5 g; transducers Ø 16 mm, 26 mm apart [VERIFY] | VCC, Trig (`digital_in`), Echo (`digital_out`), GND; 2.54 mm header | 5 V; 15 mA active, < 2 mA idle | 2–400 cm; 15°; 10 µs trigger; 58.3 µs/cm; 38 ms timeout; ≥ 60 ms cycle ([R4 §2](research/R4-electrical-physics-facts.md)) | `LogicDevice` + `Ultrasonic` (17 rays) | `hc_sr04_bracket` (the common bent-acrylic bracket), `tape_face` | F26 |
| `sens-ir-obstacle` | IR obstacle avoidance module (LM393, 2 LEDs) | v1 | 45 × 12 × 10 mm, 3 g [VERIFY] | VCC, GND, OUT (`digital_out`, active-low); pot | 3.3–5 V; 20 mA | 2–30 cm adjustable; ≈ 35°; sunlight-sensitive ([R4 §2](research/R4-electrical-physics-facts.md)) | `LogicDevice` + `IrObstacle` | `screw_m3` ×1 (Ø 3 mm hole), `tape_face` | — |
| `sens-tcrt5000-module` | TCRT5000 line sensor module (analog + digital out) | v1 | 32 × 14 mm [VERIFY] | VCC, GND, D0, A0; pot | 5 V; 20 mA (emitter) | 0.2–15 mm, optimum 2.5 mm ([R4 §2](research/R4-electrical-physics-facts.md)); R_L 10 kΩ | `LogicDevice` + `LineSensorTcrt5000` (polarity param) | `screw_m3`, `tape_face` | — |
| `sens-line-bar-5` | 5-channel TCRT5000 line-follower bar | v1 | 65 × 20 × 12 mm [VERIFY]; sensor pitch 10 mm [VERIFY] | VCC, GND, OUT1–OUT5 (analog) (+ digital variants) | 5 V; 100 mA | as above ×5 | 5 × `LineSensorTcrt5000` | `screw_m3` ×2 | — |
| `sens-gy521-mpu6050` | GY-521 MPU-6050 module | v1 | 21 × 16 × 3 mm, 2 g; 2 × Ø 3 mm holes 15 mm apart [VERIFY] | VCC, GND, SCL, SDA (`open_drain`), XDA, XCL, AD0, INT | 3.3–5 V (on-board LDO); 4 mA | 0x68/0x69; ±2–16 g; ±250–2000 °/s; 400 kHz I2C; module has 4.7 kΩ pull-ups [VERIFY] ([R4 §2](research/R4-electrical-physics-facts.md)) | `I2cDevice` + `Imu6050` (no DMP) | `screw_m3`, `tape_face` | — |
| `sens-dht11` | DHT11 module (3-pin) | v1 | 28 × 12 × 8 mm [VERIFY] | VCC, DATA (`open_drain` one-wire), GND | 3–5.5 V; 2.5 mA | ±2 °C, ±5 % RH; ≤ 1 Hz ([R4 §2](research/R4-electrical-physics-facts.md)) | `LogicDevice` + `Dht` | `tape_face` | — |
| `sens-dht22` | DHT22 / AM2302 | v1.x | 25 × 15 × 8 mm | 3 | 3.3–6 V | ±0.5 °C, ±2 %; ≤ 0.5 Hz | `Dht` | — | — |
| `sens-encoder-lm393` + `disc-20` | Optical speed sensor (LM393, slotted) + 20-slot disc | v1 | 32 × 14 mm; disc Ø 24 mm | VCC, GND, D0, A0 | 5 V; 15 mA | 20 pulses/rev; 5 mm slot width | `LogicDevice` + `Encoder` | `screw_m3` | — |
| `sens-ky040` | KY-040 rotary encoder module | v1 | 26 × 19 mm | CLK, DT, SW, +, GND | 5 V | 20 detents; 2 ms bounce | `RotaryEncoder` | `tape_face` | — |
| `sens-tilt-sw520d` | Tilt (ball) switch module | v1 | 20 × 10 mm | S, +, − | 5 V | closes beyond ≈ 30° tilt [VERIFY] | `TiltSwitch` | `tape_face` | — |
| `sens-ky038-sound` | KY-038 sound sensor | v1.x | 38 × 15 mm | A0, G, +, D0 | 5 V; 5 mA | pot threshold | `SoundSensor` | `tape_face` | — |
| `sens-microswitch-lever` | Micro limit switch with lever (bumper) | v1 | 20 × 10 × 6 mm; lever 25 mm | COM, NO, NC | — | 0.5 N actuation [VERIFY] | `BumpSwitch` | `screw_m2` ×2 | — |
| `sens-ir-receiver-vs1838b` + `remote-nec-21` | VS1838B IR receiver + 21-key NEC remote | v1 | receiver 7 × 6 mm 3-leg; remote 86 × 40 mm | OUT, GND, VCC | 2.7–5.5 V; 1.5 mA | 38 kHz; NEC; key codes [VERIFY map] | `LogicDevice` + `IrReceiver` | breadboard / `tape_face` | — |
| `sens-hc05` | HC-05 Bluetooth module (ZS-040 breakout) | v1 | 37 × 16 × 8 mm, 5 g [VERIFY] | VCC, GND, TXD, RXD, STATE, EN | 3.6–6 V; 30–40 mA pairing, 8 mA connected [VERIFY]; RXD is 3.3 V logic (divider recommended) | 9600 default; AT 38400; PIN 1234 ([R4 §2](research/R4-electrical-physics-facts.md)) | `LogicDevice` + `Bluetooth` (virtual phone) | `tape_face` | — |
| `sens-soil-moisture` | Capacitive soil moisture sensor | v1.x | 98 × 23 mm | VCC, GND, AOUT | 3.3–5 V | — | `AnalogSensor` | — | — |
| `sens-tcs3200` | TCS3200 colour sensor module | v1.x | 32 × 32 mm | S0–S3, OUT, OE, VCC, GND | 5 V | frequency output | `ColourSensor` | — | — |
| `sens-bmp280` | BMP280 pressure/temperature | v1.x | 15 × 12 mm | I2C 0x76/0x77 | 3.3 V | — | `I2cDevice` | — | — |
| `sens-mq2` | MQ-2 gas sensor | v1.x | 32 × 20 mm | A0, D0, VCC, GND | 5 V; 150 mA heater | — | `AnalogSensor` | — | — |

## 8. Displays and output

| id | Part | Tier | Dims / mass | Pins / interface | Supply / current | Key parameters | Models | Mounts | Failure |
|---|---|---|---|---|---|---|---|---|---|
| `disp-lcd1602-i2c` | 16×2 character LCD (HD44780) with PCF8574 I2C backpack | v1 | 80 × 36 × 12 mm, 35 g; 4 × Ø 2.5 mm holes at 75 × 31 mm [VERIFY] | GND, VCC, SDA, SCL | 5 V; 20–30 mA (backlight) + 1 mA [VERIFY] | address 0x27 (PCF8574) / 0x3F (PCF8574A); contrast pot; `LiquidCrystal_I2C` ([R4 §2](research/R4-electrical-physics-facts.md)) | `I2cDevice` + `Hd44780` (4-bit via PCF8574 nibble mapping, standard backpack wiring) | `standoff_m2_5` ×4 | — |
| `disp-ssd1306-096` | 0.96" 128×64 OLED (SSD1306, I2C) | v1 | 27 × 27 × 4 mm, 3 g; 4 × Ø 2 mm holes 23.5 × 23.5 mm [VERIFY] | GND, VCC, SCL, SDA | 3.3–5 V; 8–20 mA [VERIFY] | 0x3C (0x3D option); `Adafruit_SSD1306` / U8g2 | `I2cDevice` + `Ssd1306` (full command set subset: display on/off, addressing modes, page/column, contrast, GDDRAM) | `standoff_m2` | — |
| `disp-tm1637` | TM1637 4-digit 7-segment module | v1 | 42 × 24 × 12 mm, 8 g [VERIFY] | CLK, DIO (2-wire, not I2C), VCC, GND | 3.3–5 V; 20–80 mA [VERIFY] | `TM1637Display` library protocol (bit-banged at cycle resolution) | `LogicDevice` + `Tm1637` | `screw_m2` | — |
| `led-ws2812b-stick-8` | WS2812B 8-LED stick | v1 | 51 × 10 × 3 mm | DIN, DOUT, 5V, GND | 3.5–5.3 V; up to 8 × 60 mA = 0.48 A ([R4 §2](research/R4-electrical-physics-facts.md)) | 800 kbps; 0.4/0.85 µs ±150 ns; GRB | `LogicDevice` + `Ws2812` (cycle-level decode) | `tape_face` | F10 (USB fuse when powered from 5V pin), F26 |
| `led-ws2812b-ring-12` | WS2812B 12-LED ring | v1 | Ø 37 mm | same | up to 0.72 A | same | same | `tape_face` | same |
| `ic-74hc595` | 74HC595 shift register DIP-16 | v1 | 19.3 × 6.4 mm | DS, SHCP, STCP, OE, MR, Q0–Q7, Q7S, VCC, GND | 2–6 V; 70 mA total output | SPI-compatible (`shiftOut`) | `LogicDevice` + `ShiftRegister595` (cycle-level) | breadboard | pin over-current |
| `disp-max7219-8x8` | MAX7219 8×8 LED matrix | v1.x | 32 × 32 mm | DIN, CS, CLK, VCC, GND | 5 V; up to 330 mA | SPI-like | `Max7219` | — | — |
| `asm-pan-tilt-sg90` | Pan-tilt bracket kit with 2 × SG90 | v1 | 60 × 40 × 60 mm assembled, 30 g with servos [VERIFY] | two servo leads | as SG90 ×2 | two chained servo joints | 2 × `Servo` + bracket parts | `screw_m2` base | F17 |
| `asm-gripper-sg90` | Two-finger gripper kit with 1 × SG90 | v1 | 85 × 60 mm open [VERIFY] | one servo lead | as SG90 | mirrored fingers; grip force ≈ 1 N [VERIFY] | `Servo` + gripper joints | `screw_m3` | F17 |

## 9. Mechanical

| id | Part | Tier | Dims / mass | Notes / physics | Mounts provided |
|---|---|---|---|---|---|
| `chassis-2wd-round` | 2WD round acrylic chassis kit plate | MVP | Ø 148 mm × 3 mm acrylic [VERIFY]; 45 g; pre-drilled for 2 TT motors, caster, Uno, battery holder | rigid part; density 1.18 g/cm³ | `tt_motor_bracket` ×2 (via brackets), `caster_ball`, `screw_m3` grid, `standoff_m3` (Uno pattern) |
| `chassis-2wd-rect` | 2WD rectangular chassis (double layer) | v1 | 200 × 100 mm [VERIFY]; 2 plates + 4 × M3 standoffs 30 mm | — | as above |
| `chassis-4wd` | 4WD chassis with 4 TT motors | v1 | 250 × 150 mm [VERIFY] | skid-steer kinematics | `tt_motor_bracket` ×4 |
| `chassis-n20-mini` | Mini chassis for 2 × N20 (3D-printable preset) | v1 | 80 × 60 mm | — | `n20_bracket` ×2, `caster_ball` |
| `wheel-65mm` | 65 × 26 mm rubber-tyre wheel for TT motors | MVP | Ø 65 × 26 mm, 30 g [VERIFY]; D-bore 5.4 mm | sphere collider r = 32.5 mm; tyre material rubber | fits `tt_motor` shaft |
| `wheel-n20-34` / `-43` | N20 wheels Ø 34 / 43 mm | v1 | 34 × 7 mm / 43 × 19 mm [VERIFY] | plastic/rubber | 3 mm D-bore |
| `caster-ball-20` | Ball caster (metal ball Ø 20 mm, plastic housing) | MVP | 30 × 30 × 25 mm, 15 g [VERIFY] | low-friction sphere on the root | `screw_m3` ×2 |
| `caster-wheel-swivel` | Swivel caster wheel | v1 | Ø 25 mm wheel, 35 mm tall | swivel joint approximated by low-friction sphere in v1 | `screw_m3` ×4 |
| `bracket-tt` | TT motor bracket (metal L-bracket) | MVP | 40 × 20 × 18 mm, 8 g | rigid | provides `tt_motor_bracket`; needs `screw_m3` ×2 on the chassis |
| `bracket-n20` | N20 motor bracket | v1 | 20 × 12 × 15 mm | rigid | `n20_bracket` |
| `bracket-hc-sr04` | HC-SR04 acrylic bracket | v1 | 45 × 25 × 20 mm | rigid | `hc_sr04_bracket`; needs `screw_m3` ×1 or servo horn |
| `bracket-sg90-set` | SG90 mounting brackets (U/side) | v1 | 30 × 20 mm | rigid | `servo_mount_sg90` |
| `standoff-m3-set` | M3 nylon standoffs 6/10/15/20/30 mm + screws + nuts | MVP | — | rigid link between `screw_m3` holes and boards | `standoff_m3` |
| `screws-m2-set` | M2 screws/nuts (servos, small modules) | v1 | — | — | `screw_m2` |
| `tape-double-sided` | Double-sided foam tape | MVP | 1 mm thick pads | attach any flat-based part to any face; holds up to 200 g at 1 g load [VERIFY as design rule] | `tape_face` |
| `zip-ties` | Cable ties | v1 | 100 × 2.5 mm | strap cylindrical parts (battery, motors) to plates | `zip_tie` |
| `plate-acrylic-blank` | Acrylic blanks 3 mm (100 × 100, 150 × 100) | v1.x | — | for Body Studio starts | — |
| `rubber-band` / `velcro` | Straps | v1.x | — | — | — |

## Appendix A — Footprints and hole patterns for the Body Studio helper

All coordinates in mm from the part's lower-left corner in its top view; hole diameters are the clearance size. **Every entry is [VERIFY]** against a physical part or the manufacturer drawing before the helper ships; values here are the working assumptions.

| Part | Outline | Holes (x, y) | Ø | Notes |
|---|---|---|---|---|
| Arduino Uno R3 | 68.6 × 53.4 | (14.0, 2.5), (15.3, 50.7), (66.1, 7.6), (66.1, 35.5) | 3.2 | Official Arduino drawing; USB/jack overhang 6 mm at x < 0 |
| Arduino Mega 2560 | 101.5 × 53.3 | Uno holes + (90.2, 50.7), (96.5, 2.5) | 3.2 | — |
| Arduino Nano | 45 × 18 | pin rows at y = 1.3 and 16.5, x from 2.5 step 2.54 (15 pins); 2 corner holes | 1.8 | Usually breadboard- or header-mounted |
| L298N module | 43 × 43 | (3, 3), (40, 3), (3, 40), (40, 40) | 3.0 | Heatsink 23 mm tall on the +12V side |
| HC-SR04 | 45 × 20 | (2.5, 2.5), (42.5, 2.5) | 1.8 | Rarely screwed; use the bracket |
| HC-SR04 bracket | 45 × 25 base | (22.5, 12.5) centre | 3.0 | Or servo-horn holes 2 × 1.5 mm at 14 mm |
| TT motor | 70 × 22 (side) | 2 × M3 through-holes 17.5 mm apart, 9 mm from the gearbox end | 3.0 | Mounted with 2 × M3 × 30 through the chassis; shaft axis 11 mm above the mounting face |
| TT bracket | 40 × 20 | (10, 10), (30, 10) on the chassis face | 3.0 | — |
| N20 bracket | 20 × 12 | (4, 6), (16, 6) | 2.0 | — |
| SG90 | 22.2 × 11.8 (body), 32.2 with tabs | (2.0, 5.9), (30.2, 5.9) on the tab line | 2.0 | Horn axis 6 mm from one end |
| MG996R | 40.7 × 19.7 | (−4.4, 5), (−4.4, 15), (45.1, 5), (45.1, 15) relative to the body | 4.5 | Tabs extend beyond the body |
| 4×AA holder (2×2) | 62 × 58 | (31, 8), (31, 50) | 3.0 | Or tape/zip tie |
| 2×18650 holder | 78 × 40 | (10, 20), (68, 20) | 3.0 | — |
| 9 V battery | 48 × 26 | none | — | Tape/zip tie/holder clip |
| Ball caster | 30 × 30 | (5, 15), (25, 15) | 3.0 | Height 25 mm; choose so the chassis is level with 65 mm wheels on TT motors |
| 16×2 LCD | 80 × 36 | (2.5, 2.5), (77.5, 2.5), (2.5, 33.5), (77.5, 33.5) | 2.5 | Backpack adds 10 mm below |
| SSD1306 0.96" | 27 × 27 | (1.75, 1.75), (25.25, 1.75), (1.75, 25.25), (25.25, 25.25) | 2.0 | — |
| GY-521 | 21 × 16 | (3, 8), (18, 8) | 3.0 | — |
| TCRT5000 5-bar | 65 × 20 | (5, 10), (60, 10) | 3.0 | Sensors on the underside, 10 mm pitch |
| IR obstacle module | 45 × 12 | (40, 6) | 3.0 | — |
| 28BYJ-48 | Ø 28 body, 35 mm tab-to-tab | (0, 0), (35, 0) tab centres | 4.2 | Shaft off-centre by 8 mm |
| Half breadboard | 82 × 55 | none | — | Adhesive back |
| Chassis 2WD round | Ø 148 | grid of 3 mm holes on a 10 mm pitch plus the Uno pattern | 3.0 | Kit variants differ |

The helper drops the hole set and, where relevant, the part's outline as a translucent ghost so the user can align it.

## Appendix B — 3D asset production notes

**The prototype's part models (2026-09-24).** The owner asked for "ultra realistic renders and models of components". The prototype makes every catalogue part in code, from real dimensions, without downloaded models (`app/Assets/Spike/Scripts/Parts/`):

- **Geometry**: `MeshKit` builds each part in millimetres from rounded boxes, turned profiles (capacitors, transducers, the LED dome, the wheel), extruded outlines (boards, the servo horn) and swept tubes (leads, springs), one submesh per material. Each part is built once per session and shared by every robot.
- **Boards and markings**: `Raster` paints the textures on the CPU: copper under the solder mask with the gaps round each trace, tin pads, vias, holes, white silkscreen in a PCB stroke font, and the chips' laser markings, each with its colour, metal, smoothness and height; the height becomes the normal map, so traces stand up and holes sink. The Uno is teal, the L298N red, the HC-SR04 blue, the LED module black.
- **What is on them**, where the real boards have it:
  - Uno R3: USB-B and barrel jacks, the ATmega328P in its socket, the 16U2, the 16 MHz crystal, the regulator, two electrolytics, the reset button, both ICSP headers and the small SMD parts. Its four LEDs light: ON, L (D13), TX and RX.
  - L298N: the finned heatsink with the chip screwed to it, the terminals, the ENA/ENB jumpers (gone when a wire takes the pin), the 78M05, the 220 µF capacitors, the diodes and the PWR LED.
  - HC-SR04 on its stand; the TT motor with its FA-130 can and its treaded wheel; the 4×AA holder with printed cells and springs; the ball caster on brass spacers; the SG90 as its three glossy blue mouldings with the screw holes and slits cut through its tabs, its lead and socket; the LED module.
- **Solid blocks** (`PartDef.Solids`, 2026-09-25): what jumper wires go round. A part is its bounding box, except the L298N, whose board, heatsink with the chip, terminals, logic header and capacitors are separate blocks, so a jumper can come down to the header beside the tall heatsink. A motor's wheel is a 65 mm cylinder on its shaft.
- **Pins** are where the catalogue has them: a jumper lands on the pin the player sees. Two layouts follow the real boards since 2026-09-24:
  - the L298N's logic header is at the front right, the power terminal at the front left, and a motor terminal on each side of the heatsink;
  - the HC-SR04's pins read VCC, TRIG, ECHO, GND from left to right, seen from the front.
- **No manufacturer logos**: only part numbers and pin labels, as below.
- `-partShots <folder>` photographs every part close up from three sides in the Garage's light, to judge them.

- **Budgets**: 2–8 k triangles per part (boards 8 k, jumper wires procedural, resistors 300); one 1 k or 2 k texture set (albedo, normal, metallic/roughness/AO packed) per part; boards use 2 k so the silkscreen stays readable when zoomed to 1080p.
- **Pin sockets**: every pin in the pin table needs a named empty (`pin_<name>`) at the exact socket/leg position in the mesh; the wiring tool snaps to these. Header sockets at 2.54 mm pitch must align with the breadboard grid to 0.05 mm.
- **Orientation and origin**: origin at the part's mounting face centre, +Z up, +X along the long axis as in the datasheet drawing; units metres in the engine (1 mm = 0.001).
- **Colliders**: authored as primitives in Blender (boxes/cylinders named `col_*`) — no mesh colliders for parts.
- **Damage states**: a second material slot with the "burnt" look; smoke emitter empty `fx_smoke` at the hottest component (regulator, L298 chip, LED die).
- **LOD**: none needed (few hundred parts on screen at most); use GPU instancing for jumper wire ends and resistors.
- **Naming**: `<id>.glb` with `<id>_icon.png` (512²) and `<id>_datasheet.md`.
- **No manufacturer logos**; silkscreen text uses generic layouts reproducing pin labels and part numbers only ([12 §4](12-business-steam-legal.md)).

Prioritised asset list for the MVP (Phase 1): Uno R3 · half breadboard · jumper M-M/M-F/F-F · resistor (parametric colour bands) · 5 mm LED · tactile button · 4×AA holder · 9 V battery + snap · L298N module · TT motor · 65 mm wheel · ball caster · TT bracket · 2WD round chassis · HC-SR04 · USB cable · standoffs/screws · tape pads · wire ends (Dupont housings). 19 assets.

## Appendix C — Paid packs (premium catalogue plan)

All pack content follows the same rules as the free catalogue: real products, real datasheet values, the same electrical and behaviour models ([ADR-0007](adr/ADR-0007-monetization-free-to-play-dlc.md), [12 §1](12-business-steam-legal.md)). Pack line-up and prices are decided in [13 D7/D15](13-open-questions-and-risks.md); candidate parts below are **[VERIFY]** and need datasheet research before they are modelled.

| Pack | Contents | Engineering needed | Target |
|---|---|---|---|
| **Mega 2560 Pack** | Arduino Mega 2560 R3 (§2.3); Mega sensor shield [VERIFY]; datasheet cards for both | ATmega2560 device profile: timers 3/4/5, USART1–3, ports A–L, INT2–7, 3-byte PC ([05 §2](05-arduino-emulation-spec.md)); ≈ 10 days | Launch |
| **Advanced Sensors Pack** | VL53L0X laser time-of-flight distance sensor (I2C), TCS34725 colour sensor (I2C), BNO055 or ICM-20948 9-axis IMU, GPS module (NMEA over UART) | I2C device models + world queries (laser ray, colour sampling) | Post-launch |
| **Precision Motion Pack** | JGB37-520 metal-gear motors with Hall encoders, A4988 driver + NEMA 17 stepper, PCA9685 16-channel servo driver, MG90S metal-gear micro servo | Motor/stepper parameter sets; PCA9685 I2C model | Post-launch |
| **Display Pack** | 1.8" ST7735 colour TFT (SPI), MAX7219 8×8 matrix chain, 20×4 I2C LCD | SPI display models; frame-buffer rendering | Post-launch |
| **ESP32 Pack** | ESP32 DevKit (Wi-Fi/Bluetooth) | New CPU core (Xtensa LX6 dual-core, or start with the RISC-V ESP32-C3) plus toolchain `esp32:esp32`; 2–4 months [estimate] | Post-launch |
| **Uno R4 Pack** | Uno R4 Minima / WiFi (Renesas RA4M1, Cortex-M4 48 MHz) | Cortex-M4 core + RA4M1 peripherals + `arduino:renesas_uno` toolchain; 2–4 months [estimate] | Post-launch |
| **Pico Pack** | Raspberry Pi Pico (RP2040) | Cortex-M0+ dual core + PIO; the MIT-licensed rp2040js is a design reference ([R1 §1](research/R1-avr-emulation-and-toolchain-facts.md)); 2–3 months [estimate] | Post-launch |
| **Customization packs** | Body finishes (carbon fibre, brushed aluminium, wood, anodised colours, tinted acrylic), decal and number sets, board skins (black PCB, transparent PCB, gold-plated headers), wire sets (neon, braided sleeve), workbench/mat/arena themes | Finish system ([08 §2](08-body-designer-spec.md)); art only per pack | Launch: 2 packs |
| **Supporter bundle** | All current packs at a discount; supporter badge in the Workshop | — | Launch |

Board skins change only the look: a "black Uno" behaves exactly like the free Uno.
