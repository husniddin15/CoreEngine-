# 05 — Arduino Emulation Specification (AVR core + compile pipeline)

Status: DRAFT v0.1 (2026-09-22) · Pillar P1 · Decision: [ADR-0002](adr/ADR-0002-mcu-emulation-approach.md), [ADR-0003](adr/ADR-0003-compile-pipeline.md)

---

## 1. Goals

| Goal | Requirement |
|---|---|
| Binary-accurate | Execute the exact `.hex` produced by the Arduino toolchain for `arduino:avr:uno` (and nano, mega). No source interpretation, no API stubs. |
| Cycle-accurate timing | Every instruction consumes its datasheet cycle count; timers, UART, ADC and interrupts are driven by the same cycle counter. `millis()`, PWM frequencies, `tone()`, Servo pulses, UART baud, WS2812 bit-banging must match hardware to the cycle. |
| Peripheral-complete for Arduino use | Everything the Arduino core and the common libraries touch (see §5). |
| Deterministic | No wall-clock; identical runs for identical inputs. |
| Fast | ≥ 110 M cycles/s per core on min-spec in IL2CPP release ([04 §13](04-technical-design.md)). |
| Debuggable (v1.x) | ELF/DWARF symbol loading, breakpoints, single-step, variable watch. |
| Licensed cleanly | Own implementation in C#. The MIT-licensed avr8js may be consulted for design and its test vectors reused with attribution. No GPL code (simavr, QEMU) inside the shipped binary; simavr may be used as a test oracle in CI only. |

## 2. Supported targets

| Board | MCU | Clock | Flash / SRAM / EEPROM | Tier |
|---|---|---|---|---|
| Arduino Uno R3 | ATmega328P | 16 MHz | 32 KB / 2 KB / 1 KB | v1 (MVP) |
| Arduino Nano (v3, clone with CH340) | ATmega328P | 16 MHz | 32 KB / 2 KB / 1 KB | v1 |
| Arduino Mega 2560 | ATmega2560 | 16 MHz | 256 KB / 8 KB / 4 KB | 1.0, paid Mega 2560 Pack ([ADR-0007](adr/ADR-0007-monetization-free-to-play-dlc.md)) |
| Arduino Pro Mini 5 V | ATmega328P | 16 MHz | same as Uno | v1.x (needs FTDI adapter model) |
| Leonardo / Micro (ATmega32U4) | USB device on-chip | — | — | Out of scope (USB stack emulation) |
| Uno R4, ESP32, Raspberry Pi Pico | ARM / Xtensa / RISC-V | — | — | Out of scope for v1; `ICpuCore` interface is designed so another core can be added |

Bootloader: real boards run Optiboot (Uno: 0.5 KB, Nano old bootloader: 2 KB) which waits briefly for an upload after reset and blinks the L LED. We **do not execute the bootloader binary**; instead the board model applies a configurable "bootloader delay" (Uno ≈ 1 s with the L LED triple-blink) after reset before jumping to address 0, so that the observable behaviour matches (sketches start about a second after reset/power-up). Upload size limits use the real values (Uno 32 256 bytes, Nano old bootloader 30 720 bytes, Mega 253 952 bytes).

## 3. CPU core (AVRe+ / AVR enhanced)

### 3.1 Registers and memory map (ATmega328P)
- 32 general registers R0–R31 at data addresses 0x00–0x1F; X/Y/Z pairs (R26–R31).
- I/O space 0x20–0x5F (`IN/OUT/SBI/CBI/SBIS/SBIC` addressable), extended I/O 0x60–0xFF (memory-mapped only), SRAM 0x0100–0x08FF.
- SREG (I T H S V N Z C), SP (16-bit), PC (14-bit on 328P; 17-bit on 2560 with EIND/RAMPZ for `EIJMP/EICALL/ELPM`).
- Flash: 16 KWords, word-addressed; `LPM/ELPM` read from flash; `SPM` (self-programming) not supported outside the bootloader section → trap and log.
- EEPROM: 1 KB, accessed via EEARH/EEARL/EEDR/EECR with real write timing (~3.3 ms per byte, EEPE busy flag) and persisted in the project file.

### 3.2 Instruction set
All 130+ AVRe+ instructions must be implemented, grouped as: arithmetic/logic (ADD, ADC, ADIW, SUB, SUBI, SBC, SBCI, SBIW, AND, ANDI, OR, ORI, EOR, COM, NEG, SBR, CBR, INC, DEC, TST, CLR, SER, MUL, MULS, MULSU, FMUL, FMULS, FMULSU); branch (RJMP, IJMP, EIJMP, JMP, RCALL, ICALL, EICALL, CALL, RET, RETI, CPSE, CP, CPC, CPI, SBRC, SBRS, SBIC, SBIS, BRxx family, all flag-conditional branches); data transfer (MOV, MOVW, LDI, LD/LDD/LDS with all X/Y/Z addressing modes, ST/STD/STS, LPM/ELPM variants, SPM, IN, OUT, PUSH, POP); bit and bit-test (LSL, LSR, ROL, ROR, ASR, SWAP, BSET, BCLR, SBI, CBI, BST, BLD, SEC…CLC flag set/clear family); MCU control (NOP, SLEEP, WDR, BREAK).

Reference: Atmel/Microchip *AVR Instruction Set Manual* (cycle counts per instruction; note 2-word instructions JMP/CALL/LDS/STS and the extra cycle for taken branches, and `CPSE/SBRC/SBRS/SBIC/SBIS` skipping a 2-word instruction costs 3 cycles).

### 3.3 Decoding and execution loop
- On flash load, every 16-bit word is pre-decoded into a compact record `{opcodeKind, operands, cycles, length}` stored in an array indexed by word address. Execution is a `switch` over `opcodeKind`. Pre-decoding avoids per-instruction bit-twiddling and is invalidated only on a new upload.
- The run loop: `while (cycles < target) { if (pendingInterrupt && SREG.I) Dispatch(); var ins = decoded[PC]; Execute(ins); cycles += ins.cycles(+penalty); if (cycles >= nextPeripheralEvent) RunPeripherals(); }`.
- No allocations, no virtual calls in the hot path, no bounds-checked lists (arrays only), `[MethodImpl(AggressiveInlining)]` on register helpers.

### 3.4 Interrupts
- Vector table per MCU (328P: 26 vectors, 2 words each; 2560: 57 vectors, 2 words each).
- Latency: 4 cycles minimum + completion of the current instruction; `RETI` 4 cycles; after `SEI`/`RETI` one more instruction executes before a pending interrupt is served (datasheet behaviour; the Arduino core relies on this in places).
- Priority by vector address (lowest first). Flags cleared on entry per peripheral semantics (some auto-clear, some require software clear).
- Sleep: `SLEEP` halts the core until an enabled interrupt; cycle counter still advances (idle mode). Power-down modes stop timers except WDT — modelled minimally.
- Reset sources tracked in MCUSR: PORF, EXTRF (reset button, DTR auto-reset), BORF (from the electrical model when VCC < BOD level for > 2 µs, default BOD 2.7 V per Uno fuses), WDRF.

## 4. Pins and the boundary with the electrical model

Each MCU pin exposes a `PinDriver` state to the electrical solver:

| State | Source | Electrical model |
|---|---|---|
| Output high | DDR=1, PORT=1 | Voltage source VCC with series resistance R_oh. Datasheet worst case: VOH ≥ 4.1 V at −20 mA → 45 Ω; typical parts measure ≈ 25–30 Ω [VERIFY]; use 30 Ω default, 45 Ω in "worst-case" mode |
| Output low | DDR=1, PORT=0 | Sink to GND with series R_ol. Datasheet worst case: VOL ≤ 0.8 V at 20 mA → 40 Ω; use 25 Ω default |
| Input, pull-up | DDR=0, PORT=1 | Resistor 20–50 kΩ (use 35 kΩ) to VCC |
| Input, floating | DDR=0, PORT=0 | High impedance; net voltage from other drivers; if the net is undriven, the reading is **noise**: a deterministic pseudo-random sequence biased by nearby nets (teaches floating inputs) |
| Analog input (ADC channel) | ADMUX selects | Sample net voltage at conversion start (sample-and-hold), input impedance assumed ≫ source |

Digital input threshold with hysteresis: reads 1 above 0.6·VCC, 0 below 0.3·VCC, keeps previous state in between (datasheet VIH/VIL). Per-pin current limit 40 mA absolute max, 20 mA recommended; totals per port and per chip (200 mA VCC/GND) are accumulated by the failure model (doc 06).

Pin events: writes to DDRx/PORTx/PINx (toggle) create `PinChange(cycle, pin, newDriver)` events consumed immediately by the electrical model (event-driven digital nets). External level changes call back `SetInputLevel(pin, level, cycle)` which updates PINx, triggers PCINT/INT0/INT1 and timer input capture as configured.

## 5. Peripherals (ATmega328P; Mega adds more instances)

| Peripheral | Registers | Behaviour required |
|---|---|---|
| GPIO ports B, C, D | DDRx, PORTx, PINx | As §4. PINx write toggles. |
| Timer/Counter0 (8-bit) | TCCR0A/B, TCNT0, OCR0A/B, TIMSK0, TIFR0 | Modes: Normal, CTC, Fast PWM (top 0xFF / OCR0A), Phase-correct PWM; prescalers 1/8/64/256/1024 and external clock T0 (pin D4); OC0A→D6, OC0B→D5 with all COM modes; OVF/COMPA/COMPB interrupts. Arduino core: Fast PWM, /64 → `millis()` via OVF every 1024 µs; `analogWrite` on 5/6 = 976.56 Hz. |
| Timer/Counter1 (16-bit) | TCCR1A/B/C, TCNT1, OCR1A/B, ICR1, TIMSK1, TIFR1 | All 16 WGM modes incl. ICR1 top, phase/frequency-correct; input capture from ICP1 (D8) with noise canceller and edge select; OC1A→D9, OC1B→D10; 16-bit temp register semantics for TCNT1/OCR1/ICR1 access. Arduino core: phase-correct /64 → 490.2 Hz on 9/10; Servo library uses CTC-like compare scheduling; `pulseIn` timing depends on exact loop cycles. |
| Timer/Counter2 (8-bit) | TCCR2A/B, TCNT2, OCR2A/B, TIMSK2, TIFR2, ASSR | Same as Timer0 but prescalers 1/8/32/64/128/256/1024; OC2A→D11, OC2B→D3; `tone()` uses CTC on Timer2. Asynchronous (32 kHz crystal) mode not modelled (ASSR writes logged). |
| USART0 | UDR0, UCSR0A/B/C, UBRR0H/L | Baud from UBRR and U2X; 5–9 data bits, parity, 1–2 stop bits; TX shift register and UDRE/TXC timing; RX with framing/parity errors, RXC and DOR; interrupts. **Bits are driven on the TX pin (D1) net with exact bit timing** and sampled from the RX pin (D0) net, so both hardware serial and bit-banged `SoftwareSerial` work through the same net mechanism. The USB bridge device (ATmega16U2/CH340 model) listens on those nets. |
| ADC | ADMUX, ADCSRA, ADCSRB, ADCL/H, DIDR0 | 10-bit successive approximation; prescaler (Arduino uses /128 → 125 kHz ADC clock, 13 ADC clocks ≈ 104 µs per conversion, first conversion 25 clocks); references AVCC / internal 1.1 V / AREF pin; channels A0–A5 (A6/A7 on Nano); temperature sensor channel and 1.1 V bandgap channel; free-running and trigger sources; ±1 LSB deterministic noise; result left-adjust. Mega: 16 channels. |
| External interrupts | EICRA, EIMSK, EIFR | INT0 (D2), INT1 (D3): low level, any change, falling, rising. Mega: INT0–INT5 with their pin mapping. |
| Pin-change interrupts | PCICR, PCMSK0–2, PCIFR | All pins; used by SoftwareSerial and rotary encoder libraries. |
| SPI | SPCR, SPSR, SPDR | Master mode with all clock rates/modes; bit-level timing on SCK/MOSI/MISO nets (D13/D11/D12); SS handling. Slave mode optional. Devices: 74HC595, SD card (not v1), nRF24L01 (not v1). |
| TWI (I2C) | TWBR, TWSR, TWCR, TWDR, TWAR, TWAMR | Master transmitter/receiver with all status codes used by the `Wire` library; START/STOP/ACK timing at the configured SCL rate; **bit-level on SDA/SCL nets (A4/A5)** with open-drain modelling and pull-up requirement; clock stretching from slaves; bus error when pull-ups are missing (reads 0xFF/NACK). Devices: PCF8574 LCD backpack, SSD1306, MPU6050, DS3231, BMP280 (tier 2). |
| EEPROM | EEARH/L, EEDR, EECR | Read immediate; write/erase-write 1.8–3.4 ms with EEPE busy; EERIE interrupt; persisted. |
| Watchdog | WDTCSR, MCUSR | Timed sequence for changes; prescaler 16 ms–8 s; interrupt and/or reset; WDR instruction. |
| Power management | SMCR, PRR | Sleep modes (idle, ADC noise reduction, power-down, standby); PRR gating logged (no functional effect except ADC/TWI/SPI/USART disable). |
| Analog comparator | ACSR, ADCSRB (ACME) | Basic compare of AIN0/AIN1 (D6/D7) or ADC mux vs bandgap; interrupt. Tier 2. |
| Clock/fuses | — | Fixed: 16 MHz external, CKDIV8 off, BOD level from board profile, bootloader size (BOOTSZ) for the vector base after "bootloader delay". `CLKPR` writes honoured (prescaler changes affect all peripherals). |
| debugWIRE/JTAG | — | Not modelled. |

Register-level reference: *ATmega328P datasheet* (Microchip DS40002061) and *ATmega2560 datasheet* (DS40002211). Every register bit that the Arduino AVR core (`wiring.c`, `wiring_analog.c`, `HardwareSerial.cpp`, `Tone.cpp`, `WInterrupts.c`), `Servo`, `Wire`, `SPI`, `SoftwareSerial`, `EEPROM` and the top-50 libraries touch must be implemented; the golden-test corpus in §10 enforces this.

## 6. Board models (around the MCU)

| Board element | Model |
|---|---|
| USB-to-serial bridge (ATmega16U2 on Uno; CH340 on Nano clones) | A virtual device on the D0/D1 nets (through the real 1 kΩ series resistors on the Uno) that decodes/encodes UART at the monitor's baud. DTR toggle on monitor open pulses the RESET line through the 100 nF auto-reset capacitor (→ EXTRF reset). TX/RX LEDs blink on traffic. |
| Power path | USB 5 V through a 500 mA resettable polyfuse; barrel jack/VIN through the reverse-protection diode and the NCP1117 5 V LDO (dropout ~1.1 V → VIN ≥ 6.2 V needed for 5 V; thermal limit modelled), LP2985 3.3 V LDO (150 mA device, board rating 50 mA). Automatic source selection: 5 V from USB unless VIN > 6.6 V (comparator on Uno). Nano: UA78M05 / similar. |
| Reset button | Clickable; pulls RESET low. |
| On-board LEDs | ON (power), L (D13 via op-amp buffer on Uno R3 — does not load D13), TX/RX. |
| Decoupling | VCC net has a lumped 47 µF-equivalent capacitance so brief dips are filtered as on the real board; brown-out below 2.7 V for > 2 µs → BORF reset. |
| Header geometry | Pin sockets positioned to real Uno geometry (including the 0.16" offset between D7 and D8 that prevents flipped shields). |

## 7. Timing integration

- The MCU runs in 1 ms slices (16 000 cycles) inside the electrical tick ([04 §4](04-technical-design.md)). Within a slice, pin events are applied at their exact cycle.
- Peripherals are **lazily advanced**: each keeps `nextEventCycle` (next compare match, overflow, UART bit edge, ADC completion, EEPROM ready, WDT timeout). The CPU loop runs until the minimum of those, then services the event. Register reads compute the current counter value from the elapsed cycles; writes resynchronise.
- Devices needing sub-slice timing (HC-SR04 echo, UART bit sampling, WS2812 decoding, servo pulse measurement) register cycle-stamped callbacks with the scheduler. Physics-derived quantities (distance to obstacle) are sampled from the latest physics state (≤ 10 ms old), which is well below the sensor's own measurement cycle.

## 8. Compile pipeline

### 8.1 Bundled toolchain
- `tools/arduino-cli/arduino-cli.exe` — v1.5.x (latest 1.5.1, June 2026), GPLv3, run as a separate process. Arduino's own licensing page states that calling the CLI "as a separate executable binary from another program" is permitted without licensing our code under the GPL ([R1 §3](research/R1-avr-emulation-and-toolchain-facts.md)). Licence text and source links are shipped — see doc 12 §3.
- `arduino:avr` core **1.8.8** (May 2026; depends on `avr-gcc@7.3.0-atmel3.6.1-arduino7` ≈ 50 MB Windows zip, avr-libc 2.0.0, binutils 2.26, `avrdude@8.0.0-arduino1` unused) pre-installed at build time into a data directory shipped with the app, selected with a bundled `arduino-cli.yaml` (`directories.data`, `directories.user`, `directories.downloads`) or the `ARDUINO_DIRECTORIES_DATA` environment variable, so the user compiles fully offline. Phase 0 must verify this on a clean machine (doc 13 §2).
- Bundled libraries (installed into the user library dir at build time): Servo, Wire, SPI, EEPROM, SoftwareSerial, LiquidCrystal, LiquidCrystal_I2C, NewPing, Adafruit_GFX, Adafruit_SSD1306, Adafruit_NeoPixel, FastLED, MPU6050 (electroniccats or i2cdevlib), DHT sensor library, IRremote, Stepper, AccelStepper, PID_v1, Bounce2, Encoder, ArduinoJson, TM1637Display, MPU6050_light, Adafruit_MPU6050 (verify each licence — most are MIT/BSD/LGPL; LGPL is fine for source distribution). Every library referenced by the tutorial or by a Notebook card ([10](10-content-arenas-tutorial-notebook.md)) must be in this bundle.
- Online library install (`arduino-cli lib install`) offered when internet is available.

### 8.2 Invocation
```
arduino-cli compile --fqbn arduino:avr:uno ^
  --config-file "<app>/tools/arduino-cli/arduino-cli.yaml" ^
  --build-path "<localappdata>/CoreEngine/build/<projectId>/<board>" ^
  --output-dir "<localappdata>/CoreEngine/out/<projectId>/<board>" ^
  --warnings default --format json "<sketchDir>"
```
Outputs: `<sketch>.ino.hex`, `<sketch>.ino.elf`, `<sketch>.ino.eep`, `<sketch>.ino.with_bootloader.hex/.bin`, JSON with `compiler_out`, `compiler_err`, `builder_result.executable_sections_size` (flash/RAM usage). The build path is cached per project to keep rebuilds at ~1–2 s. Note: `--build-path` combined with `--output-dir` had a reported interaction problem (arduino-cli issue #2318); if it persists in v1.5.x, omit `--output-dir` and read the artefacts from the build path.

Nano FQBN: `arduino:avr:nano:cpu=atmega328` (new bootloader) or `cpu=atmega328old`. Mega: `arduino:avr:mega:cpu=atmega2560`.

Implementation note (Phase 0, 2026-09-23): `ArduinoCliCompiler` in `core/CoreEngine.Sim/Compile` runs this command without `--format json`, because the core library stays free of a JSON dependency; the configuration forces English output (`locale: en`), GCC diagnostics are parsed from the text, and the flash size is computed from the `.hex`. Measured compile times are in [13 §2 Q11](13-open-questions-and-risks.md).

Process start (2026-09-24): Unity's IL2CPP runtime does not implement `System.Diagnostics.Process`; starting arduino-cli from the IL2CPP player failed with "Native error= Success" ([Unity forum](https://discussions.unity.com/threads/solved-il2cpp-and-process-start.533988/)). `Win32ProcessRunner` (`core/CoreEngine.Sim/Compile/ProcessRunner.cs`) starts it with `CreateProcessW` through P/Invoke, one anonymous pipe for standard output and error, no console window, and a timeout. It is the default on Windows, so tests, simcli, Mono and IL2CPP builds use the same code; a test compiles Blink through it into folders with spaces and checks the hex against the golden file.

### 8.3 Diagnostics mapping
Parse `compiler_err` lines `path:line:col: (error|warning|note): message` (GCC format). Map paths back to the editor's tabs. Provide curated explanations for the most frequent messages.

### 8.4 Loading
Intel HEX parser → flash byte array; verify size ≤ board limit (report like the IDE: "Sketch uses 924 bytes (2%) of program storage space"). `.eep` is not auto-loaded (Arduino IDE does not either). Keep `.elf` for the v1.x debugger (DWARF via a managed ELF reader).

## 9. Serial Monitor / Plotter and virtual serial devices

- Monitor connects to the USB bridge device, not to the MCU directly, so the physical behaviour (D0/D1 conflicts, auto-reset on open, baud mismatch garbage, TX/RX LEDs) is inherited.
- Line ending options, timestamps, autoscroll, 1 MB scrollback, export.
- Plotter: parses numeric tokens per line; up to 8 series; labels `name:value` supported.
- Other UART devices (HC-05 Bluetooth with virtual phone, GPS NMEA generator (tier 2), a second board) are simply more devices on nets.

## 10. Verification and test corpus

1. **Instruction tests**: for every instruction, cases for flags, result, PC advance and cycle count; generated from the instruction set manual tables; cross-checked against avr8js test vectors (MIT, with attribution) and against simavr in CI (differential fuzzing with random instruction streams and register states; simavr is a CI-only tool).
2. **Peripheral tests**: all timer WGM/COM combinations with expected waveforms; UART at 300, 9600, 57600, 115200, 250000, 500000, 1 000 000 baud (note the real baud errors at 16 MHz: the Arduino core uses U2X for 115200 → UBRR 16 → 117 647 baud, +2.1 %; for 57600 it deliberately uses U2X = 0 → UBRR 16 → 58 824 baud, +2.1 %; the emulator must reproduce these exact rates, not the nominal ones); ADC timing; EEPROM busy timing; WDT timeouts; interrupt latency.
3. **Golden sketches** (compiled with the bundled toolchain, run headless, waveform/serial assertions): Blink (1.000 s period), BlinkWithoutDelay, Fade (976.56 Hz PWM on D5/D6 and 490.2 Hz on D9), toneMelody (exact frequencies), Servo Sweep (pulse widths 544–2400 µs at 50 Hz), Serial echo at several bauds, `pulseIn` with a generated pulse, I2C scanner with a PCF8574 at 0x27, MPU6050 WHO_AM_I, NeoPixel strandtest (decoded WS2812 bits: 0.4/0.85 µs ±150 ns), SoftwareSerial loopback, EEPROM write/read, watchdog reset, `micros()` drift over 60 s (0 drift), interrupt counting with a 10 kHz input, AccelStepper timing, sleep + wake by INT0.
4. **Hardware fidelity log**: the same sketches on a real Uno with a logic analyser (Saleae or a USD 10 clone); compare edge timings; discrepancies become issues.
5. **Performance benchmark**: cycles/s on a reference sketch mix (Blink, Serial, I2C LCD, PID loop) in IL2CPP release; gate: ≥ 110 M cycles/s per core on the min-spec CPU.

## 11. Debugger (v1.x)

- Load `.elf` with DWARF (line table, variables, types). Breakpoints at source lines (map to word addresses), single-step at instruction and line level, step over/into/out, register and SREG view, SRAM/EEPROM viewer, watch expressions for globals and locals (stack-frame aware for simple cases), call stack from frame info.
- Pause is trivially supported (deterministic core); "run to cursor"; conditional breakpoints on pin events ("break when D9 goes high").
- Optional GDB Remote Serial Protocol server on localhost so VS Code/`avr-gdb` can attach (Wokwi offers this; it is cheap to add once the core exists).

## 12. Known deviations from hardware (documented on purpose)
- Bootloader code not executed (behavioural delay instead).
- Asynchronous Timer2, debugWIRE, JTAG, SPM outside the bootloader, and the 16U2 firmware internals are not modelled.
- ADC noise, floating-input noise and oscillator jitter are deterministic pseudo-random sequences (seeded per run), not physical noise.
- Temperature effects on the internal oscillator are ignored (external crystal assumed).

## 13. Risks specific to emulation
| Risk | Mitigation |
|---|---|
| Subtle timing bug breaks a popular library (e.g., NeoPixel, SoftwareSerial) | Golden corpus includes them; differential tests vs simavr; hardware log |
| C# too slow on min-spec | Pre-decode + lazy peripherals; IL2CPP; plan B: C++ core behind `ICpuCore` |
| Mega 2560 3-byte PC and extended I/O differences | Implement `ICpuCore` with device profile tables; add Mega tests early |
| Toolchain packaging on Windows (paths with spaces/Unicode user names) | Always pass explicit `--config-file`, `--build-path`; test with a Cyrillic user profile path |
