"""
Turns convert_report.json (written by Tools/Blender/convert_parts.py) into parts_manifest.json,
a list-shaped file that Unity's JsonUtility can read.

Shafts are stock you cut to length in the app, so only the longest shaft of each family is kept
(the same was done for metal before uploading). Part models that are dropped are listed in
<parts_manifest>.removed.txt so they can be deleted from the Unity project.

Usage: python make_parts_manifest.py <convert_report.json> <parts_manifest.json>
"""
import json
import os
import sys

# Folder holding every length of a stock part -> keep only its longest. The iPart "factory" file
# with the folder's own name (a default length) is dropped too.
FULL_LENGTH_FOLDERS = [
    "Motion/Shaft HS/Shaft HS",
    "Motion/Shaft LS/Shaft LS",
    "Motion/Shaft LS/Shaft LS Capped",
]

report_path, out_path = sys.argv[1], sys.argv[2]
with open(report_path, encoding="utf-8") as f:
    report = json.load(f)


def path_of(rel_obj):
    return rel_obj.replace("\\", "/")[:-4]  # without .obj


removed = set()
for folder in FULL_LENGTH_FOLDERS:
    family = [(max(info["size_m"]), path_of(rel)) for rel, info in report.items()
              if info.get("ok") and os.path.dirname(path_of(rel)) == folder]
    if family:
        longest = max(family)[1]
        removed.update(p for _, p in family if p != longest)
    removed.add(folder)  # the factory file sits next to the folder, with the same name

parts = []
for rel_obj, info in sorted(report.items()):
    if not info.get("ok") or path_of(rel_obj) in removed:
        continue
    parts.append({
        "path": rel_obj.replace("\\", "/")[:-4],  # without .obj
        "size": info["size_m"],
        "triangles": info["tris"],
        "bodies": info["bodies"],
        "materials": [{"name": name, "color": color} for name, color in sorted(info.get("materials", {}).items())],
    })

with open(out_path, "w", encoding="utf-8") as f:
    json.dump({"parts": parts}, f, indent=1)
with open(out_path + ".removed.txt", "w", encoding="utf-8") as f:
    f.write("\n".join(sorted(removed)))
failed = [k for k, v in report.items() if not v.get("ok")]
print(f"{len(parts)} parts written, {len(removed)} shorter stock lengths dropped, {len(failed)} failed: {failed[:10]}")
