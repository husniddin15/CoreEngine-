# ADR-0005 — Body designer approach and CSG library

Status: **Accepted** 2026-09-23 after the Phase 0.5 spike (proposed 2026-09-22) · Spec: [08-body-designer-spec.md](../08-body-designer-spec.md) · Facts: [research/R3 §4](../research/R3-engine-libraries-steam-facts.md)

## Context
Pillar P4 needs an in-app body designer good enough to produce printable chassis with correct mounting holes, plus STL/OBJ import and STL export, with physics-friendly colliders.

## Options
| Option | For | Against |
|---|---|---|
| Block/voxel builder (Scrap Mechanic style) | Very easy; trivially stable colliders | Blocky bodies are not printable-realistic; poor fit for real hole patterns |
| **Tinkercad-style primitives + booleans (Manifold, Apache-2.0)** | Familiar to the target audience (Tinkercad has 100 M+ users); parametric; robust guaranteed-manifold booleans; used by OpenSCAD, Blender, Godot | Needs a native plugin and own C# P/Invoke (the ManifoldNET NuGet is alpha and lags) |
| csg.cs / pb_CSG (MIT, BSP booleans, pure C#) | No native code | Not robust on coincident faces; non-manifold output breaks printing |
| RealtimeCSG / Chisel | Level-design tools | No runtime API / not production-ready |
| CAD-style sketch/extrude | Powerful | Too complex for v1; users can import from real CAD instead |

## Decision
Tinkercad-style parametric shapes with solid/hole semantics; booleans via Manifold through its own C API (`manifoldc`, built by `native/manifold` as one DLL) and our own P/Invoke; STL via pb_Stl (MIT) and OBJ via a small reader; glTF caching via glTFast (Apache-2.0). Colliders are compound primitives for parametric parts and convex decompositions for imported meshes.

**Plan B** (if the native integration or IL2CPP build proves troublesome in Phase 0): ship the MVP with presets + STL import + primitives without boolean subtraction (holes affect mount points and decals only) and add booleans in v1.1.

## Phase 0.5 spike results (2026-09-23)
Manifold v3.5.3 in the Unity 6.6 IL2CPP player on the development laptop (i5-12450H), single-threaded, main thread ([app/Assets/Spike/Scripts/CsgSpike.cs](../../app/Assets/Spike/Scripts/CsgSpike.cs)):

| Case | Median time | Result check |
|---|---|---|
| Two boxes united, minus a cylinder hole (the exit criterion, < 50 ms) | **1.6 ms** | 176 triangles; volume exact; genus 1 |
| Chassis plate + 2 walls − 48 holes (51 shapes, docs/08 target < 100 ms), 20-sided holes (Manifold's default for r = 3 mm) | **34 ms** | 4 078 triangles; volume exact; genus 48 |
| Same with 32-sided holes | **63 ms** | 6 382 triangles; volume exact; genus 48 |

- **Build**: Manifold's upstream C bindings cover everything the Body Studio needs, so no own C++ wrapper. `native/manifold/CMakeLists.txt` builds the core as a static library into **one** `manifoldc.dll` (1.2 MB) with the static MSVC runtime; it imports only `KERNEL32.dll`, like the Unity player itself, so players need no Visual C++ Redistributable. Loading the DLL and the first call cost about 90 ms once.
- **IL2CPP**: P/Invoke with blittable arrays works unchanged; no marshalling code needed.
- **Unity mesh**: Manifold's triangles and normals are used as they are; winding and normals render correctly in Unity's left-handed frame (checked in a screenshot). Converting to a Unity mesh takes < 0.5 ms.
- **Where the time goes**: in the 48-hole case about 75 % is the single boolean (body minus the union of holes) and about 20 % is Manifold's normal calculation. Cost grows faster than the hole count (12 holes ≈ 30 ms, 192 holes ≈ 470 ms in a console test), and times vary ±30 % between runs on this laptop.
- **Consequences for the Body Studio**: run booleans on a worker thread with the ghost preview (docs/08 §7); use Manifold's size-based segment count by default (12 sides for an M3 hole, 20 for 6 mm); recompute only the part that changed; if large hole grids feel slow in playtests, compute normals in C# and try Manifold's parallel backend (needs oneTBB, Apache-2.0).

## Consequences
- One native DLL to build and sign (CMake/MSVC), plus 64-bit-only support (fine for Windows x64).
- Mesh limits (200 k triangles per part) and async CSG to keep the UI responsive.
- Attribution for Manifold, pb_Stl, glTFast in the credits.
