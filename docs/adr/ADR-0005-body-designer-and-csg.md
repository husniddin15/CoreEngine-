# ADR-0005 — Body designer approach and CSG library

Status: **Proposed** (confirm after the Phase 0 Manifold spike) · Date: 2026-09-22 · Spec: [08-body-designer-spec.md](../08-body-designer-spec.md) · Facts: [research/R3 §4](../research/R3-engine-libraries-steam-facts.md)

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
Tinkercad-style parametric shapes with solid/hole semantics; booleans via Manifold through a thin C wrapper (`native/manifold_csg`) and our own P/Invoke; STL via pb_Stl (MIT) and OBJ via a small reader; glTF caching via glTFast (Apache-2.0). Colliders are compound primitives for parametric parts and convex decompositions for imported meshes.

**Plan B** (if the native integration or IL2CPP build proves troublesome in Phase 0): ship the MVP with presets + STL import + primitives without boolean subtraction (holes affect mount points and decals only) and add booleans in v1.1.

## Consequences
- One native DLL to build and sign (CMake/MSVC), plus 64-bit-only support (fine for Windows x64).
- Mesh limits (200 k triangles per part) and async CSG to keep the UI responsive.
- Attribution for Manifold, pb_Stl, glTFast in the credits.
