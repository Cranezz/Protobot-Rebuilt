# Protobot Rebuilt V2, Beta 1.9: playtest prompt

Paste everything below the line into Claude (desktop app, Code tab or a chat with computer use) on the test laptop.

---

You are play-testing **Protobot Rebuilt V2 Beta 1.9**, a Windows app for designing VEX V5 robots (a CAD-style
builder: parts, screws, nuts, cutting, drilling, motors and a physics simulation). The developer is asleep. Work on
your own, don't ask questions, and write a very detailed report. Never change anything outside the app and the report
file. Don't sign in to anything and don't install anything other than this app.

## Setup
1. The app is in the folder `Protobot Beta 1.9` (on the Desktop or in Downloads). Run `ProtobotRebuiltV2.exe`.
   Use computer use to control it (screenshots, clicks, typing).
2. Write the laptop's specs at the top of the report: CPU, GPU, RAM, screen resolution (Settings > System > About and
   Display).
3. Save the report as `Desktop\Protobot Beta 1.9 playtest report.md`. Save it after each section, so nothing is lost
   if you run out of time.

## How to report
For every problem, write:
- **Title**, **severity** (Crash / Blocker / Major / Minor / Polish), **area**.
- **Steps** to reproduce, numbered, exact (menu paths, keys, which part).
- **Expected** vs **actual**.
- What the message toast at the bottom said, if anything (every action is meant to show one).
- A screenshot file name if you saved one (Desktop\playtest shots\).

Also list anything confusing for a new user (unclear labels, missing hints, hard-to-find features) as
"Usability" items. End with a summary: the top 10 things to fix, in order.

## What to test (in this order; spend about 10 to 15 minutes on each section)

### 1. Start-up and graphics (low-end check)
- How long it takes to start; the launch window; close it.
- Edit > Graphics…: note the preset it picked "for this PC". Try every preset from Very Low to Extreme. For each,
  write the FPS shown in the footer while orbiting the camera. Run the Benchmark tab once and copy its results.
- Turn on the frame rate counter. Note stutter, flicker, black or teal screens, missing shadows, or UI drawn wrong.
- Window mode and resolution changes: does the "keep these settings?" prompt work?

### 2. Building basics
- Parts list on the right: every category tab, search ("omni 3.25", "spacer 1/4", "c-channel"), folding families,
  the sort options. Recent should only list parts that were actually placed.
- Place a C-channel, rotate with R while placing (a dial with tick marks and the angle should show), T resets the
  turn, Ctrl+mouse wheel changes the hole count while placing.
- Select (V), Transform (1), Move (2) tools: move and rotate parts, the rotate rings' dial, snapping steps in the
  tool options.
- Undo/redo (Ctrl+Z / Ctrl+Y) after every kind of action.

### 3. Fastening
- Hover a hole and press C (screw + nut through it and the hole touching it), Shift+C (through every hole in line).
- Hold F over a hole (screw ghost; mouse wheel changes length; release places). Hold F over a screw (nut; wheel
  changes the kind). Hold E over a screw or hole (nylon spacer; wheel changes length). Ctrl turns edge snapping off.
- Put a wheel, a gear and spacers on a screw between two C-channels: do they snap flush against neighbours, to the
  side the mouse is nearer? Grab a wheel by its hole with the Move tool and slide it along the screw: the face you
  grabbed should go against the edge it snaps to, without strange flips.
- Wheels/gears on a screw show a bronze insert; on a 1/8" shaft a silver one; taking the wheel off removes it.
- Look closely at hole outlines on channels and other parts: are any sitting inside or outside the surface?

### 4. Mirror, saw, drill, measure, paint
- Select a part, press M: arrows on each side. Click one (copy touching), drag one out (gap), Shift for one part.
- Saw (4): cut channels square and at an angle, "All parts", chamfer gear teeth. Drill (5): drill holes, enlarge one.
  Measure (6): distances between holes/edges/faces. Paint: paint parts, Alt+click to pick a color.

### 5. Motors, chains, simulation
- Place motors, click a motor to set its keys, run it by holding the key; gears and chains should turn.
- Edit > Make a chain…: make a chain round two sprockets.
- Open `Sample Builds\6 Motor Drive (3.25 omni, 360 rpm).pbb` (File > Open). Press Simulate; drive with W/A/S/D and
  the arrows. Note how it drives, FPS during the simulation, anything that falls apart. Esc stops.
- Play game (top bar): set a start, run Robot Skills, check the score.

### 6. Plastic and custom parts
- Plastic > New polycarbonate piece…: draw a rectangle with a few holes, dimension it, make the piece, place it.
- File > Import part…: if any STL/OBJ is on the laptop use it, otherwise skip.

### 7. Saving and preferences
- File > Save as… (to the Desktop), close, reopen it: everything should come back (colors, cuts, motor keys).
- Edit > Preferences…: every setting on every tab. Edit > Keyboard shortcuts…: change a key (for example C to G),
  check it works, Reset all.
- Autosave (Preferences > General): turn on, wait a minute, check it saved.

### 8. Free play
Spend the rest of the time building a small robot the way a new VEX student would, and write down everything that
felt slow, confusing or broken.
