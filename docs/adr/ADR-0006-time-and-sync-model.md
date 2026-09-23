# ADR-0006 — Time and synchronization model

Status: **Accepted** · Date: 2026-09-22 · Detail: [04 §4–5](../04-technical-design.md), [05 §7](../05-arduino-emulation-spec.md), [07 §7](../07-physics-world-sensors-spec.md)

## Context
Three subsystems run at different natural rates: the MCU (16 MHz, cycle-level pin events), the electrical/behavioural layer (motor currents, sensor timers, battery sag: kHz-scale), and rigid-body physics (100 Hz is ample for 20 cm robots at ≤ 1 m/s). Users need pause, single-step, time scaling and deterministic replays; development needs reproducible regression tests.

## Options
| Option | For | Against |
|---|---|---|
| Free-running MCU thread synchronized to wall clock (Wokwi-style), physics on its own clock | Smooth real-time feel | Non-deterministic coupling; replays and tests unreliable; pause/step semantics messy |
| Physics at 1 kHz to match the electrical tick | Fewer rates to reason about | 10× physics cost for no visible gain at robot scale |
| **Lockstep fixed steps: physics 100 Hz → electrical/behaviour 1 kHz → MCU 16 000 cycles per electrical tick, with cycle-stamped device events inside a tick** | Deterministic; simple mental model; pause/step/time-scale trivial; sensor edges still cycle-accurate | Fast-forward bounded by CPU; physics-derived sensor inputs can be up to 10 ms stale |

## Decision
Lockstep fixed steps as above. Within a 1 ms electrical tick the MCU runs 16 000 cycles; pin changes are applied to nets at their exact cycle; devices that need sub-millisecond timing (UART bits, HC-SR04 echo, servo pulses, WS2812) schedule cycle-stamped callbacks. Physics-derived quantities (distances, wheel speeds, pose) are sampled at the start of each 10 ms physics step and treated as constant inside it. No subsystem reads the wall clock; randomness is seeded per run.

## Consequences
- Determinism enables replays, Workshop "ghost" runs and headless regression tests.
- The 10 ms staleness is far below every sensor's own measurement cycle (HC-SR04 ≥ 60 ms, line sensors sampled every few ms by user code, IMU 100 Hz–1 kHz) and below human perception; documented in doc 07.
- Real-time factor is displayed; on slow machines the sim slows down instead of skipping steps.
- Moving the core to a worker thread later keeps the same semantics (one-step-ahead pipeline).
