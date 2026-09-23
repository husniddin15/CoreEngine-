# ADR-0003 — Compile pipeline

Status: **Accepted** · Date: 2026-09-22 · Spec: [05 §8](../05-arduino-emulation-spec.md) · Facts: [research/R1 §3](../research/R1-avr-emulation-and-toolchain-facts.md)

## Context
Sketches must be compiled exactly as the Arduino IDE would (same core, same flags, same libraries) into `.hex` files for the emulator, offline, on Windows, from a closed-source application.

## Options
| Option | For | Against |
|---|---|---|
| **Bundle `arduino-cli` + `arduino:avr` core and call it as a subprocess** | Byte-identical builds to the IDE; offline; Arduino's licensing page explicitly permits "calling the CLI as a separate executable binary from another program"; libraries/boards managed by the CLI | ≈ 60–80 MB of tools in the install; GPL compliance duties (licence texts, source links); Windows path/AV edge cases |
| Ask users to install arduino-cli themselves (Shortcuit) | No bundling | Terrible onboarding for the primary persona; version drift |
| Server-side compilation (Wokwi, Velxio, Tinkercad) | Nothing to bundle | Needs servers and internet; offline play impossible; ongoing cost |
| Call avr-gcc directly with our own sketch preprocessing | No CLI process | Re-implements `.ino` concatenation/prototype generation, library resolution, board macros; still GPL toolchain; drift from the IDE |

## Decision
Bundle `arduino-cli` (v1.5.x, GPLv3) and a pre-installed `arduino:avr` core (1.8.8: avr-gcc 7.3.0-atmel3.6.1-arduino7, avr-libc 2.0.0) in `tools/` with a bundled `arduino-cli.yaml` whose data/user/downloads directories point into the app data folder, so compilation is fully offline. Invoke it as a separate process with `--format json`; parse diagnostics from the GCC output; load the resulting `.hex`. Ship licence texts and the source links/offer in `tools/licenses/` and in the About screen. Bundle a curated set of libraries (licence-checked); allow online library installation through the CLI when a network is available.

## Consequences
- Phase 0 spike must prove the pre-populated data directory works on a clean Windows machine, including non-Latin user paths, and measure warm compile time (target < 3 s).
- Watch `--build-path` + `--output-dir` behaviour (arduino-cli issue #2318); read artefacts from the build path if needed.
- Toolchain updates are a deliberate, tested step (core version pinned; changing it re-runs the golden corpus).
- An "advanced" setting lets power users point to their own arduino-cli/core for newer libraries.
