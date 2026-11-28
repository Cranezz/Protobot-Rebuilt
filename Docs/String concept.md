# String and rope: the plan

A player on the Discord asked for string. It's a good fit: Protobot already has rubber bands, pneumatic tubing and
chains (a path of points and wraps that follows the parts), VEX's Pulley, Winch Drum, Ratchet, Pawl and Winch Bracket
models, gear trains that turn shafts, and a physics sandbox with the chain stops that string needs. Phases 1 to 3 are
built, with most of 4 and 5 (sections 6 to 8).

## 1. What the rules allow

- **Override R19d**: "An unlimited amount of non-elastic rope/string, no thicker than 1/4" (6.35 mm)." No minimum this
  season (Spin Up had a 1/8" minimum for its endgame).
- **R21**: tape may be used as an aglet on a rope's end; melting the end of nylon rope is allowed.
- **R19**: plastic zip ties up to 12" long and 1/4" wide are legal too (teams use them beside string; a later step).
- **VEX's own**: the Winch and Pulley Kit (276-1546): Winch Bracket, Winch Drum, Ratchet Gear, Winch Pawl, 8 VEX
  Pulleys and 4 ft of 1/8" solid braided nylon rope rated to 100 lb, "to lift, lower, hang, drag", block and tackle for
  mechanical advantage, the pawl to stop it back driving.

## 2. How teams really use string (what the plan has to cover)

1. **Winch hangs and lifts.** A motor turns a drum (VEX's Winch Drum, or just a shaft or a spacer used as a spool), the
   string winds on and pulls an arm up, or pulls the whole robot up a bar (hang/climb endgames). A ratchet and pawl stop
   it unwinding when the motor stops.
2. **Pulleys and block and tackle.** String over pulleys turns the pull's direction, and doubling it back through a
   pulley halves the force needed (VEX sells the kit for exactly this).
3. **Linear slide rigging.** Telescoping lifts: a winch pulls a string over a pulley at the top of one stage to raise the
   next. **Cascade** rigging (one string per stage, all stages move together) and **continuous** rigging (one string
   through every stage, the stages move in turn); a second string pulls it back down (or rubber bands/gravity do).
4. **Pull-back and triggers.** A winch pulls a catapult or puncher down against rubber bands; a string pulls a pin to
   fire it (a "string trigger").
5. **Releases and deploys.** At the start of a match a mechanism (an intake, wings, an arm) is held folded by a string
   tied over a pin or hook; a piston, a motor or a ratchet pulls the pin (or the string) and rubber bands throw it out.
6. **Travel stops.** A string from the frame to an arm or flap stops it swinging too far (a light, free hard stop,
   often used with rubber bands pulling the other way).
7. **Strings with rubber bands.** A band pulls a string that runs over a pulley, so the band can sit somewhere else.
8. **Endgame string launchers** (Spin Up): string fired out to cover tiles. Game-specific: only noted.

## 3. What a player sees

### Making a string (one tool, like the rubber band tool)

Parts list > Motion > **String and pulleys** > String (also Edit > Make a string, and right-click a pulley, drum or
shaft > Run string from here). The string follows the mouse like the rubber band does:

- **Click where it starts**: a hole, a screw, a standoff, a shaft or an edge. It's tied there (a small knot).
- **Click what it goes round**: a pulley, a winch drum, a shaft, a standoff or screw (it wraps on the side the mouse is
  on; Shift+click flips it), or through a hole (it passes through). The line snaps to the pulley's groove.
- **Click where it ends**: tied to a hole/screw/standoff, or **wound on a drum or shaft** (a winch: the end is tied to the
  drum and it winds on as that shaft turns).
- Enter finishes, Backspace removes the last point, Esc stops.
- By the mouse: the length so far, and what each point is ("ties to the arm", "over pulley 2", "winds on the drum").

A small bar (like the rubber band's): **kind** (VEX rope 1/8", paracord, braided nylon line, braided fishing line),
**Tight / Slack** (slack: the extra length, typed or stepped), **wound on the drum** (turns already on it), and a live
summary: length, "1 turn of the drum = 4.4" of string", and a warning past 1/4" thick (R19d).

### Editing one (like tubing)

Select a string: a dot on every point to drag onto another part, a gray dot on each span to add a point, right-click to
remove one, click a wrap to flip its side. The bar shows its length, slack and (in the simulation) its tension.

### In the build

- The string follows its parts when they move, straight when tight, hanging in a curve when slack.
- **Winding**: a motor (or the hand crank) turning the drum's shaft winds the string on, coils drawn on the drum; what
  it pulls moves, using the same joint solving the chain tensioner uses (an arm on one pivot swings up, a slide truck
  slides). When it can't (both ends grounded, or the string is already tight against something fixed) the run stops
  there, like a jammed gear train.
- **A string is a limit when moving parts**: dragging a part tied by a tight string further than the string allows
  stops at the string's length (a travel stop works in the build, not only in the simulation).
- **Ratchet**: a pawl on the drum's ratchet stops it unwinding (the gear kit's ratchet already blocks one way).

### In the simulation and games

- The string can't stretch (it's non-elastic): a span is slack or tight, and when it's tight it pulls both ends, round
  every pulley in its path (block and tackle multiplies the force like the real thing).
- **Winches** drive through it: the motor feels the load (a drum winding up a heavy robot slows and stalls like a real
  V5 motor).
- **Tension** shown on the selected string (lbf), red past the rope's rating (VEX rope 100 lb): it **breaks** there.
- **Releases**: a string tied round a pin (a screw or standoff) comes off when the pin is pulled out of its hole (a
  piston, a motor) or slides off the end of a post. Then the rubber bands do the rest.

### Everything else that should just work

- **Saved in the build**, copied/pasted and mirrored with its parts, undo for every change.
- **Parts list and cost**: string by length (VEX rope comes in 4 ft sections in the winch kit; rope by the foot for
  other kinds).
- **Online matches and replays**: strings are drawn from the simulation, like chains.
- **Help**: a foldable "How string works" guide in the tool (like the chain tool's): tie, wrap, wind, slack.
- **Examples** in Sample Builds: a winch arm, a hang winch, a cascade lift, a released intake, a travel stop.

## 4. How it fits together (for building it)

- **Part** "STRG-<kind>" (like "PTUB-4mm" and "CHAN-..."): a `ConnectingPart` joining what it ties (so the build moves
  together), its path in its saved state: `string:kind|slack|nodes` where each node is a tie (part, local point), a wrap
  (part, axis line, radius, way), a through-hole (hole) or a spool (part, axis line, radius, wound turns). Old builds
  without strings are unaffected.
- **Path**: `ChainPath` already lays tangent spans between circles with a way round each (chains, rubber bands);
  string adds ties (a point, no wrap) and spools (a circle whose wound length changes). Pulleys measured from the VEX
  model (groove radius), drums from the Winch Drum, shafts and standoffs by `Strands.RadiusAt` as rubber bands do.
- **Drawing**: `Strands.Rounded`/`Strands.Tube` (tubing's) at the rope's diameter with a braided texture; slack spans
  drawn as a hanging curve with the right length; coils on a drum as a helix; knots as small meshes.
- **Build mode**: `Follow` on state changes (as tubing and chains), the length kept; winding through `MotorRunner`
  (rotor turn -> wound length -> `ChainTensioner.SolveAngle`-style solve of the pulled body), moving limits through
  `MovementManager`.
- **Simulation**: the chain stop's method (`Sandbox.MakeChainStops`): a ConfigurableJoint per pulled body whose limit is
  moved every step so its edge is where the whole path would be exactly the string's length; free while slack. A
  spool's wound length follows its rotor; tension = the joints' force, fed back as torque on the drum (motors load and
  stall). Breaking = the joint removed past the rating. Releases = a tie node whose post has left its hole.
- **Tool**: `Motion/StringTool` (the rubber band tool's mouse flow and bar), `StringEditor` (tubing's editor),
  `FeaturePicker` for holes/edges, port of the chain tool's "how it works" guide.

## 5. In what order

1. **String you can place.** The tool, kinds, ties/wraps/through-holes, drawing (tight and slack), editing, saving,
   undo, parts list, pulleys and the winch kit together in the catalog. (About the size of the tubing work.)
2. **Physics.** Non-stretching string in the simulation through pulleys, tension readout, breaking. Travel stops and
   block and tackle work here.
3. **Winches.** Winding on drums and shafts in the build (motor runs, hand crank) and in the simulation, coils, ratchet
   and pawl holding it, motors feeling the load. Hangs and pull-backs work here.
4. **Slides and releases.** Cascade and continuous rigging on the linear slides; pins that release a string; string
   with rubber bands.
5. **Polish.** The guide, sample builds, costs, tests on each use from section 2.

**How hard**: phase 1 is moderate (most pieces exist). Phase 2 and 3 are the real work (a string through several
pulleys that can't stretch, and winding that pulls parts in the build), about as much as the chain tensioners were.
Phases 4-5 are smaller once those work.

## 6. What's built (build 88: phase 1)

- `Motion/StringLine.cs`: `StringKind` (VEX rope 1/8" 100 lb, paracord 550, braided nylon line 1/16", braided fishing
  line 50 lb), `StringNode` (a rubber band `BandPost` + side + spool), `StringPath.Of` (the 3D path: tangent arrival and
  departure round each round post in its own plane, sides chosen like a tight string: away from the line between its
  neighbours, else the side it bends least; tied-round loops, coils on a drum, knots; `Hanging` draws slack as a
  parabola per straight run), `StringLine` (part "STRG-<kind>", token "string:kind|length|turns|nodes", follows its
  parts and saves where its points are, undo `StringElement`, parts list name "String: <kind>, cut <length>").
- `Motion/StringTool.cs`: `StringTool` (parts list > Motion > String and pulleys > String; right-click a pulley or winch
  drum > Run string from here), `StringUi` (post picking = the band's `ElasticTool.PostAt` with pulleys and drums taken at
  their groove round their middle hole; the bar), `StringPreview`, `StringEditor` (select a string or right-click > Edit
  string: drag dots, click a post's dot for the other side, drag the string onto a post, right-click to remove; panel:
  kind, slack, turns on the drum).
- The catalog's "String and pulleys" group: String, VEX Pulley, Winch Drum, Ratchet Gear, Winch Pawl (moved from the
  Advanced Gear Kit: VEX sells them as the Winch and Pulley Kit).
- Not yet (phases 2-5): the string doesn't pull anything (no physics, no winding when the drum turns), no copy/paste or
  mirror of strings, no price (VEX's rope only comes in the winch kit).
- Tests: `Tools/AgentTesting/cs/t_str1.cs` (rig, make, info, look, select, tool from a pulley, tight), `t_str2.cs` (the
  tool's clicks by reflection, a pulley moved, undo, save and reopen, Simulate).

## 7. What's built (build 88: phase 2, physics)

- `Motion/Sandbox.Strings.cs` (Sandbox is partial now): string can't stretch. Each point of a string that moves relative
  to its frame (the body most of its points are on, or the world when a point is on a part that isn't simulated) gets
  a joint to the frame like the chain stops: a 40" distance limit moved every step so its edge is where the whole path
  (`StringPath.Of`, round pulleys and posts) is exactly the string's length; the slack is shared by each point's
  gradient (finite differences in 3D). A pulley turning on the frame (its middle on a hinge axis) holds nothing; a
  pulley that moves with two strands lengthens the string twice as fast, so it takes twice the pull (block and tackle).
- Pull (lbf) = the joint's force over that gradient; past the rating for 6 steps (1/40 s) it breaks: the joints go,
  a message says so ("The VEX rope broke: pulled with 112 lbf, it's rated for 100 lb"), and it's drawn as two loose ends
  hanging from its ties (`StringLine.broken`, `LooseEnds`). The pull is shown by the string (overlay) while the mouse is
  on it, when it's over 75% of its rating, and for 3 s after a break.
- Strings aren't simulated bodies (the sandbox skipped bands and tubes but not strings: a string became a part).
- `OnHingeAxis` counts only real hinges (one free turn): a string's or chain's stop joint (every way free) was taken
  for a hinge through its point, so a second string on the same arm held nothing.
- Measured (`cs/t_str3.cs` + `MotionTestRig.StringRig/FinishStringRig`, a 100 rpm motor swinging a 4" arm over a plate):
  the arm stops where the string is tight both ways and holds there (slack within 0.002"); the pull matches the torque
  the sandbox's motor applies to within 10-16% (single strand 0.58 lbf vs 0.69, block and tackle 0.26 vs 0.29 with
  twice the lever); with the rating lowered below the pull it breaks and the arm swings free.
- Found for phase 3: a stalled motor on a light rotor gives only a few % of its stall torque. `Sandbox.Turn` steps the
  motor implicitly (next = (I w + a dt) / (I + b dt)); when something stops the rotor every step, the torque it applies is
  a I / (I + b dt): for this arm (I 0.056 kg in², b dt 1.29) 4% of 2.1 N·m. Drivetrains don't notice (the robot's mass
  is in I), but a winch drum or an arm pushing on a stop does. A winch has to stall like a real motor, so phase 3
  starts by moving the motor's torque into the hinges' PhysX drives (target = free speed x command, damper = stall /
  free speed, capped at stall: exactly the DC motor curve, solved with the string's joints).
- Not yet: winding (phase 3), releases (phase 4), strings in the build's Move/Transform (a tight string doesn't stop a
  drag in the build yet).

## 8. What's built (build 88: phases 3 to 5)

- **Motors stall like V5 motors** (Sandbox): a gear train without wheels (a winch, an arm, a lift; drivetrains and
  racks keep the old step) is driven through its hinges' PhysX velocity drives: target the motor's free speed x
  command, damping stall / free speed, capped at the stall torque, which is the DC motor curve solved together with
  the string, a hard stop or a chain. Its motor's shaft also carries the V5 motor's rotor inertia through its cartridge
  (VEX doesn't publish it; 1e-6 kg m², a small 11 W motor's, times (3600 rpm / output rpm)²: ~2 kg in² at 100 rpm).
  Without it the solver couldn't hold a light drum against a tight string at the motor's full torque.
  Measured: the test arm stalled against a string pulls 10.7 lbf where 2.1 N·m at its lever is 10.5 (phase 2 got
  0.58); the chain tensioner rig's swing arm stops with the chain 0.002" over (0.03" before); the demo robot's arm
  holds still when let go (it crept 1.5" with the old step).
- **Winches in Simulate**: an end wound on a drum (or a shaft) is held where the string leaves the drum's rim, so the
  string's pull turns the drum and the motor feels it; what's wound on follows where the string leaves the drum in the
  drum's own frame (the drum turning, or the string swinging round it), and the free length is the rest. Coils are
  drawn as it winds. Measured: a 100 rpm motor winding on a shaft collar (0.28" to the rope's middle) pulls 64.8 lbf
  where 2.1 N·m gives 66.5; unwinding pays out 2.9 in/s (10.5 rad/s x 0.28").
- **Winches in the build**: a motor run or the hand crank turning the drum winds the string on or off (turns and free
  length, one undo step with the run); it winds until the string is tight and unwinds until it's all off, and the run
  stops there (what it pulls doesn't move in the build: in Simulate it does).
- **Releases**: an end tied round a pin (a screw, standoff or shaft) that goes through a hole in another simulated part
  lets go when the pin's end passes the loop (a piston or motor pulling it out). The string then hangs from its other
  end. Tested with a loose standoff over a plate hole lifted out: it lets go once its end passes the loop, not before.
- **Price**: VEX rope is the Winch and Pulley Kit (276-1546, $18.99, one 4 ft section): a string costs its share of
  48", and the order counts kits by cut length. The other kinds aren't VEX products (type a price in the parts list).
- **Guide**: "How string works" in the String tool's bar (tie, round, wind, pull, release, rules), foldable.
- Not yet: the build's Move/Transform still doesn't stop at a tight string; a pin's loop rides with the pin until it
  lets go (the string doesn't slide along the pin). (Cascades, open chain and sample builds: section 9.)
- Tests: `Tools/AgentTesting/str.sh` + `cs/t_str3.cs` (finish winch / pin, lines, lift), `t_tens.cs`, `t_armcheck.cs`.

## 9. What's built (build 88: open chain, cascade lifts)

- **Open chain** (`StringKind.chain`: "CHAIN" normal, "HSCHAIN" high strength; part "STRG-CHAIN"): a string kind whose
  line is the chain's pitch line (`PathRadius` 0: no stand-off from a post; a sprocket post is its pitch circle,
  `StringUi.PostAt`), drawn as VEX's links (children placed along the line every pitch, `StringLine.PlaceLinks`), cut
  length in whole links, never wound on anything, not rated (VEX doesn't publish a breaking strength: it doesn't break).
  In Simulate, round the first sprocket in its middle that a motor's train turns (`DrivenSprocket`) it is split into two
  runs, each "wound" on that sprocket at one end with plenty in reserve (`SimString.sub`, `partner`), so the sprocket
  pays one run out as fast as it takes the other in and the motor feels both. A run pulled past its length while the
  other side is slack skips teeth (`SkipTeeth`, at 0.2": less let a stalled motor slip its chain at 5 lbf).
- **What a string's walls learned from the cascade lifts** (`Sandbox.Strings.StringStop`):
  - The frame: a pulley or sprocket counts as the body it turns on (`TurnsOn`): an idler at the top of a tower made the
    moving stage the frame and the drive's and idler's stops asked for opposite things.
  - Where points are going: each wall is set for the slack the string will have next step at the points' speeds
    (`ahead`), so one that follows isn't a step behind.
  - Even pull (`EvenUp` 2.5 in/s per lbf, at most `MaxGive` 0.03" a step, only past 1.5x the least-held point + 1 lbf):
    a point held much harder than the rest is given room along its way and the others take up what it really moved
    into it. Lowering a cascade went at a twentieth of the motor's speed (the stage held by the carriage's rope, which
    walls only stop, never pull); now about 60% of raising. Taking up a whole room dragged a carriage into its bracket
    when its stage was held by its own stop; a drum's end in it walked a lift at its stop (`GiveSpool`, off).
  - The pull shown for a string wound on a drum or driven by a sprocket is that end's (what the motor feels): a point
    pressed against a hard stop read the stop's push too.
  - A pin only releases a string when it goes through the hole (a tower's hole behind a standoff post let a winch rope
    go at once).
- **Slides** (`Sandbox.MakeSlideTrucks`): a stage collides with the body it slides in (the joint had switched that
  off: hard stops did nothing), and the end limits act 0.1" early (a chain at stall pushed a stage 0.6" past them).
- **Reopening**: a string or chain round a sprocket, pulley or drum on a shaft is put back on that part, not the shaft
  (`StringLine.RoundPartAt`): reopened, the chain lift's chain was on the motor's shaft and nothing drove it.
- **Sample builds** (`Editor/CascadeLiftRig`, test `Tools/AgentTesting/lift.sh` + `cs/t_lift.cs`, `lift_cycle.sh MODE`,
  `cs/t_liftload.cs` for the saved files): `Sample Builds/Cascade Lift (string).pbb` and `(chain).pbb`. A plate, two
  1x5 C-channel towers on angles, a 12" outer slide member on each (stage 1), a slide truck in each bolted to a second
  member in front (stage 2, both sides joined by a crossbar at the top), a truck in each of those bolted to a plate
  across (the carriage). Two #64 bands per side raise it (tower top to stage 2's bottom bracket, stage 2's top bracket to
  the carriage); a hard stop on each tower stops stage 2 at 4.5". String: a 200 rpm motor's winch drum per side winds a
  VEX rope tied to stage 2 (5.5" of rope on it: it runs slack at the stop), and a rope from the tower's top under a post
  on stage 2 up to the carriage (the cascade, 2:1). Chain: a chain loop per side from stage 2's bracket down round the
  motor's 10T sprocket, up over a 10T idler and back (U raises, J lowers: one motor is reversed by how its loop wraps),
  and a chain from the tower's top under a 15T sprocket on a shaft on stage 2 up to the carriage. Measured (two cycles
  each, the same both times): stage 2 4.48", carriage 9.13" (string) / 9.06-9.14" (chain); at the bottom stop the winch
  holds 22 lbf (its 200 rpm motor's stall at the drum: 23.6), the chain 8-17 lbf at the top stop.
- Not done: the chain lift's carriage rests up to 0.4" high in some runs (its cascade sprocket's light body sits a little
  off its shaft under the band's pull); the walls are a heuristic, not a solved pulley constraint.

## Sources

- VEX V5RC Override game manual, Section 4 - The Robot (R19, R21): https://manuals.vex.com/v5/override/v2.1/robot/
- VEX Winch and Pulley Kit 276-1546 (contents, rope 1/8" nylon 100 lb): https://www.vexrobotics.com/276-1546.html
- VEX Library, Building V5 Robot Lift Systems (winch-driven telescoping lift): https://kb.vex.com/hc/en-us/articles/360037388692
- VEX Forum, 99371a cascade lift explanation: https://www.vexforum.com/t/99371a-cascade-lift-explanation/47984
- VEX Forum, How does a cascade lift actually work: https://www.vexforum.com/t/how-does-a-cascade-lift-actually-work/61500
- Game Manual 0, rigging (cascade vs continuous): https://game-manual-0.readthedocs.io/en/stable/docs/linear-motion-guide/rigging.html
- VEX Forum, My catapult is really weak (winch pull-back): https://www.vexforum.com/t/my-catapult-is-really-weak/147081
- VEX Forum, Crossbow trigger mechanism: https://www.vexforum.com/t/crossbow-trigger-mechanism/95109
- VEX Forum, Spin Up meta (string endgames, ratchet string release): https://www.vexforum.com/t/sooo-spin-up-meta-is/102027?page=25
- VEX Forum, How to build a string launcher for endgame: https://www.vexforum.com/t/how-to-build-a-string-launcher-for-endgame/106468?page=2
- VEX Q&A 1222 (Spin Up, tethered string endgame): https://events.vex.com/V5RC/2022-2023/QA/1222
