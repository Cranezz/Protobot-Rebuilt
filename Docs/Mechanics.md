# Protobot Rebuilt V2 — Planned Mechanics

Taken from the original VR design doc. **Only the mechanics apply for now** — the app is desktop first,
so the VR controls (grip, trigger, X/Y buttons, laser pointers, radial menu on the hand) will be replaced
with mouse/keyboard equivalents. The VR mode comes later.

## Metal parts are cut to length in the app
- Only the **full-length** version of each metal part is imported (e.g. 35-hole C-channels and angles,
  the largest plates). Players cut them down in the app, like a real hacksaw.
- Cuts are done with mesh plane-slicing: the mesh is split along the cut plane and the open side is
  capped with new faces, so the cut part is solid (no hole left in the mesh).
- The removed side disappears; only the kept side stays in the build.

## Workspace
- Building happens in a workshop/garage: tall ceiling, tools and decorations, a metal table in the middle.
  You can build anywhere, the table is for organisation.

## Tools (picked from a radial menu; centre = no tool)
### Hack saw
- Near a part, a cutting plane is shown at the saw's angle. The side that will be removed turns semi-transparent.
- Angle snapping is on by default at 15°; cycle through 5°, 10°, 15°, 30°, 45°.
- **Precise cut menu**: the saw freezes relative to the part; sliders + typed values for each axis angle
  and cut depth. The plane updates live; angles above 360° wrap around.
- Confirm to cut; the removed side vanishes.

### Spray paint
- Colour picker: square colour chart (Photoshop-style), RGB sliders/fields, hex code,
  and the last 20 colours used (5 columns × 4 rows).
- The paint chamber on the gun shows the current colour. Click a part to paint it.

### Drill
- Widens existing holes so other parts fit.
- Bit-size picker: scrolling slider with labelled tick marks, ends fade out, stops at min/max size.
- Drills perpendicular to the surface, only on existing holes. The preview snaps to the hole nearest the
  bit tip and shows the area that will be removed; confirm to drill.

## Saving & loading
- Save writes to the app's own saves folder automatically (no file picker each time).
- Load screen lists every file in the saves folder (so manually copied files show up).
  Importing a file copies it into the saves folder.
- Hovering a save shows a large preview plus size, created/modified dates, playtime and part count.
- Opening a save while a build is open asks: Save / Don't save / Cancel.
- Autosave after every major change (move, cut, paint…) if it doesn't hurt performance; otherwise every 5 minutes.

## Main menu
- App name, New build, Load build, Settings, Help, Quit — shown in front of the workshop backdrop.
- New build: loading screen (darkened backdrop + loading bar), then fade into the workshop.
- Load build: scrollable list (thumbnail, name, size, created, last opened) + "Open from file".
- Help: opens the GitHub help pages (ideally shown inside the app).
