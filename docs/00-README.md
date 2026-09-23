# CoreEngine — Project Documentation

A Windows 3D STEAM-education robotics game: design a robot body, place real electronic parts, wire them like on a real breadboard, write real Arduino code that is compiled by the real toolchain and executed by a cycle-accurate ATmega emulator, and test the robot in a physics arena. Free to play on Steam with paid packs (premium boards, real parts, customization), plus school licences.

Start with [01-vision-and-scope.md](01-vision-and-scope.md). Decisions the owner still has to make are in [13-open-questions-and-risks.md](13-open-questions-and-risks.md).

## Document map

| # | Document | What it answers |
|---|---|---|
| 01 | [Vision and scope](01-vision-and-scope.md) | What we are building, for whom, pillars, goals/non-goals, success criteria |
| 02 | [Market research](02-market-research.md) | Competitors, gaps, pricing (data from 2026-09-22) |
| 03 | [Game design](03-game-design.md) | Sandbox, spaces, core loop, build/wire/code/test UX, economy, UI, art, audio, failure design |
| 04 | [Technical design](04-technical-design.md) | Architecture, modules, time model, data formats, compile pipeline, rendering, UI tech, testing, CI |
| 05 | [Arduino emulation spec](05-arduino-emulation-spec.md) | AVR core, peripherals, board models, compile pipeline, verification corpus |
| 06 | [Electrical simulation spec](06-electrical-simulation-spec.md) | Nets, breadboards, solver, power model, component electrical models, failure/damage model |
| 07 | [Physics, world and sensors spec](07-physics-world-sensors-spec.md) | Physics setup, actuators, sensors, arenas, determinism, interfaces |
| 08 | [Body designer spec](08-body-designer-spec.md) | Primitives, booleans, mounting, import/export, colliders |
| 09 | [Components catalogue](09-components-catalog.md) | Every part with real specs, tiers, model parameters, footprints |
| 10 | [Content: arenas, tutorial, Notebook](10-content-arenas-tutorial-notebook.md) | Arenas, the 5–10 minute tutorial, Notebook cards, localization (EN/UZ/RU) |
| 11 | [Roadmap](11-roadmap.md) | Phases, work packages, estimates for a solo developer with AI |
| 12 | [Business, Steam, legal](12-business-steam-legal.md) | Free-to-play model and paid packs, Steam publishing facts, payouts from Uzbekistan, licences, trademarks, costs |
| 13 | [Open questions and risks](13-open-questions-and-risks.md) | Owner decisions with recommendations, Phase 0 questions, risk register |
| 14 | [Glossary](14-glossary.md) | Terms |
| ADR | [adr/](adr/) | Architecture Decision Records 0001–0008 (engine, emulation, compile pipeline, electrical solver, CSG, time model, monetization, pure sandbox) |
| R | [research/](research/) | Sourced fact sheets behind the docs (emulation/toolchain, competitors, engine/Steam, electrical/physics) |
| Archive | [archive/](archive/) | Superseded designs kept for reference (the mission campaign v0.1) |

## Conventions
- Each document carries a `Status` line (DRAFT / REVIEWED / ACCEPTED) and a date. Facts that are not verified against a primary source are marked **[VERIFY]**.
- Decisions with lasting consequences get an ADR; documents reference the ADR instead of repeating the argument. Changing a decision means a new ADR that supersedes the old one.
- Units: millimetres for parts and bodies, metres in physics, grams for mass, volts/amps/ohms as usual. Real part numbers are used as-is.
- Cross-links are relative so the docs work on GitHub and in any Markdown viewer.
- Trademark rule: "Arduino" is never part of the product name; use "compatible with Arduino® boards" wording (doc 12 §4).

## How the docs relate to the code (planned layout)
```
CoreEngine/
  docs/        this documentation
  core/        CoreEngine.Sim (C#, engine-free), tests, headless CLI
  app/         Unity 6 project
  native/      Manifold CSG wrapper (C++)
  tools/       arduino-cli + Arduino AVR core + licences
  content-src/ Blender sources, textures, datasheet Markdown
```

## Status and next steps (2026-09-23)
All major decisions are made ([13 §1](13-open-questions-and-risks.md)): Unity 6 (6.6 now, 6.7 LTS when released); name "CoreEngine"; a solo developer working with AI; a pure sandbox with a short tutorial and the Notebook; one full 1.0 release, free to play with low-priced paid packs (Mega 2560 first); English, Uzbek and Russian; payouts through a company in Uzbekistan.

1. Tools (doc 04 §19): install the .NET 10 SDK (not installed yet); add the Windows Build Support (IL2CPP) module to the installed Unity 6.6; install Visual Studio Community with the Unity and C++ workloads.
2. Start Phase 0 (doc 11 §3): repository and CI, the emulator core spike, the toolchain packaging test.
3. Open items: the name check (D2), the bank confirmation (D12), the purpose of email accounts (D18), and a logic analyser if the Uno kit has none (D9).
