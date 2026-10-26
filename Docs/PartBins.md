# Part Bins: how players get parts

There are ~650 VEX parts, far too many for one bin each. The shelf in the back-right corner of
the workshop holds **24 category bins** (4 rows × 6). Each bin is a category; clicking it opens a
picker with everything in that category.

## The shelf

| Row (colour) | Bins |
|---|---|
| Top (green) – electronics & other | Motors · Brain & Power · Sensors · Game Elements · Miscellaneous · **Recently Used** |
| 3rd (yellow) – hardware & wheels | Screws · Nuts & Retainers · Omni Wheels · Other Wheels · Hinges & Slides · Pneumatics |
| 2nd (red) – motion | Shafts · Gears · Sprockets & Chain · Bearings & Collars · Spacers & Washers · Advanced Motion |
| Bottom (blue) – structure | C-Channels · Angles · Plates · Rails & U-Channels · Gussets & Brackets · Standoffs |

Heavy structural metal is on the bottom shelf, small hardware at eye level, like a real shop.
The **Recently Used** bin fills itself with the last parts you picked, so the parts you use most
(screws, nuts, spacers) are always one click away.

## Picking a part

1. Click a bin. It slides out a little and the **part picker** opens next to it.
2. The picker shows a grid of part cards (thumbnail + name). Parts that come in sizes are grouped:
   one card per family, with a size selector (e.g. screw length, gear tooth count, standoff length).
3. A search box filters across *all* bins, so typing "36T" finds every 36-tooth gear.
4. Click a card and the part attaches to your cursor, ready to place on the build table.
5. In VR later, the same picker becomes a panel floating beside the bin, operated with the laser pointer.

## Metal stock

Metal (C-channels, angles, plates, rails) comes out of the bins at **full length** only. Cut it to
size with the hack saw, as described in `Mechanics.md`.

## Where it lives in the project

- `Scripts/Runtime/Parts/PartBins.cs`: the 24 bins, their labels, and the rules that sort a library
  part into a bin.
- `Scripts/Runtime/Parts/PartCatalog.cs` + `Parts/PartCatalog.asset`: every part, its bin, model and
  measured real-world size.
- `Scripts/Runtime/Parts/PartBin.cs`: the component on each bin in the scene (with a click collider).
- `Tools > Protobot > Build Part Library` rebuilds materials, the catalog and the bin setup.
