# Protobot Rebuilt V2, Beta 1.9

Built on Protobot Rebuilt 1.3.7, now in Unity 6. Draft notes; finish after the playtest.

## New
- Full-detail VEX parts (the VEX-CAD library), a new parts list with categories, families, search and Recent.
- Quick fastening: C (screw + nut through touching holes), Shift+C (through every hole in line), hold F (screw over a
  hole, nut over a screw; mouse wheel = size), hold E (nylon spacer).
- Parts slide along screws and shafts and snap flush against their neighbours (the side the mouse is nearer); a part
  moved by a hole puts that face against the edge. Wheels and gears get real inserts (silver on shafts, bronze on
  screws) and spin on screws.
- Mirror (M): arrows on each side of the selected part; click to copy it mirrored, drag for a gap.
- Rotate rings show a dial with tick marks and the angle, lying in the plane of the turn.
- Saw, drill, measure, paint; chamfered gear teeth; polycarbonate sketcher (SolidWorks-style) with plastic rules.
- Motors, gears, chains and cylinders that move; a physics sandbox; Robot Skills on the 2026-27 Override field.
- Custom parts (import STL/OBJ/3MF/GLB/PLY) and a custom parts library.
- Preferences, Graphics (presets from Very Low to Extreme, benchmark) and Keyboard shortcuts (every key can be
  changed) windows.
- Build files are now zip-based (.pbb); old files still open and are upgraded on save. Autosave every minute.

## Fixed in this build
- Recent lists only parts that were placed.
- Gears and wheels moved by a hole onto a screw no longer flip.
- Inserts disappear when a wheel or gear comes off its screw or shaft.
- Cleaner motor window (keys as cards, gearbox buttons, sections) and a new Preferences window without keybinds.
- A part slid onto a screw goes against the neighbour on the mouse's side when both are in reach.
- New parts menu: category pills under the search, each family a row that opens in place, bigger tiles (three to a row).
- Controls window redone: Drive motors / Other motors sections, one card per motor with its cartridge color (click to change it), Keys / Left / Right, keys and Normal / Reversed.
- Changing a motor's cartridge after opening a build now changes its color too; the cartridge color no longer bleeds onto the motor housing.
- Motor labels follow the robot in the simulation (they stayed where the build was).
- New sample: 6 Motor Drive with intake (a flex-wheel roller on its own motor, keys I / K).
