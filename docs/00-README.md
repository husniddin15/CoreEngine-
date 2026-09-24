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
  native/      Manifold mesh booleans, built as one DLL (C++)
  tools/       arduino-cli + Arduino AVR core + licences; fetch-lab-assets.ps1 (Garage lab, CC0)
  content-src/ Blender sources, textures, datasheet Markdown
```

## Status and next steps (2026-09-24)
All major decisions are made ([13 §1](13-open-questions-and-risks.md)): Unity 6 (6.6 now, 6.7 LTS when released); name "CoreEngine"; a solo developer working with AI; a pure sandbox with a short tutorial and the Notebook; one full 1.0 release, free to play with low-priced paid packs (Mega 2560 first); English, Uzbek and Russian; payouts through a company in Uzbekistan.

1. Phase 0 is done (doc 11 §3): repository and CI, the Arduino toolchain, the emulator, the Unity robot spike, the Manifold spike, the UI spike with the code editor and three languages, and the decision review. The schedule was re-estimated to 15–18 months to 1.0. Next: Phase 1, the vertical slice.
   Phase 1 has started (doc 11 §4):
   - the Garage main screen ([ADR-0009](adr/ADR-0009-garage-main-screen.md)) with working Build, Wire and Body modes;
   - a photographed robotics lab (CC0 assets, D19) and the redesigned menu;
   - the core of the Body Studio (shapes, holes, STL/OBJ import).

   Next: the Body Studio editor.
2. Tools in use: .NET 10 SDK, Unity 6000.6.2f1 with Windows Build Support (IL2CPP), Visual Studio Build Tools 2026 with the C++ workload and CMake.
3. Open items: the name check (D2), the bank confirmation (D12), the purpose of email accounts (D18), a logic analyser if the Uno kit has none (D9), and compile time (13 §2 Q11).
