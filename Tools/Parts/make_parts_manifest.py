"""
Turns convert_report.json (written by Tools/Blender/convert_parts.py) into parts_manifest.json,
a list-shaped file that Unity's JsonUtility can read.

Usage: python make_parts_manifest.py <convert_report.json> <parts_manifest.json>
"""
import json
import sys

report_path, out_path = sys.argv[1], sys.argv[2]
with open(report_path, encoding="utf-8") as f:
    report = json.load(f)

parts = []
for rel_obj, info in sorted(report.items()):
    if not info.get("ok"):
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
failed = [k for k, v in report.items() if not v.get("ok")]
print(f"{len(parts)} parts written, {len(failed)} failed: {failed[:10]}")
