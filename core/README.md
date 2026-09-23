# core

The engine-independent simulation core (docs/04-technical-design.md §1–3). Plain C#, no Unity types, so it builds and tests with the .NET SDK and runs headless.

| Project | Purpose |
|---|---|
| `CoreEngine.Sim` | The simulation library. netstandard2.1 and C# 9, so Unity 6 can compile the same source. It is also a local Unity package (`package.json`, `CoreEngine.Sim.asmdef` with no engine references) that `app/` loads from this folder. |
| `CoreEngine.Sim.Tests` | xUnit tests, including golden tests with real compiled sketches. |
| `CoreEngine.Sim.Cli` | `simcli`, the headless runner. |

## Commands

```powershell
dotnet test core/CoreEngine.slnx -c Release
dotnet run --project core/CoreEngine.Sim.Cli -c Release -- run core/CoreEngine.Sim.Tests/Golden/Hex/Blink.hex --seconds 5
dotnet run --project core/CoreEngine.Sim.Cli -c Release -- bench core/CoreEngine.Sim.Tests/Golden/Hex/Blink.hex
dotnet run --project core/CoreEngine.Sim.Cli -c Release -- compile path\to\MySketch --run 3
```

`compile` needs the bundled toolchain: run `tools/fetch-toolchain.ps1` once.

## Emulator status (Phase 0 spike, 2026-09-23)

| Part | Status |
|---|---|
| AVR CPU (ATmega328P) | All AVRe+ instructions with datasheet cycle counts and flags; interrupts with the one-instruction delay after SEI/RETI; sleep; pre-decoded program. |
| GPIO ports B, C, D | DDR/PORT/PIN, PIN-write toggling, pull-ups, external input levels, pin-change callbacks. |
| Timer0, Timer2 | Normal, CTC, fast PWM and phase-correct counting; TOV/OCFA/OCFB flags and interrupts; prescalers. |
| USART0 | Transmit buffer and shift register, exact frame timing from UBRR/U2X/format, UDRE/TXC/RXC interrupts, host receive queue. |
| Compile service | arduino-cli as a separate process; GCC diagnostics mapped to sketch lines. |
| Component models (spike) | TT gear motor (DC motor with gearbox and friction), L298N channel (drive, brake, coast), HC-SR04 with cycle-exact echo timing; the distance comes from the host's raycast. |
| Not yet (Phase 1) | Timer1, ADC, SPI, TWI (I2C), EEPROM, watchdog, external and pin-change interrupts, PWM output pins, bit-level UART pins, Optiboot start-up delay, ATmega2560. |

Measured on the development laptop (i5-12450H, .NET 10 JIT): 130–156 million cycles per second, 8–10 times real time. Target: 110 M cycles/s.

Build output goes to `core/.build/` (see `Directory.Build.props`), so Unity does not import `bin/` and `obj/` folders from the package folder.
