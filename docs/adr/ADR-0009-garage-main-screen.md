# ADR-0009 — The Garage as the main screen

Status: **Accepted** 2026-09-23 (owner recommendation) · Design: [03 §3.1](../03-game-design.md) · Related: [ADR-0007](ADR-0007-monetization-free-to-play-dlc.md) (free to play with packs), [ADR-0008](ADR-0008-pure-sandbox-full-release.md) (pure sandbox)

## Context
The owner asked for a main screen "where everything the player can do can be found", like the hangar in War Thunder: the player sees their tank or plane, customizes it, repairs it, and starts a battle from one place. The design so far had spaces (Workbench, Body Studio, Code Desk, Arena, Notebook) and mode tabs, but no home screen that shows the player's robots.

## Options
| Option | For | Against |
|---|---|---|
| Classic menu with a project list (New, Open, Settings) | Simple to build | Feels like a tool, not a game; robots stay invisible until opened; the shop and customization are hidden |
| Straight into the workbench with the last project | Fast for returning players | No overview of all robots; no natural place for customization, repair or the shop |
| **Garage hub** (War Thunder's hangar pattern) | Every action in one place around the robot; the robot is the hero; a natural home for customization packs and "Try"; repair and readiness are visible before a test | One more screen to build and translate; needs thumbnails and room art |

## Decision
The game opens in the **Garage**:
- The selected robot stands on a turntable in the stylized room ([13 D11](../13-open-questions-and-risks.md)); the player orbits and zooms around it.
- The **robot bar** along the bottom holds all of the player's robots and **+ New robot**.
- The **robot card** on the left shows the facts: board, sketch and whether it is compiled, number of parts, mass, battery charge, and readiness warnings.
- The **action column** on the right opens every editor: **Build**, **Wire**, **Code**, **Body**, **Customize**, **Check & repair**.
- The **top bar** has Notebook, Shop, Workshop, language and settings, plus the arena picker and the big **START** button. START runs the robot in the chosen arena; a **Garage** button brings it back, with its battery and any damage.

Taken from War Thunder: the hub around the vehicle, the vehicle bar, customization and repair next to the vehicle, and one big start button.
Not taken, because they conflict with ADR-0007 and ADR-0008: research trees, unlocks, currencies, crews, paid or timed repairs, and battle rewards. **Repair is always free and instant**, and every repair links to its "why it broke" card ([10 §4.3](../10-content-arenas-tutorial-notebook.md)).

## Consequences
- The robot (project) is what the player owns and sees. The save format holds a list of robots with their parts, sketch, finishes, battery charge and damage; thumbnails are rendered from the 3D model.
- The Garage room is the stylized room of D11; its art is part of the Phase 1–2 art work.
- Customization packs (ADR-0007) live in the Customize panel with lock badges and "Try": a tried finish shows everywhere, including in arenas, but only owned finishes are saved.
- Warnings never block START ([03 §6.3](../03-game-design.md)); the robot card lists them.
- The shipped game starts with an empty robot bar ([13 D16](../13-open-questions-and-risks.md): no example robots); the tutorial creates the first robot.
- Roadmap: Garage v1 in Phase 1 (≈ 6 days), thumbnails and room art in Phase 2 ([11](../11-roadmap.md)).
- Prototype: `app/Assets/Spike/Garage/` (2026-09-23).
