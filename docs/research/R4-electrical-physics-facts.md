# R4 — Electrical and physics simulation: fact sheet

Researched 2026-09-22. [?] = derived, blog-grade, or not re-verified against a primary source. Decisions live in [06-electrical-simulation-spec.md](../06-electrical-simulation-spec.md), [07-physics-world-sensors-spec.md](../07-physics-world-sensors-spec.md) and [09-components-catalog.md](../09-components-catalog.md).

## 1. How existing simulators work
- **Wokwi** = instruction-level AVR emulation (avr8js) with a cycle counter; peripherals attach via register hooks (event-driven, behavioural). No electrical solver — https://blog.wokwi.com/avr8js-simulate-arduino-in-javascript/. "Wokwi is a digital simulator with basic analog support"; ADC reference fixed at 5 V — https://docs.wokwi.com/chips-api/analog. "You won't be able to use resistors together with analog components… still use resistors as pull-up/pull-down" — https://docs.wokwi.com/parts/wokwi-resistor; open issue: resistors don't affect voltage — https://github.com/wokwi/wokwi-features/issues/791. LED brightness = gamma-corrected PWM duty; no burnout — https://docs.wokwi.com/parts/wokwi-led.
- **Tinkercad Circuits**: engine undisclosed [?]; models LED overcurrent ("If your LED is blown… resistor value is too low"; explosion tooltip shows amps) — https://mjvo.github.io/tutorials/circuits/tinkercad/; MCU pins are not damaged ("in this simulation you cannot do any damage") — https://www.elektormagazine.com/articles/simulate-circuits-online-circuit-simulation-made-simple; help article on "voltage bleed when the switch is off" implies a numerical solver with leakage conductance [?] — https://www.tinkercad.com/help/circuits/voltage-bleed-when-the-switch-is-off
- **CRUMB**: "Using SPICE for the actual simulation work" — https://freelance.halfacree.co.uk/tag/crumb-circuit-simulator/; v1.3 claims simulation "over 20,000 % faster" — https://80.lv/articles/build-run-your-own-cpu-with-crumb-1-3
- **Falstad CircuitJS1**: GPL-2.0-or-later (cannot be embedded in closed source) — https://www.falstad.com/circuit/about.html. Method: Modified Nodal Analysis, LU factorisation (once for linear circuits), Newton–Raphson for diodes/transistors, fixed timestep, backward-Euler/trapezoidal — https://github.com/SEVA77/circuitjs1-webapp/blob/master/INTERNALS.md
- **ngspice** v47 (Aug 2026): core under modified 3-clause BSD; XSPICE public domain (except one GPLv2 table model); tclspice/KLU/numparam LGPLv2; OSDI MPL-2.0; Sparse MIT — https://sourceforge.net/p/ngspice/ngspice/ci/master/tree/COPYING · https://ngspice.sourceforge.io/faq.html. Shared-lib API: background thread, read data per time point, halt/alter/resume, external V/I source callbacks, multiple instances — https://ngspice.sourceforge.io/shared.html. No documented real-time guarantee (adaptive timestep) [?].

## 2. Component parameters
### L298 (ST datasheet https://hades.mech.northwestern.edu/images/a/ad/L298N.pdf)
- Vs = (V_IH + 2.5) to 46 V; Vss 4.5–7 V; Io 2 A DC / 2.5 A repetitive / 3 A for 100 µs; Ptot 25 W at Tcase 75 °C; Tj −25…130 °C, thermal shutdown; Rth j-c 3 °C/W, j-amb 35 °C/W.
- VCEsat source at 1 A 0.95/1.35/1.7 V (min/typ/max), at 2 A 2.0/2.7; sink at 1 A 0.85/1.2/1.6, at 2 A 1.7/2.3; **total drop at 1 A: 1.8 V typ / 3.2 V max; at 2 A up to 4.9 V.**
- Quiescent Is (Ven = H): Vi = L 13/22 mA, Vi = H 50/70 mA; Ven = L 4 mA. V_IL ≤ 1.5 V, V_IH ≥ 2.3 V; commutation ≤ 25–40 kHz.
- L298N module: 78M05 on board; remove the 5V-EN jumper if Vs > 12 V; with the jumper, 5 V out ≤ 0.5 A; ENA/ENB jumpered high by default; IN1 = IN2 → off/brake; ≈ 2 V drop (12 V in → ≈ 10 V to motor) — https://lastminuteengineers.com/l298n-dc-stepper-driver-arduino-tutorial/ · https://forum.arduino.cc/t/l298n-why-disable-onboard-5v-regulator-78m05-for-vcc-12v/1005987

### TT gear motor 1:48 (Adafruit 3777 https://www.adafruit.com/product/3777, PDF https://media.digikey.com/pdf/Data%20Sheets/Adafruit%20PDFs/3777_Web.pdf)
- 3–6 V; no-load 90 rpm at 3 V, 200 rpm at 6 V (±10 %); no-load current 150 mA ±10 %; stall torque 0.4 kg·cm at 3 V, 0.8 kg·cm at 6 V; 30.6 g; 70×22×18 mm.
- Measured: 3 V 150 mA/120 rpm, stall 1.1 A; 4.5 V 155 mA/185 rpm, stall 1.2 A; 6 V 160 mA/250 rpm, stall 1.5 A.
- Derived [?]: R ≈ 6 V / 1.5 A ≈ 4 Ω (clones quoting 0.8 A stall ⇒ ≈ 7.5 Ω); Ke ≈ (6 − 0.16·4)/(250 rpm × 48 → 1257 rad/s motor-side) ≈ 4.3 mV·s/rad.

### N20 micro metal gearmotor (Pololu HP 6 V https://www.pololu.com/category/60/micro-metal-gearmotors)
- 30:1 1000 rpm / 0.57 kg·cm; 50:1 590 / 0.86; 100:1 310 / 2.4; 150:1 210 / 3.0 kg·cm stall; free-run 100–120 mA [?]; stall 1.6 A. Clone (60 rpm): no-load 40 mA, stall 0.67 A, 2.6 kg·cm — https://sharvielectronics.com/product/n20-6v-60-rpm-micro-metal-gear-motor/

### SG90 (https://towerpro.com.tw/product/sg90-7/, https://www.auselectronicsdirect.com.au/assets/brochures/TA0132.pdf)
- 9 g; 1.8 kg·cm at 4.8 V; 0.1 s/60°; 4.8 V nominal, 6.6 V max; 20 ms frame; ≤ 1 ms = −90°, 1.5 ms = 0°, ≥ 2 ms = +90°; "up to 200 mA under load"; gear 55.49:1; travel 0–150° unless specified.
- Arduino Servo library defaults 544–2400 µs for 0–180° — https://github.com/arduino-libraries/Servo/blob/master/docs/api.md
- Blog-grade [?]: idle 10–15 mA, moving 200–250 mA, stall 650–700 mA — https://microservomotor.com/common-specifications-and-parameters/micro-servo-current-draw.htm
- MG996R: 9.4 kg·cm at 4.8 V, 11 at 6 V; 0.17/0.14 s/60°; run 500–900 mA, stall 2.5 A at 6 V; 55 g — https://components101.com/motors/mg996r-servo-motor-datasheet

### HC-SR04 (https://cdn.sparkfun.com/datasheets/Sensors/Proximity/HCSR04.pdf)
- 5 V, 15 mA working (< 2 mA quiescent), 40 kHz 8-cycle burst, 2–400 cm, 3 mm resolution, 15°, trigger ≥ 10 µs; distance = t_high × 340 / 2; µs/58 = cm; > 60 ms measurement cycle recommended; target ≥ 0.5 m², smooth.
- No echo → Echo times out after ≈ 38 ms — https://howtomechatronics.com/tutorials/arduino/ultrasonic-sensor-hc-sr04/
- Original is 5 V-only (Echo is 5 V → divider for 3.3 V boards); HC-SR04+ and 2021 revisions run at 3.3 V — https://docs.toit.io/tutorials/hardware/ultra/
- Speed of sound 343 m/s at 20 °C; v ≈ 331 + 0.6·T(°C) ⇒ 58.3 µs/cm round trip.

### Batteries
- AA alkaline (Energizer E91): 1.5 V; internal resistance 150–300 mΩ fresh; capacity to 0.8 V ≈ 3000 mAh at low drain falling to ≈ 1000–1500 mAh at 500 mA [?]; resistance rises through discharge — https://data.energizer.com/pdfs/e91.pdf · https://data.energizer.com/pdfs/alkaline_appman.pdf
- 9 V (Energizer 522): ≈ 600 mAh at 10–25 mA to 4.8 V; 45 g — https://data.energizer.com/pdfs/522.pdf; internal resistance ≈ 1–2 Ω; > 600 mA draws sag rapidly [?].
- 18650: Samsung 25R 2500 mAh ≤ 18 mΩ; 30Q 3000 mAh ≤ 26 mΩ; 3.6 V nominal, 4.2 V full, 2.5 V cutoff.
- 2S LiPo: 7.4 V nominal, 8.4 V full, 6.0 V empty; I_max = C-rating × capacity.
- NiMH AA (Energizer NH15): 1.2 V, 2300 mAh; IR 30 mΩ charged / 40 mΩ half; eneloop 1900–2000 mAh.

### Arduino Uno power and MCU
- VIN 7–12 V recommended, 6–20 V limit; 20 mA/pin recommended (40 abs max); 3.3 V pin 50 mA; USB polyfuse trips > 500 mA; regulator "may overheat" > 12 V. 3.3 V part LP2985 (150 mA rated).
- NCP1117: > 1 A; dropout 1.07 V typ / 1.20 max at 800 mA; current limit 1.0–2.2 A; thermal shutdown ≈ 175 °C; SOT-223 θJA 160 °C/W (min pad), θJC 15 — https://www.onsemi.com/pdf/datasheet/ncp1117-d.pdf
- ATmega328P (datasheet): abs max 40 mA/pin, 200 mA VCC/GND; ΣIOL ≤ 100 mA per group {C0–C5}, {B0–B5, D5–D7, XTAL}, {D0–D4}; ΣIOH ≤ 150 mA per group {C0–C5, D0–D4}, {B0–B5, D5–D7, XTAL}; VOL ≤ 0.8 V at 20 mA, VOH ≥ 4.1 V at −20 mA (⇒ driver ≈ 40–45 Ω worst case [?]); pull-ups 20–50 kΩ; BODLEVEL 111 off, 110 1.8 V, 101 2.7 V (2.5–2.9), 100 4.3 V (4.0–4.6); hysteresis 80 mV; BOD fires only if VCC below level > tBOD; restart after tTOUT.
- Uno fuses L 0xFF, H 0xDE, E 0xFD ⇒ BOD 2.7 V — https://github.com/arduino/ArduinoCore-avr/blob/master/boards.txt

### LEDs
- Vf (5 mm): red 1.63–2.03 V, orange 2.03–2.10, yellow 2.10–2.18, green 1.9–4.0, blue 2.48–3.7, white 3.2–3.6; reverse max 5 V — https://components101.com/diodes/5mm-round-led. Typical: red/yellow ≈ 1.8 V, blue/green/white 3.0–3.5 V at 20 mA; 5 mm max usually 20 mA (30 mA abs) — https://electronicsclub.info/leds.htm
- LED straight on a pin: I ≈ (5 − Vf)/≈ 40 Ω ≈ 45–75 mA (derived [?]) > 40 mA abs max; the pin is stressed and the LED is tougher than the pin — https://forum.arduino.cc/t/safe-to-use-led-without-resistor-uno-r3/1281638
- 220 Ω → (5 − 2)/220 ≈ 14 mA; 330 Ω ≈ 9 mA.

### Sensors and parts
- GL5528 LDR: 8–20 kΩ at 10 lux, ≥ 1 MΩ dark, peak 540 nm, rise 45 ms / fall 55 ms — https://www.alldatasheet.com/datasheet-pdf/view/1131893/ETC2/GL5528.html
- TCRT5000: peak sensitivity 2.5 mm, range 0.2–15 mm, IF max 60 mA, Ic 0.5–2.1 mA typ — https://www.vishay.com/docs/83760/tcrt5000.pdf
- IR obstacle module: LM393, 3.3–5 V, 2–30 cm adjustable, ≈ 35°, active-low — https://www.sunfounder.com/products/ir-obstacle-avoidance-sensor-module
- MPU-6050: 0x68/0x69 (AD0); ±2/4/8/16 g (16384 LSB/g at ±2 g); ±250…2000 °/s (131 LSB/°/s at ±250); 400 kHz I2C — https://www.i2cdevlib.com/devices/mpu6050
- HC-05: 9600 data / 38400 AT (KEY/EN high); PIN 1234 — https://www.electronicwings.com/sensors-modules/bluetooth-module-hc-05-
- PCF8574 0x20–0x27 (typ. 0x27), PCF8574A 0x38–0x3F (typ. 0x3F) — https://www.nxp.com/docs/en/data-sheet/PCF8574_PCF8574A.pdf; SSD1306 0x3C/0x3D.
- WS2812B: 800 kbps; T0H 0.4 µs, T1H 0.85, T0L 0.85, T1L 0.4 (±150 ns); RES > 50 µs; GRB MSB-first; VDD 3.5–5.3 V — https://cdn.sparkfun.com/assets/e/6/1/f/4/WS2812B-LED-datasheet.pdf
- 28BYJ-48: 32 steps × 63.68395:1 ⇒ 2037.9 full / 4075.8 half steps per rev (nominal 2048/4096); 5 V; ULN2003 — https://lastminuteengineers.com/28byj48-stepper-motor-arduino-tutorial/
- Buzzer: active = internal oscillator (2–4 kHz on DC); passive needs `tone()`; ≤ ≈ 25–36 mA — https://deepbluembedded.com/active-buzzer-vs-passive-buzzer/
- DHT11/22: host low ≥ 18 ms; sensor responds 80 µs low + 80 µs high; bit = 50 µs low then 26–28 µs (0) / 70 µs (1) high; 40 bits; DHT11 ≤ 1 Hz, DHT22 ≤ 0.5 Hz; 2.5 mA — https://www.ocfreaks.com/basics-interfacing-dht11-dht22-humidity-temperature-sensor-mcu/

## 3. Physics / robotics simulation
- Unity ArticulationBody: reduced coordinates; locked DOF "unbreakable and unstretchable"; no kinematic loops; one root; depth ≤ 64 — https://docs.unity3d.com/Manual/physics-articulations.html; quality independent of mass ratio — https://unity.com/blog/engine-platform/simulate-robots-with-more-realism-whats-new-in-physics-for-unity-20212-beta
- Unity differential-drive guide: wheels as sphere colliders; high-friction physic material on drive wheels, slippery casters; motor via ArticulationDrive damping; raise wheel mass 2–3× if target velocity is unreachable — https://docs.unity3d.com/Simulation/manual/author/control-a-vehicle-model/set-up-a-differential-drive-controller.html
- Timesteps: Unity default 0.02 s; Gazebo 0.001 s; Webots 32 ms default (ODE, ERP 0.2, CFM 1e-5); MuJoCo 0.002 s, PyBullet 1/240 s [?].
- Webots motor: maxVelocity, maxTorque, acceleration, controlPID; position mode = PID with velocity/torque clamps; velocity mode; torque mode — https://github.com/cyberbotics/webots/blob/master/docs/reference/motor.md
- Webots DistanceSensor: numberOfRays, aperture, Gaussian ray weights; lookupTable(dist, value, noise σ); sonar rays with incidence > 22.5° "never return"; infra-red reflectance f = 0.2 + 0.8·red_level·(1 − 0.5·roughness)·(1 − 0.5·occlusion) — https://github.com/cyberbotics/webots/blob/master/docs/reference/distancesensor.md
- Gazebo noise: Gaussian per beam clamped to range; IMU Gaussian + bias sampled once — http://classic.gazebosim.org/tutorials?tut=sensor_noise

## 4. DC motor modelling
- V = R·i + L·di/dt + Ke·ω; T = Kt·i; J·ω̇ = Kt·i − b·ω − T_load; Kt = Ke in SI — https://nrsyed.com/2018/01/21/how-to-read-a-dc-motor-datasheet/
- From datasheet: R = V/I_stall; Kt = T_stall/I_stall; Ke = (V − i_nl·R)/ω_nl; T_friction = Kt·i_nl; gearmotor ratings are output-side (divide/multiply by ratio and efficiency) — https://www.mathworks.com/help/sps/ref/dcmotor.html
- Decay modes (TI SLVA321): slow decay = both low-side switches on, current decays with L/R, shorts back-EMF ⇒ braking; fast decay = opposing switches/diodes, faster decay, more ripple — https://www.ti.com/lit/an/slva321a/slva321a.pdf
- PWM frequency vs τ = L/R: on-time should exceed ≈ 5τ for linear duty→current; with slow decay and continuous conduction V_avg ≈ D·V_supply [?] — https://www.precisionmicrodrives.com/ab-022. Uno PWM 490 Hz (980 Hz on 5/6), 0–255.
