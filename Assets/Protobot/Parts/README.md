# VEX Part Library — do not edit

The models in `Models/` are the official VEX-CAD part models, made by someone else.
Their dimensions must stay exactly as authored.

- Don't scale, re-export or modify these files.
- Import settings for everything in `Models/` are enforced by
  `Scripts/Editor/PartModelImportGuard.cs` (metres, read/write, the models' own materials).
- The app doesn't use these files directly: `Tools > Protobot > Import VEX Parts` bakes them into
  inch-scaled meshes and part types in `Assets/ProtobotRebuilt` (see `CLAUDE.md`, "Parts").
