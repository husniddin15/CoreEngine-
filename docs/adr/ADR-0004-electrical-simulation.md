# ADR-0004 — Electrical simulation approach

Status: **Accepted** · Date: 2026-09-22 · Spec: [06-electrical-simulation-spec.md](../06-electrical-simulation-spec.md) · Facts: [research/R4 §1](../research/R4-electrical-physics-facts.md)

## Context
Pillar P2 needs nets, breadboard topology, voltages and currents, and realistic failure modes (LED burn-out, brown-out, fuse trips, driver voltage drop, battery sag), synchronized with a cycle-level MCU and a 100 Hz physics world, at ≤ 0.3 ms per 1 ms tick.

## Options
| Option | For | Against |
|---|---|---|
| Digital-only with behavioural parts (Wokwi) | Fast; simple | Cannot show voltage/current consequences; resistors do nothing (Wokwi's own limitation); no failure modes |
| Full SPICE via **ngspice** (BSD-licensed core) | Real analog fidelity (Velxio uses ngspice-in-WASM at ≈ 60 Hz) | Adaptive time stepping is hard to lock to a fixed 1 kHz/cycle-level schedule; convergence failures on user-made messes; large native dependency; transistor-level models of L298N/servos are overkill |
| Own SPICE-like transient solver (Falstad-style MNA with NR) | Fidelity | Falstad code is GPL (cannot reuse); writing a general transient solver is a project of its own |
| **Hybrid: event-driven digital nets + DC modified nodal analysis at 1 kHz + behavioural component models** | Deterministic, fast, fits the time model; failure modes come from behavioural limits (I²t, thermal RC); educational quantities (V, I, temperature) available; every analog element is a simple stamp | Not physically exact for fast transients (µs-scale ringing, inductive spikes); some models are curve-fits |

## Decision
Hybrid solver: digital nets resolved by driver-strength rules on pin events at cycle resolution; analog islands solved by DC MNA (dense LU, ≤ 60 nodes, piecewise-linear diodes with bounded Newton iterations, backward-Euler capacitors for decoupling) once per 1 ms tick and on demand when a stamp changes; behavioural models for motors, drivers, servos, batteries, regulators, sensors and displays; failure accumulators per component. Keep an `ISolver` abstraction so an ngspice-backed "advanced analog" mode could be added later without touching component behaviours.

## Consequences
- Everything players meet when building ordinary robots (Ohm's law, dividers, LED currents, H-bridge drops, sag, brown-out) is exact enough; RF/AC/EMI are out of scope and documented.
- Component models need datasheet parameters (doc 09) and validation circuits (doc 06 §9).
- No GPL solver code is used; ngspice (BSD) remains an option.
