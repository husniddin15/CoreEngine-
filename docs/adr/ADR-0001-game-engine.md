# ADR-0001 — Game engine and language

Status: **Accepted** (owner decision 2026-09-23, [13 D1](../13-open-questions-and-risks.md); version updated 2026-09-23: Unity 6.6 now, Unity 6.7 LTS when released) · Date: 2026-09-22. The Phase 0 spikes still verify performance; a failed gate reopens this ADR.

## Context
We need a Windows 3D application with: rigid-body physics for small jointed robots, a tool-like UI (dockable panels, lists, inspectors, a code editor), procedural meshes (wires, CSG bodies), Steam integration, and a simulation core (AVR emulator + electrical solver) that must run at 1× real time. The team is one developer working with AI, on a small budget. Facts: [research/R3-engine-libraries-steam-facts.md](../research/R3-engine-libraries-steam-facts.md).

## Options
| Option | For | Against |
|---|---|---|
| **Unity 6 (C#)** | Largest tutorial/asset ecosystem; `ArticulationBody` for robots; PhysX; UI Toolkit for tool panels; Steamworks.NET (MIT, current); IL2CPP for release performance; Personal licence free below $200 k; Runtime Fee cancelled; splash optional | Proprietary; Mono in the editor is slower than CoreCLR; UI Toolkit still "alternative" for runtime in the manual; CoreCLR migration at 6.8 (Mono removed) |
| **Godot 4.7 (C#/.NET 8)** | Free/MIT; .NET 8 CoreCLR (fast C#); Jolt physics default; built-in CSG nodes backed by Manifold; excellent UI Control nodes for tools | Smaller 3D ecosystem/assets; GodotSteam has no official C# bindings; CSG nodes "for prototyping" with runtime stutter reports; fewer robotics precedents |
| **Unreal 5.8** | Best rendering; Chaos physics | C++/Blueprints only; heavy for a tool-like UI; iteration speed; 5 % royalty above $1 M |
| **Custom engine** | Full control | Months of infrastructure before any product work; not viable for this team |

## Decision
Unity 6 with C#. Development starts on **Unity 6.6** (installed: 6000.6.2f1) and moves to **Unity 6.7 LTS** as soon as it is released (expected at the end of 2026); the project then stays on 6.7 LTS through the 1.0 release. IL2CPP for release builds, URP, UI Toolkit for panels. The simulation core (`CoreEngine.Sim`) is a plain C# library with no engine types, targeting netstandard2.1 so Unity can consume it as a local package, and built and tested with the .NET 10 SDK (LTS; .NET 8 support ends in November 2026). This keeps Godot (also C#) as a realistic fallback: only the app layer would be rewritten.

Version choice: Unity 6.3 LTS is supported only until December 2027, before the planned 1.0 release (late 2028), so it would force an upgrade late in the project. Unity 6.6 is a Supported Update release: it gets fixes only until the next release, and features first introduced in 6.6 can be removed again in 6.7 LTS ([Unity 6 support](https://unity.com/releases/unity-6/support), [makaka.org](https://makaka.org/unity-tutorials/best-version)). Moving from 6.6 to the next LTS is a small step, so 6.6 is a good starting point as long as the project avoids 6.6-only experimental features.

## Consequences
- Track revenue for the Unity Personal threshold; budget Pro seats if exceeded.
- Upgrade once from 6.6 to 6.7 LTS when it ships, then stay on 6.7 LTS through the 1.0 release; plan the CoreCLR/6.8 migration afterwards (BinaryFormatter and stricter float semantics are already avoided).
- Steam via Steamworks.NET; Workshop via ISteamUGC.
- Performance-critical C# (emulator) is written in a Burst-friendly style (arrays, structs, no allocations) so it can be moved to a job or native code if needed.
