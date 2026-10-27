# Protobot Rebuilt: mechanics review and plan

What the original Protobot Rebuilt (GPL-3.0, Unity 2021, by BreadSoup, continuing davegersh's Protobot) does,
including the code behind the scenes, and for each mechanic: will people use it, should we have it, and how we
can do it better in a first-person workshop. Written from four seats: developer, mentor, playtester and VEX
community member. Nothing here is copied code; it's a list of ideas to rebuild our own way.

Verdicts: **Adopt** (build it, roughly as is), **Improve** (build our own, better version), **Later** (worth it,
not yet), **Skip** (not worth it here).

## 1. How the original is put together

- A CAD-style editor: orbit camera around a pivot, click to select, drag gizmos (axis arrows, plane, rotate
  ring), sidebar of part generators, context menus.
- **1 Unity unit = 1 inch** (we use metres for VR).
- Parts are **generated from parameters**, not imported per size: C-channel = profile (1x2, 1x3, 1x5) x hole
  count, angle, rail, U-channel, plate = length x width holes (combined from a one-hole tile), shaft = scaled
  1-inch mesh (Normal up to 12", HS up to 24").
- Each part prefab carries a little text script that lists its holes (`shape=square size=.182 depth=.04
  spacing=.5 axis=y type=normal hole=(-8>8, 0, 0)`); `HoleGenerator` turns it into one trigger collider per hole
  on a "HoleCollisions" layer.
- Building = **placement rules** checked in priority order every frame while placing (`PlaceDisplacement`
  subclasses), then **connections** discovered by trigger capsules on screws and shafts (`HoleDetector`),
  stored in a graph (`GroupManager`) so connected parts move together.
- Undo/redo (`StateSystem`), JSON saves locally or to Firebase with accounts, parts list with weight.

## 2. Every mechanic, with a verdict

### Parts and part data

| Mechanic (original) | Will people use it? | Verdict | Our way |
|---|---|---|---|
| Parametric generators (C-channel by hole count, plate L x W, shaft by length) | Yes, constantly: "give me a 1x2x1x25" | **Improve** | We import full-length stock and cut it with the saw, which is our signature mechanic. Add a *pre-cut* option in the part picker (choose hole count, it spawns already cut, with the cut recorded) so the saw is for custom cuts, not chores. |
| Hole data written by hand per part (text DSL) | Invisible to players, but it decides what can attach | **Improve** | We have ~600 real VEX-CAD models; writing hole data by hand is hopeless. We scan each model's mesh for holes at library build time (depth-map scan from six directions), so every part, even motors and electronics, gets real holes automatically. |
| Hole types: Normal / Threaded / Clamp | Yes (decides what a screw or shaft does) | **Adopt + extend** | Kinds: clearance (square or round), threaded (nuts, standoff ends, motor cap holes, inserts), shaft-lock (square bores of gears, wheels, collars, motor output), bearing (round hole a shaft spins in). |
| `HoleFace` (shows a hole's outline where you hover) | Yes, it's how you aim at a hole | **Adopt** | Hole highlight ring in the world at the hole you aim at. |
| `HoleDetector` (trigger capsule along a screw or shaft collects the holes it passes) | Invisible | **Improve** | We compute the stack along the fastener's axis from the scanned hole data (no physics triggers, deterministic, works for saving/undo). |
| Bearing inserts auto-shown when a round shaft passes an HS square hole | Small but loved detail | **Later** | Bearings as real parts that snap into holes; suggest one when a shaft goes through bare metal. |
| `PartName` + weight tables (grams per hole) | Yes, teams weigh robots | **Improve** | Our mass is mesh volume x density, so it stays right after cuts and drilling. Show pounds and ounces in the parts list. |
| 4-letter part ids (SCRW, NUT, SNDF, SPCR, BRNG, WSHR, MOTR, PLSI...) used by placement rules | Invisible | **Adopt (as roles)** | Part *roles* from the library path: screw, nut, standoff, spacer, washer, bearing, collar, shaft, gear, wheel, motor, structure. |
| Motor shaft offset table | Invisible | **Adopt** | Motor output = shaft-lock hole; shafts snap into it. |

### Placing and building

| Mechanic (original) | Will people use it? | Verdict | Our way |
|---|---|---|---|
| Hole-to-hole placing (`HolePlaceDisplacement`, `MoveHoleTool`): pick a hole on the part you move, click a hole on another part, it jumps there | This *is* building | **Improve** | First person: grab a part by the hole you want to attach (that hole becomes its anchor, Q/E pick another), aim at a hole on another part: the part snaps with the holes lined up and faces flush. Wheel turns it about the hole in 45 degree steps (Shift: 15), right click flips it over, click places it. |
| Screw placing into a hole face (`ScrewPlaceDisplacement`) | Every build uses dozens | **Adopt** | Hold a screw, aim at a hole: it seats with the head on the surface. Click to drive it in. |
| Standoffs, spacers, washers, bearings stacking on a hole or on a screw's end (`StandoffPlaceDisplacement`) | Yes | **Adopt** | Same snap: onto a hole face, or onto the end of a screw that sticks out, stacking. |
| Nut onto the end of a screw (`NutPlaceDisplacement`) | Yes | **Adopt** | Hold a nut, aim at a screw tip or at the back of a screwed hole. |
| Wheels and gears slid along a shaft, snapping to holes, with a flip key (`ShaftAxisPlaceDisplacement`) | Yes, every drivetrain | **Adopt (phase 2)** | Slide along the shaft with the aim; snap to bearings/collars; spacers fill gaps. |
| Inserts into HS square holes | Niche | **Later** | |
| Placement rotation with 22.5 degree snap, rotate ring with optional 5 degree snap | Yes | **Adopt** | Already have rotate mode (R); add snapping steps there too. |
| Axis arrows, plane drag, typed exact distance (`PositionAxisExact`) | Yes for precision, and you asked for exact offsets and angles | **Improve** | In rotate mode and while snapped: type an offset along an axis in inches (fractions OK), or an angle. Arrow keys nudge by one hole (1/2"). |
| Move shaft tools (move a shaft and everything on it, arrows at both ends) | Yes once shafts exist | **Later** | |
| Connected groups (`GroupManager` graph): screw + threaded hole clamps everything between head and nut; shaft with two clamp holes joins what's between; moving one moves all | Essential | **Improve** | Our groups become *real physics*: clamped parts merge into one rigid body (an assembly), pivots become joints. Same graph data, so saves, undo and later simulation all use it. |
| `Pivot` (temporary parent to move several objects around their bounds centre) | Invisible | **Skip** | Assemblies are rigid bodies; we move those. |
| Duplicate while moving (hold a key, the connected group is cloned) | Yes, a lot (drive sides, towers) | **Adopt** | Duplicate held part or assembly. |
| Mirror tool | Asked for on the VEX Forum for drivetrains | **Adopt (later)** | Mirror an assembly across a plane; swaps left/right parts where VEX has mirrored versions. |
| Delete | Yes | **Have** | Scrap bin (fun, physical); add a Delete key for big assemblies later. |
| Click, add, box and select-all selection, outline on selected | CAD habit; less needed in first person | **Later** | Multi-select becomes "grab assembly"; box select in an orbit/inspect view. |

### Editor and app

| Mechanic (original) | Will people use it? | Verdict | Our way |
|---|---|---|---|
| Undo/redo of every change (`StateSystem`) | Absolutely; without it nobody trusts a CAD tool | **Adopt (high priority)** | Command-based undo for spawn, move, cut, drill, paint, fasten, delete. |
| Save/load builds as JSON (part id, position, rotation, colour, states, camera, version) + Firebase cloud saves with accounts | Yes / cloud later | **Adopt + improve** | Local JSON in persistentDataPath with everything that changes a part (cuts, holes, paint per material) and the connection graph. Cloud and accounts only with multiplayer. |
| Unsaved-changes prompt | Yes | **Adopt** | |
| Parts list (BOM) export with weights, live total weight | Yes, for ordering and the engineering notebook | **Adopt** | Parts list panel, total weight, CSV/text export; plus a plastic-legality check (see polycarbonate below). |
| Orbit camera, focus on point/object, ortho/perspective switch | Yes for precise work and screenshots | **Improve** | We're first person, which is the fun part, but precise building needs a second view: an *inspect* view that orbits the assembly on a build table (also good for VR later as "pick it up and turn it"). |
| Per-part colour tool | Yes | **Have (better)** | Ours paints per material with a live preview. |
| Key rebinding, quality settings, tooltips, context menus | Yes | **Have / Later** | Settings and tooltips exist; rebinding later. |
| Measurement tool (they used an editor add-on) | People want distances and angles | **Adopt** | A tape measure tool: click two points or two holes, shows inches and holes. |
| Firebase auth and user database | Not until online play | **Skip for now** | Privacy, cost and moderation; revisit with multiplayer. |
| Game elements (Push Back blocks, older seasons) | Yes, asked for on the forum | **Later** | Needed for the driving/scrimmage goal. |

## 3. What the community asks for (VEX Forum)

- From Protobot Rebuilt threads: export the robot, an "animate" mode where mechanisms move, drive the robot
  with a controller, test autonomous separately, program in the languages teams use (blocks, C++, Python),
  import models from other CAD, a mirror tool, game elements, Mac support.
- CAD threads: Inventor, Fusion, Onshape, SolidWorks and Protobot are what people use; Protobot is recommended
  "if you want to make a robot fast"; Fusion is called hard to set up for VEX. Most teams use CAD for the
  engineering notebook (renders) more than for design.
- Screws: Keps nuts clamp (teeth bite the metal); Nylock nuts are for pivots because they lock on the thread,
  not by pressure; shoulder screws make low-slop pivots that fit square holes; bearing flats let shafts and
  shoulder screws spin smoothly; washers shim joints.
- What people modify: slip gears for catapults and punchers (teeth cut off a 36T or 60T gear, often about a
  third), flex wheels cut into rings, bearing flats sanded to fit inside C-channel, and custom plastic parts
  (polycarbonate, Delrin) cut by hand or laser from CAD drawings.
- Plastic rules (check the current manual each season): older seasons allowed all plastic from one 12" x 24"
  sheet up to 0.070" thick; Push Back limits each polycarbonate piece to an 8" x 4" x 0.070" footprint;
  inspectors are given 1:1 drawings; shattering plastics (acrylic) and 3D printed parts are not allowed.

## 4. Decisions: what we build, in order

**Phase 1: put parts together** (done: 7,122 holes found on 492 parts; grab by a hole, hole grab mode (H),
snap, tack, turn a tacked part on its hole, screw, nut, permanent fastened assemblies, held parts pass through
everything, build view (B) to get close; still to do: snapping parts by their side faces, standoffs/spacers
stacking on screw ends, the assembly's own name in the tooltip)
1. Holes on every part, found automatically from the models (`HoleScanner`, `PartHoles`), updated by cuts and
   the drill.
2. Aim at a hole: it highlights. Grab a part by a hole and bring it to another part's hole: it snaps, flush,
   holes lined up; wheel turns it about the hole, right click flips it, Q/E change the anchor hole, click
   places it (it stays put, "tacked", until fastened).
3. Screws snap into holes and drive in; nuts thread onto screw ends; a screw ending in a threaded hole
   (standoff, motor) needs no nut. A fastened stack becomes one rigid assembly.

**Phase 2: things that move**
4. Pivots: shoulder screws and loosened Nylock joints become hinge joints.
5. Shafts through holes; collars, gears and wheels lock onto shafts; bearings; shaft spins in its bearings.
6. Motors drive shafts (simulation goal: gears mesh, then drive the robot).

**Phase 3: trust and output** (undo/redo, save/load with connections and autosave done, ahead of phase 2,
as asked; they're state snapshots rather than commands, so every tool is covered by one `Record` call)
7. Undo/redo, save/load with connections, parts list with weight and plastic check, tape measure, duplicate,
   mirror, inspect view, exact offsets and angles between parts.

**Phase 4: the things CAD tools are used for**
8. **Custom polycarbonate**: take a 0.070" sheet (or 0.032"/0.060"), sketch the outline on it in first person
   (lines, arcs, circles snapped to the 1/2" hole grid and existing parts), cut it out, drill holes, mark a bend
   line and heat-bend it to an angle. Show the season's legality check (footprint, thickness) and export a
   1:1 SVG/PDF drawing for cutting and for inspection. This is the feature that can pull people away from
   Fusion/Inventor for VEX work.
9. Slip gears (remove teeth with a file/saw on gears), cut flex wheels, sanding.
10. Constraints between parts: angle between two parts, offset along a hole axis, typed in.

**Phase 5: play**
11. Drive the robot (controller), game elements, field, scrimmage; programs (blocks, then C++/Python);
    friends online. Keep every system above simulation-friendly: assemblies and joints from one connection graph,
    masses from geometry, part roles known.

## 5. How we beat it

- It's a place, not a form: you pick metal from bins, cut it with a saw, drill it, paint it. Every action makes
  a sound and shows something.
- Real parts: ~600 VEX-CAD models with real holes found automatically, real masses.
- Building gives physics, not just grouping: the robot you assemble is a rigid body with joints, ready to move.
- Custom plastic parts with a legality check and printable drawings: the gap between "fast builder" and "real
  CAD".

## Sources

- Original code: https://github.com/BreadSoup/Protobot-Rebuilt (GPL-3.0)
- [Protobot Rebuilt forum thread](https://www.vexforum.com/t/protobot-rebuilt/126101?page=2),
  [1.3.6 release](https://www.vexforum.com/t/protobot-rebuilt-1-3-6-released-with-push-back-elements/137417),
  [Protobot beta 1.3](https://www.vexforum.com/t/protobot-beta-1-3-is-now-available/100140)
- [Best CAD software? (VEX Forum)](https://www.vexforum.com/t/best-cad-software/99789),
  [Best free CAD for VEX IQ?](https://www.vexforum.com/t/best-free-cad-software-for-vex-iq/116029)
- [Using V5 Fasteners (VEX KB)](https://kb.vex.com/hc/en-us/articles/360035952791-Using-V5-Fasteners),
  [1 Post Hex Nut Retainer with Bearing Flat (VEX KB)](https://kb.vex.com/hc/en-us/articles/360035590952-How-to-Use-the-1-Post-Hex-Nut-Retainer-with-a-Bearing-Flat),
  [Frictionless joints](https://www.vexforum.com/t/frictionless-joints/17442)
- [Catapult slip gears](https://www.vexforum.com/t/catapult-bots-how-did-you-do-the-slip-gear/110259),
  [Cutting flex wheels](https://www.vexforum.com/t/is-it-allowed-to-cut-flex-wheels-and-attach-them-to-claws-for-better-grip/115225),
  [Custom white plastic parts](https://www.vexforum.com/t/how-to-design-these-white-custo-parts/138989),
  [Low-profile bearing flats](https://www.vexforum.com/t/new-products-may-2022-exp-products-for-v5/102860)
- Plastic rules: [Over Under Q&A 1860](https://events.vex.com/V5RC/2023-2024/QA/1860),
  [Push Back Q&A 2802](https://events.vex.com/V5RC/2025-2026/QA/2802),
  [Push Back R25](https://events.vex.com/storage/game_manual/VEX_V5_Robotics_Competition_2025-2026_Push_Back/rules/R25.html)
