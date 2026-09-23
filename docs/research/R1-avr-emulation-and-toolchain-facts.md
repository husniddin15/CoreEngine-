# R1 — AVR emulation and Arduino toolchain: fact sheet

Researched 2026-09-22 (web). "FLAG" = not verified from a primary source. This is a research appendix; decisions live in [05-arduino-emulation-spec.md](../05-arduino-emulation-spec.md) and the ADRs.

## 1. avr8js (wokwi/avr8js)
- License: MIT — https://github.com/wokwi/avr8js (package.json `"license": "MIT"`).
- Latest release: v0.21.1 (2026-08-31), maintenance-level changes. Recent: v0.20.x (ATtiny85 INT0 fix), v0.19.0 (ATtiny85 USI), v0.18.11 ("don't clear RAM on reset") — https://github.com/wokwi/avr8js/releases.
- Size: ≈5 360 lines of non-test TypeScript in `src/` (`instruction.ts` ≈ 801 lines); ≈5 270 lines of tests (~347 `it(` cases, vitest); 352 commits.
- MCUs: README: focuses on ATmega328p; "highly configurable" for ATmega2560 and ATtiny. Public exports include configs for timer0/1/2, usart0, ports A–L, adc, spi, twi, eeprom, watchdog, clock, ATtiny timer1, USI; `MAX_INTERRUPTS = 128` ("Enough for ATMega2560"). Mega Timer3/4/5 and USART1–3 configs are NOT in the public exports (Wokwi's hosted Mega has them) — FLAG (inferred from grep).
- Peripherals (`src/peripherals/`): adc, clock (CLKPR), eeprom, gpio (INT0/INT1 via EICRA/EICRB; PCINT0–2 with PCMSK), spi, timer (Normal/CTC/FastPWM/PhaseCorrect/PhaseFreqCorrect; ICR top), timer-attiny, twi, usart, usi, watchdog — https://github.com/wokwi/avr8js/tree/master/src/peripherals.
- Not implemented: `SLEEP` is a no-op; `SPM` not implemented (no self-programming/bootloader). Wokwi docs: Uno — SPI/I2C master mode only, analog comparator not supported (https://docs.wokwi.com/parts/wokwi-arduino-uno); Mega — input capture not implemented in the 16-bit timers, output compare modulator and analog comparator not supported.
- Timing model: no explicit cycle-accuracy claim in the README. Each instruction ends with `cpu.cycles++`; multi-cycle ops add 1–3. Peripherals schedule via `cpu.addClockEvent(cb, cycles)` (sorted linked list) executed by `cpu.tick()`; timers count lazily from cycle deltas; USART schedules `cyclesPerChar` events. Demo pacing: `speed = 16e6`, `workUnitCycles = 500000`.
- Performance: the only published figure (2019): "runs about half speed" in the browser — https://blog.wokwi.com/avr8js-simulate-arduino-in-javascript/. No current benchmark found (FLAG).
- Wokwi ESP32 core: no simulator-core repo in the org; inferred proprietary (FLAG). rp2040js: MIT — https://github.com/wokwi/rp2040js.

## 2. Other AVR emulators
- **simavr**: GPL-3.0; Linux/macOS focus; ATmega 2560/1280/328/…, ATtiny; eeprom, watchdog, ports + pin interrupts, timers, UART, SPI, I2C master/slave, ext-int, ADC, self-programming, GDB, VCD — https://github.com/buserror/simavr. Windows build unofficial/broken (MinGW `strsep`) — https://github.com/buserror/simavr/issues/459.
- **simulavr**: GPL-2.0, C++; Win/MinGW "yes (TCL interface unchecked)" — https://github.com/ljessendk/simulavr. RepRap wiki claims cycle-accurate — https://reprap.org/wiki/SimulAVR.
- **QEMU AVR**: GPLv2 overall — https://github.com/qemu/qemu/blob/master/LICENSE. Boards: arduino-duemilanove (ATmega168), arduino-uno (ATmega328), arduino-mega (ATmega1280), arduino-mega-2560-v3 — https://github.com/qemu/qemu/blob/master/hw/avr/arduino.c. "model is limited to USART & 16-bit timer devices" — https://github.com/qemu/qemu/blob/master/docs/system/target-avr.rst. No GPIO/ADC/PWM → unusable for a physics sandbox.
- Permissive candidates:
  - **avrcore** — C99, MIT; "cycle-accurate ATmega328P emulator… runs unmodified avr-gcc firmware with simulated timers, USART, GPIO and interrupts"; Timer0/1, 26 vectors, EEPROM; NOT Timer2/SPI/TWI/ADC; CMake; pushed 2026-08-05; 0 stars — https://github.com/TheModelStudent/avrcore.
  - **arduboy-emu** — Rust, Apache-2.0/MIT; "cycle-accurate"; ATmega32u4 + ATmega328P; Timer0/1/2/3/4, SPI, ADC, EEPROM; "80+ instructions"; GDB server; 0 stars — https://github.com/nanamitm/arduboy-emu.
  - dylanmckay/avr — Rust, MIT; incomplete SREG updates — https://github.com/dylanmckay/avr.
  - roboter/AVR-Simulator — C#/WPF .NET 10, Unlicense, proof of concept — https://github.com/roboter/AVR-Simulator.
  - Migueel0/ATMega328p-emulator — C++, MIT, CPU/memory only — https://github.com/Migueel0/ATMega328p-emulator.
  - Roller23/digi-avr — C, complete-ish, NO licence file → not usable — https://github.com/Roller23/digi-avr.
  - `avr-simulator` Rust crate is MIT but wraps simavr (GPL) — copyleft still applies — https://crates.io/crates/avr-simulator. emulino: GPLv3+ — https://github.com/ghewgill/emulino.
- MPLAB X simulator: proprietary Microchip, IDE-integrated — https://ww1.microchip.com/downloads/aemDocuments/documents/MCU08/ProductDocuments/UserGuides/AVR-Simulator-UserGuide-DS50003042A.pdf. Not embeddable (FLAG: EULA not read).
- Embedding rule: linking GPL code into a closed binary triggers copyleft. Workaround = separate process + IPC (GPLv3 §5 "aggregate"). Only avr8js/avrcore/arduboy-emu are in-process-safe. FSF FAQ "MereAggregation": https://www.gnu.org/licenses/gpl-faq.html#MereAggregation (unreachable this session; FLAG).

## 3. arduino-cli / toolchain
- Latest: v1.5.1 (2026-06-05); v1.5.2-rc.1 (2026-07-23) adds Windows/ARM64 — https://github.com/arduino/arduino-cli/releases.
- License: GPLv3 — https://github.com/arduino/arduino-cli/blob/master/LICENSE.txt.
- **Arduino's official position**: "If you intend to make the Arduino IDE, CLI, or Cloud-CLI available to your users in binary form without any modification… you can freely do so. This includes the case of calling the CLI as a separate executable binary from another program." / "If you intend to… call it as a library…, its GPLv3 license will apply to your code as well"; commercial licence via license@arduino.cc — https://support.arduino.cc/hc/en-us/articles/4415094490770-Licensing-for-products-based-on-Arduino (text verified from https://github.com/arduino/help-center-content). Core is LGPL 2.1+ — https://raw.githubusercontent.com/arduino/ArduinoCore-avr/master/cores/arduino/Arduino.h.
- Command: `arduino-cli compile --fqbn arduino:avr:uno --output-dir out <sketchdir>`. Flags: `--output-dir`, `--build-path`, `-e/--export-binaries`, `--libraries`, `--library`, `--build-property`, `--warnings`, `-v`, `--json`, `--only-compilation-database` — https://arduino.github.io/arduino-cli/1.5/commands/arduino-cli_compile/. Note: `--build-path` + `--output-dir` interaction issue — https://github.com/arduino/arduino-cli/issues/2318.
- Outputs (platform.txt recipes): `{build.project_name}.elf`, `.eep`, `.hex` (`-R .eeprom`), plus `<name>.with_bootloader.hex/.bin` written by the CLI (`mergeSketchWithBootloader`) — https://raw.githubusercontent.com/arduino/ArduinoCore-avr/master/platform.txt, https://github.com/arduino/arduino-cli/blob/master/internal/arduino/builder/sketch.go. Project name = `<sketch>.ino`.
- Config: `arduino-cli.yaml`; Windows `directories.data` = `%LOCALAPPDATA%\Arduino15`, downloads `{data}/staging`, user `{DOCUMENTS}/Arduino`, build cache `%LOCALAPPDATA%\arduino`; every key overridable via `ARDUINO_*` env vars (`ARDUINO_DIRECTORIES_DATA`, `ARDUINO_CONFIG_FILE`) — https://arduino.github.io/arduino-cli/1.5/configuration/.
- Index: https://downloads.arduino.cc/packages/package_index.json. `arduino:avr` latest 1.8.8 (2026-05-21; 1.8.7 fixed CVE-2025-69209) — https://github.com/arduino/ArduinoCore-avr/releases. Deps: `avr-gcc@7.3.0-atmel3.6.1-arduino7`, `avrdude@8.0.0-arduino1`, `arduinoOTA@1.3.0`.
- Toolchain: `avr-gcc-7.3.0-atmel3.6.1-arduino7-i686-w64-mingw32.zip` ≈ 50 MB (gcc 7.3.0 + Atmel 3.6.1 patches, binutils 2.26, avr-libc 2.0.0, gdb 7.8) — https://github.com/arduino/toolchain-avr. Core tarball ≈ 7 MB.
- Offline core install: `file:///…/package_x_index.json` in `additional_urls` reported working (forum; FLAG) — https://forum.arduino.cc/t/arduino-cli-on-win10-file-protocol-in-json/1025877. Alternative: ship a pre-populated data dir via `ARDUINO_DIRECTORIES_DATA` (design inference; must test).
- arduino-builder is deprecated in favour of arduino-cli — https://github.com/arduino/arduino-builder.
- Precedents: Wokwi compiles server-side (https://docs.wokwi.com/guides/libraries); Velxio compiles on a backend with official tools (https://hackaday.com/2026/04/06/simulating-the-avr8-for-a-browser-based-arduino-emulator/); Shortcuit (Steam) uses arduino-cli installed by the user (https://store.steampowered.com/app/2125820/Shortcuit/).
- Direct avr-gcc without the CLI: recipes are public (C: `-c -g -Os -std=gnu11 -ffunction-sections -fdata-sections -MMD -flto -fno-fat-lto-objects -mmcu={mcu} -DF_CPU={f_cpu} -DARDUINO={ver} -DARDUINO_{board} -DARDUINO_ARCH_{arch}`; C++ adds `-std=gnu++11 -fpermissive -fno-exceptions -fno-threadsafe-statics -Wno-error=narrowing`; link `-Os -g -flto -fuse-linker-plugin -Wl,--gc-sections … core.a -lm`). Cons: reimplementing .ino preprocessing, library resolution, board macros — https://arduino.github.io/arduino-cli/1.5/sketch-build-process/.

## 4. Arduino trademark
- Policy: "Arduino and the Arduino logo are trademarks or registered trademarks of Arduino S.r.l."; forbidden: using an Arduino trademark as part of a company name or logo, in a commercial domain name, or on a third-party product; required acknowledgement: "Arduino® is a trademark of Arduino S.r.l." — https://www.arduino.cc/en/trademark.
- Compatible-products guide (updated 2025-10-31): allowed "Compatible with Arduino", "For Arduino", "Based on Arduino"; always place the word Arduino last ("Ladybird Shield for Arduino"); forbidden: naming the product "Arduino …", Arduino in company/domain names, the Arduino logo on product/packaging/promo; own distinguishable logo required — https://www.arduino.cc/en/trademark/guides/trademark-guide-for-compatible-products/. FLAG: guides are hardware-oriented; no software-specific text found.

## 5. Board facts
- **Uno R3** (https://docs.arduino.cc/hardware/uno-rev3/): ATmega328P; 5 V; VIN 7–12 V (limit 6–20); 14 DIO, PWM 3,5,6,9,10,11; 6 ADC; 20 mA/pin; 3.3 V pin 50 mA; 32 KB flash (0.5 KB bootloader), 2 KB SRAM, 1 KB EEPROM; 16 MHz ceramic resonator; one USART (0/1); INT 2,3; SPI 10–13; I2C A4/A5; polyfuse 500 mA; ATmega16U2 USB-serial. Parts: NCP1117ST50T3G (5 V), LP2985-33DBVR (3.3 V), MF-MSMF050-2 PTC, FDN340P + LMV358 (source switch) — https://www.allaboutcircuits.com/technical-articles/understanding-arduino-uno-hardware-design/ (secondary); official schematic https://docs.arduino.cc/resources/schematics/A000066-schematics.pdf.
- boards.txt (https://raw.githubusercontent.com/arduino/ArduinoCore-avr/master/boards.txt): uno `maximum_size=32256`, `maximum_data_size=2048`, fuses L=0xFF H=0xDE E=0xFD, optiboot 115200; nano `30720` (old bootloader 57600, ATmegaBOOT) / new bootloader same as uno size; mega2560 `253952`, `8192`, H=0xD8 E=0xFD, stk500v2.
- BOD: E=0xFD → 2.7 V — https://github.com/arduino/ArduinoCore-avr/issues/164.
- PWM: "490 Hz (pins 5 and 6: 980 Hz)" on Uno/Nano/Mini — https://github.com/arduino/reference-en/blob/master/Language/Functions/Analog%20IO/analogWrite.adoc. ADC 10-bit, ~100 µs/read; Uno A0–A5, Nano A0–A7 — analogRead reference.
- **Nano**: 32 KB (2 KB bootloader), 2 KB SRAM, 1 KB EEPROM, 16 MHz, 8 ADC, 22 DIO/6 PWM, 20 mA/pin, VIN 7–12 V; FT232RL on the official board (3.3 V from its LDO, 50 mA); 5 V regulator UA78M05 (secondary; FLAG). Clones: CH340G, identical bootloader — https://axotron.se/blog/updated-arduino-nano-clones/.
- **Mega 2560 R3**: ATmega2560 + ATmega16U2; 54 DIO/15 PWM; 16 ADC; 256 KB (8 KB bootloader), 8 KB SRAM, 4 KB EEPROM; 16 MHz crystal; 4 UARTs; INT 2,3,18–21; SPI 50–53; I2C 20/21; 3.3 V 50 mA; polyfuse 500 mA — https://store.arduino.cc/products/arduino-mega-2560-rev3.
- ATmega328P limits: 40 mA/pin, 200 mA VCC/GND, ~100 mA per port group — datasheet https://ww1.microchip.com/downloads/en/DeviceDoc/ATmega48A-PA-88A-PA-168A-PA-328-P-DS-DS40002061A.pdf.
- **Uno R4 Minima** (out of scope): Renesas RA4M1, 48 MHz Cortex-M4 + FPU, 256 KB flash, 32 KB SRAM, 8 KB data flash, 14-bit ADC — https://docs.arduino.cc/hardware/uno-r4-minima. Needs a Cortex-M emulator.

## 6. 2024–2026 signals
- Velxio (Hackaday 2026-04-06): avr8js + rp2040js + RISC-V; backend compile; self-hostable — https://velxio.dev/atmega328p-simulator/.
- Edrys-Labs classroom module on avr8js (2025-11) — https://edrys-labs.github.io/blog/posts/013_module-avr8js/.
- Tinkered.ai claims cycle-accurate AVR emulation (engine unnamed) — https://www.tinkered.ai/tinkercad.
- Shortcuit (Steam, "Coming soon"): AVR emulation of real compiled code with user-installed arduino-cli; motors, drivers, ultrasonic — closest precedent.
- No Unity/Godot/Unreal AVR integration or avr8js port to C#/C++ found (FLAG).
