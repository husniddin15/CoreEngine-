# 08 — Body Designer Specification ("Body Studio")

Status: DRAFT v0.1 · Pillar P4 · Decision: [ADR-0005](adr/ADR-0005-body-designer-and-csg.md)

---

## 1. Goals and philosophy

- Let a beginner make a usable chassis in **5 minutes** and an enthusiast make a printable, mount-accurate chassis in **30 minutes**.
- **Tinkercad-style**, not CAD: place solid primitives and "holes", resize with handles, align, group. Everything stays parametric and editable; the mesh is a derived result.
- **Real dimensions, real materials, real mounting.** Units are millimetres. A body has mass from volume × material density, a centre of mass, and mounting holes that match real part hole patterns.
- **Round trip with reality**: import STL/OBJ from any CAD; export STL for printing (and DXF for laser-cut plates in v1.x).
- Physics-friendly: colliders are compound primitives (fast, stable in PhysX); imported meshes get convex decomposition.

## 2. Objects

| Object | Description |
|---|---|
| **Body** | An assembly of rigid **Parts** connected by **Joints**. A robot usually has one main part (chassis) plus wheels and moving parts attached via motors/servos. |
| **Part** | A rigid group of **Shapes** (solids and holes) or an imported mesh, with a **Material**. Owns **Mount points** and **Attachment faces**. |
| **Shape** | Parametric primitive: box, cylinder, sphere, wedge, tube, cone, rounded box, plate-with-hole-grid (M3 grid), L-bracket, standoff, text emboss (v1.x). Each has position, rotation, size, radius parameters, and a **solid/hole** flag. |
| **Material** (physical) | PLA, PETG, ABS, acrylic 3 mm, plywood 3 mm, aluminium 1.5 mm sheet, cardboard, foam board. Provides density, friction and a "printability" estimate (filament grams, print time rough estimate). Always free. |
| **Finish** (visual) | How the part looks: a colour (free) or a premium finish such as carbon fibre, brushed aluminium, wood grain, anodised metal or tinted acrylic, plus decals and stickers placed on faces. A finish **never** changes density, friction or collision; it is stored separately from the physical material. Premium finishes come from customization packs ([12 §1](12-business-steam-legal.md)); STL export ignores finishes. |
| **Mount point** | Typed anchor on a part: `screw_m2`, `screw_m2_5`, `screw_m3`, `standoff_m2`, `standoff_m2_5`, `standoff_m3`, `tape_face`, `zip_tie`, `axle_3mm_d`, `servo_horn_sg90`, `servo_mount_sg90`, `tt_motor_bracket`, `n20_bracket`, `hc_sr04_bracket`, `caster_ball`, `breadboard` (header-pin insertion into a breadboard), `breadboard_rail`. Components declare which mount types they accept ([09](09-components-catalog.md)); the list is extended only by adding the type to both documents. |
| **Joint** | Created automatically when a motor/servo is mounted and a wheel/horn is attached: `axle` (revolute driven by a DC motor model), `servo` (position-controlled revolute), `caster` (free ball). Manual joints (free hinge, slider) are v1.x. |

## 3. Tools and interactions

| Tool | Behaviour |
|---|---|
| Place shape | Choose from the shelf; click on the workplane/face to place; default size preset. |
| Move / Rotate / Scale | Gizmos with snapping: grid 1 mm (0.5 mm with Ctrl), rotation 15° (5° with Ctrl), scale by numeric entry in the Inspector. Mirror X/Y/Z. |
| Workplane | Snap the workplane to any face (Tinkercad-style) to build on top or on the side of an existing shape. |
| Align | Align/distribute selected shapes on any axis (left/centre/right, min/mid/max). |
| Hole mode | Toggle any shape to a hole; holes subtract from solids in the same part when **Group** is applied. |
| Group / Ungroup | Group = boolean union of solids minus holes (Manifold). Result stays a group with editable children (non-destructive: we keep the shape tree and recompute). |
| Hole pattern helper | Drops a hole set that matches a real part: Uno (4 × Ø3.2 mm at the official coordinates), Mega, Nano (breadboard pitch), L298N module (4 × Ø3 mm, 37 × 43 mm pattern [VERIFY]), HC-SR04 bracket, TT motor (2 × M3 with 17.5 mm spacing [VERIFY]), N20 bracket, SG90 (2 × Ø2 mm, 28 mm spacing), 9 V/4×AA holder, caster wheel. Values live in the component JSON so they are always consistent with the parts. |
| Measure | Distance/angle between points; shows wheelbase, track width, ground clearance. |
| Mount point tool | Add a mount point of a given type to a face; auto-created by hole helpers. |
| Wheel helper | Choose wheel diameter/width; place on motor shafts; checks that wheels do not intersect the chassis and reports ground clearance. |
| Import mesh | STL (binary/ASCII) and OBJ; unit detection (mm assumed; warn if bounds suggest cm/inch and offer scaling); mesh repair (weld, fix normals; Manifold requires manifold input — non-manifold meshes are voxel-remeshed or rejected with a helpful message); triangle budget 200 k per part (decimate on import above that). |
| Export | STL binary per part or whole body; optional 0.2 mm hole tolerance offset for printing; BOM includes filament estimate. DXF (2D plates) v1.x. glTF for sharing v1.x. |
| Paint / finishes / decals | Colour per part (free). Premium finishes and decal sets from customization packs, with a "Try" preview before buying. Decals are projected onto faces, scaled and rotated like shapes. Custom decals from the player's own images: v1.x, local projects only, because Workshop sharing of user images needs moderation. |

Undo/redo covers every operation. Autosave of the body with the project.

### 3.1 The prototype Studio (2026-09-24)

**Body** and **Build** in the Garage both open the Body Studio full screen: Body on the library's Shapes tab, Build on its Parts tab. The owner asked for "something like Blender 3d or CAD" first, then (the same day) for Tinkercad itself, as in the rma_fullstack modeller: no chassis given, the player makes their own from shapes in real materials, places every part and turns it any way, but cannot resize a real part.

**A new robot is empty.** There is no default chassis. The player takes a plate or other shapes from the library, sets them down, and puts the parts on them (docs/03 §5.2).

**Layout**
- A toolbar: Garage, undo, redo, the snap step (1, 5 or 10 mm), Draw, Import, STL and Parts (shows or hides the parts).
- The library, with two tabs:
  - **Shapes**: Solid / Hole, the material (PLA, acrylic, plywood, cardboard, EVA foam, PVC foam board, aluminium) with its density, a colour, and the shapes: plate with holes, box, rounded box, cylinder, cone, sphere, wedge, tube.
  - **Parts**: every catalogue part with how many the robot may take and its mass (Uno, L298N, HC-SR04, TT motor, 4×AA holder, ball caster, SG90 servo, LED module).
- The 3D view with a grid on the workplane (the turntable's top).
- An inspector: the selected shape, group or part; or the list of shapes and parts.
- A status bar that says what the mouse does and shows the body's volume, the robot's mass and the build time.

**What works**

| Tool | In the prototype |
|---|---|
| Place | A click on a shape or part in the library puts it on the mouse; it rides over the robot and a click sets it down. Shapes land on the surface under the mouse; boards stand on a plate, motors and the caster hang under one. Esc puts it back. A perforated plate counts as flat under a part, as a real one does. |
| Handles (Tinkercad's) | The selected item shows its dashed footprint and stem. A shape has white squares at its base corners and dark squares at the middles of its edges that size it (the far side stays; Shift keeps proportions; Alt sizes from the middle), and a white square on top for its height (its base stays). A cone 50 px above lifts any item. Three curled arrows turn it about x, y and z, in 15° steps (Shift 45°, Ctrl 1°), with a protractor while dragging. Dimension lines show the sizes. The handles keep one size on the screen and have a light halo, so they read on dark and light scenes. |
| Real parts | A part keeps its real size: it has only the cone and the curls. It moves by a drag (sliding over surfaces, standing on them or hanging under them) and turns freely about all three axes. |
| Numbers | Position, rotation and size in the inspector (typed, applied with Enter); corner radius, tube wall, cone top, a plate's hole spacing and hole size. |
| Materials | Each shape has its own material and colour; the body's mass is the sum of each material's volume × density. The "as built" finish shows each shape in its material: grained plywood, cardboard, see-through acrylic, matte PLA, foam, brushed aluminium. |
| Groups | Ctrl+G groups the selection, Ctrl+Shift+G ungroups, Shift+click adds to the selection. A hole cuts only the solids of its own group; a hole on its own cuts nothing (Tinkercad's rule). Each solid keeps its own material in the group. |
| Draw | Click corners on the workplane (snapped); a click on the first corner or Enter closes the outline, which becomes a 10 mm solid or hole. A crossing outline is refused. |
| Import | Windows' Open dialog; STL (binary or ASCII) and OBJ. The file is copied into the robot's folder, a model under 2 units long is taken as metres (×1000), one over 400 mm gets a warning, and more than 200 000 triangles are refused. A model that is not closed is shown but cannot cut or join. |
| Edit | Duplicate (Ctrl+D), mirror copy (M), stand on the workplane (D), delete (Del), undo (Ctrl+Z) and redo (Ctrl+Y); arrow keys and PgUp/PgDn nudge. |
| Export | STL of the whole body. |

**Not yet:** the workplane on faces, align, the hole pattern helpers, measuring, mount points, decimation of large models, cm and inch prompts, a per-part STL, and several selected items sized together.

## 4. Physics derivation

- **Mass** = Σ(solid volume − hole volume) × density. Mass and centre of mass shown live; warnings when CoM is outside the wheel support polygon (tipping) or when ground clearance < 5 mm.
- **Colliders**: for parametric parts, each solid shape becomes a primitive collider (box/sphere/capsule; cylinders use convex meshes with 16–24 sides); holes are ignored for collision unless larger than 20 mm (then a convex decomposition of the group result is used). For imported meshes: V-HACD-style convex decomposition (≤ 16 hulls) with a fallback to a single convex hull.
- **Friction/bounciness** from the material; wheels use tyre material presets (rubber, plastic).
- Attached components add their own mass and colliders at their mount transforms.

## 5. Constraints and validation

- Parts must be manifold after grouping; if Manifold fails, keep the previous mesh and show the problem shape.
- Components may not interpenetrate the body or each other (soft warning; blocking only for the physics run if overlap > 2 mm).
- Wires must be long enough to reach; the wiring tool reports unreachable pins after body edits.
- Minimum wall thickness warning for printing (< 1.2 mm) and overhang hint (v1.x).

## 6. Presets (ship with v1)
- 2WD round chassis (Ø 150 mm plate, 2 TT motors, caster), 2WD rectangular acrylic-style, 4WD rectangular, mini N20 chassis (80 × 60 mm), pan-tilt bracket, gripper (servo-driven, two-finger), sumo wedge plate, line-follower plate with a 5-sensor bar.

## 7. Technical design

- **Geometry kernel**: Manifold (Apache-2.0) through its own C API (`manifoldc.dll`, built by `native/manifold`) and a thin C# P/Invoke layer: primitives, transforms, union/difference/intersection (batch forms for groups), `simplify` for decimation, status checks for non-manifold input, and mesh output with normals. Mesh repair for imports is C# code (weld, fix normals) before the mesh is handed to Manifold. Operations run on a worker thread; the UI shows a ghost until the result arrives (target < 100 ms for ≤ 50 shapes). Phase 0 measurements are in [ADR-0005](adr/ADR-0005-body-designer-and-csg.md): a chassis plate with 48 holes takes 34 ms single-threaded with 20-sided holes (63 ms with 32-sided), most of it in one boolean, so holes use Manifold's size-based segment count (20 sides for a 6 mm hole) and each part is recomputed only when it changes.
- First kernel (2026-09-24, the Garage prototype's Body mode): `BodyDesign` (shape rectangle, rounded or round; length, width, thickness, corner radius; one or two decks; an M3 hole grid with its spacing; side walls; acrylic, PLA or plywood) is built by `BodyBuilder`. A rounded outline is the hull of four corner cylinders; holes keep at least 3 mm of material to the edge and stay clear of the walls. Manifold runs on a worker thread, one build at a time under one lock (the main thread uses the same lock for thumbnails and the arena), and the model switches to the new plates when they are ready. Measured in the Mono player: a rounded 170 × 125 mm body with two decks and 2 × 77 holes builds in 75–100 ms, and dragging a slider for a second keeps 144 fps with no frame over 8 ms. **Export STL** writes binary STL in millimetres with z up, the plates side by side 10 mm apart; the file size matches 84 + 50 bytes per triangle. Wheels, motors, caster and sensor follow the body: the axle is 50 mm in front of the rear edge, a TT motor hangs 10 mm inside the plate edge and its wheel sits just outside it (on a round body, at the edge of the disc at the axle's line).
- Shapes on the chassis (2026-09-24, core and kernel; the Body Studio editor comes next). `BodyDesign.Features` holds a list of `BodyFeature`s.
  - **Kinds**: box, rounded box, cylinder, cone, sphere, wedge, tube, an extrusion of a sketched outline, and an imported mesh.
  - **Fields**: each feature is a solid or a hole, and has a position, rotation and size in millimetres in the chassis frame.
  - **Build**: `BodyBuilder` makes the plates, adds the solid shapes and cuts the holes in one Manifold pass on the worker thread. The arena weighs the result from Manifold's exact volume. The quick estimate in the core, used before any mesh exists, counts a hole at half its volume, because a hole only cuts where there is material.
  - **Import**: `MeshFile` in the core reads binary and ASCII STL (converted from z-up to y-up) and OBJ (polygons split into triangles, negative indices allowed), then welds duplicate vertices.
    - Imported files live in the robot's own folder (`Imports/<robot id>` in the player's data), so a project does not depend on where the original file was.
    - The mesh is scaled to fit the shape's box, so the size fields resize it like any other shape.
    - A mesh that is not a closed solid cannot join the booleans. It is shown as it is, gets a convex collider, and is reported (`NotClosed`) so the editor can ask the player to repair it.
  - Six core tests cover the feature model, the mirror copy and the readers (`BodyStudioTests`).
- The Studio's view (2026-09-24, `GarageStudio.cs`).
  - **Ghosts.** Every shape has a "ghost" that the mouse picks, built from the shape's own mesh (Manifold returns it with the body). A hole's ghost is drawn see-through grey, the selected shape's see-through blue. While a handle moves a shape, its ghost moves at once: its parent carries the new place, turn and size, and its child undoes the place and turn the mesh was built with. The body catches up when the worker's build arrives.
  - **Shaders.** Two small URP shaders (`Shaders/StudioOverlay`, `Shaders/StudioGrid`). The handles are drawn on top of everything; the ghosts get a small depth offset so they win over the body faces they share.
  - **Picking.** Handles are picked on the screen: squares and the cone by distance, curls along their line.
  - **Handles** (`GarageHandles.cs`, 2026-09-24): painted every frame over the 3D view with UI Toolkit's vector painter, at Tinkercad's sizes in screen pixels (from rma_fullstack's Tinkercad Parity notes): 13 px corner squares with a 2.5 px border, 8 px edge squares, the lift cone 50 px above the top, curls about 22 px round an edge. Dimension readings are small white labels. The benchmark drags every handle with the mouse on a box and checks the result (corner +10 mm, top +10 mm with the base kept, lift 15 mm, a quarter turn).
  - **Camera.** It uses an off-centre projection, like a shift lens, so the robot sits in the middle of the free area between the palette and the inspector. Since 2026-09-25 it has Tinkercad's navigation ([03 §3.1](03-game-design.md#31-the-garage-main-screen)): a view cube in the free area's top left corner (the inspector is on the right) with Home and Fit under it, right-drag to turn, Shift+drag or the middle button to move the pivot, the wheel toward the mouse, a double-click to turn round a spot, and a view from below the bench with the room left out. The robot stands on the bench's measuring mat: the turntable is only for the showroom.
  - **Plate cache.** The plates with their hole grid are kept between builds while only shapes change. That halved the rebuild of a body with 2 × 77 holes and five shapes, from about 220 ms to about 105 ms (Mono build), and dragging a shape keeps about 130–140 fps.
- A robot built from nothing (2026-09-24). `BodyDesign.Features` is the whole body; a `Plate` feature (a plate with an M3 hole grid, rectangle, rounded or round) replaced the fixed decks, and each feature has a `Material` (density from docs/09 §9) and a colour. A `Group` feature is a parent id on its members. `BodyBuilder` builds each top-level item: a lone shape as it is, a group as the union of its solids per material and colour minus its holes. Parts take a free pose (x, y, z and three Euler angles, Unity's order); each part knows its box, the point it mounts by and which way that faces (down for a board, up for a motor or the caster). Saves of version 2 and older are moved over by `DesignMigration`: the decks become plates on four aluminium standoffs, the parts keep their places, holes keep cutting.
- Shape tree stored in `body.json` (parametric). Derived meshes cached in the project as glb for fast load; regenerated when the kernel version changes.
- Rendering: one mesh per part with per-face colour groups; selection outline; hole shapes rendered translucent orange (Tinkercad convention; colour-blind alternative: hatch pattern).
- Import: our own STL and OBJ readers in the core (`MeshFile`, no dependency); glTFast (in the project since 2026-09-24 for the Garage lab) for glb caching and future sharing.
- Fallback if Manifold integration slips: MVP can ship **without boolean subtraction** (holes only affect mount points and rendering via cut-out decals) while unions remain simple overlapping colliders. Booleans are then a v1.1 feature. This is the documented plan B in ADR-0005.

## 8. Not in v1
Sketch/extrude, fillets/chamfers (v1.x), shell, text emboss (v1.x), lattice/organic shapes, DXF export (v1.x), multi-material parts, print-bed layout/slicing (recommend PrusaSlicer/Cura externally).
