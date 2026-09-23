# ADR-0002 — Microcontroller emulation approach

Status: **Accepted** · Date: 2026-09-22 · Spec: [05-arduino-emulation-spec.md](../05-arduino-emulation-spec.md) · Facts: [research/R1](../research/R1-avr-emulation-and-toolchain-facts.md)

## Context
Pillar P1 requires that the user's sketch be compiled by the real toolchain and executed with the same logic and timing as a real ATmega328P. Options range from interpreting the C++ source (UnoArduSim style) to embedding an existing emulator.

## Options
| Option | For | Against |
|---|---|---|
| Source interpreter / API-level model (UnoArduSim, CRUMB's Nano, Tinkercad?) | Simple; no toolchain | Not the real program: no libraries with inline assembly, no real timing, no interrupts semantics; violates P1 |
| **simavr** (GPLv3) via separate process + IPC | Mature; many peripherals; GDB | GPL means it cannot be linked in; IPC adds latency and complexity at cycle-level pin exchange (millions of events/s); Windows build is unofficial/broken |
| QEMU AVR (GPLv2) | Mature CPU core | Only USART and 16-bit timers modelled; no GPIO/ADC/PWM; GPL |
| Port/translate **avr8js** (MIT, TypeScript, ≈ 5.4 k lines) to C# | Proven design used by Wokwi; MIT allows porting; test suite (≈ 350 cases) reusable | Needs care: SLEEP is a no-op, SPM missing, Mega timers 3–5/USART1–3 not in public exports, input capture missing on Mega; JS-oriented code style |
| Own C# implementation informed by avr8js and the datasheets | Full control; engine-free; designed for pre-decoding and lazy peripherals from the start | Effort (≈ 6–8 weeks for a complete 328P with tests) |
| New permissive cores (avrcore C99 MIT, arduboy-emu Rust) | Cycle-accurate claims | Immature (0 stars, partial peripherals); native interop overhead for pin events |

## Decision
Write our own cycle-accurate AVR core in C# inside `CoreEngine.Sim`, structured after avr8js (CPU + register-hooked peripherals + clock-event scheduler) and the ATmega328P/2560 datasheets. Where convenient, translate avr8js sections and reuse its test vectors under the MIT licence with attribution. Peripheral scope is defined in doc 05 §5 and exceeds avr8js where the game needs it (SLEEP, input capture, analog comparator tier 2). simavr is used only as a differential-testing oracle in CI (Linux container), never shipped.

Performance design: pre-decoded instruction table, lazy peripheral catch-up with next-event cycles, no allocations in the run loop; gate ≥ 110 M cycles/s under IL2CPP in Phase 0. **Plan B** if the gate fails: port the CPU core to C++ behind the same `ICpuCore` interface (the peripherals and pin model stay in C#).

## Consequences
- We own correctness: a large golden-sketch corpus and hardware measurements are mandatory ([05 §10](../05-arduino-emulation-spec.md)).
- The ISA/peripheral split via `ICpuCore` + device profiles makes Mega 2560 an incremental addition and leaves room for non-AVR cores later.
- No GPL code in the shipped binary; credits list avr8js if any test data or translated code is used.
