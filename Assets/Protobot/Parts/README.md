# VEX Part Library — do not edit

The models in `Models/` are the official part models made by someone else.
Their dimensions must stay exactly as authored.

- Don't scale, re-export or modify these files.
- Import settings for everything in `Models/` are enforced by
  `Scripts/Editor/PartModelImportGuard.cs`, so changes in the Inspector get reset.
- To change how a part *looks* (color, material), do it in code or in
  `Materials/` — never on the mesh itself.
- `Definitions/` holds the data that describes each part (name, holes, weight, etc.).
