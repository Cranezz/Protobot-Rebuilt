using System;
using System.Collections.Generic;
using Protobot.Building;
using Protobot.Interaction;
using Protobot.Parts;
using Protobot.Tools;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Protobot.Saving
{
    /// <summary>
    /// Reads the workshop into a <see cref="BuildData"/> and makes the workshop match one. Applying only
    /// changes what differs: parts that are still the same shape are moved, not rebuilt; parts that were cut
    /// or drilled differently are made again from the catalog and their edits replayed (the same code as the
    /// saw and drill), so undo and loading a save give exactly what the tools made.
    /// </summary>
    public static class BuildSnapshot
    {
        public static List<PartBody> LiveParts()
        {
            var result = new List<PartBody>();
            foreach (var part in Object.FindObjectsByType<PartBody>(FindObjectsSortMode.None))
                if (part.gameObject.activeInHierarchy)
                    result.Add(part);
            result.Sort((a, b) => a.Uid.CompareTo(b.Uid));
            return result;
        }

        public static BuildData Capture()
        {
            var data = new BuildData();
            var assemblies = new List<Assembly>();
            foreach (var part in LiveParts())
            {
                data.parts.Add(Describe(part));
                var assembly = Assembly.Of(part);
                if (assembly != null && !assemblies.Contains(assembly))
                    assemblies.Add(assembly);
                if (part.TryGetComponent(out ScrewJoint joint) && joint.enabled)
                    data.screws.Add(joint.Save());
            }
            foreach (var assembly in assemblies)
                data.assemblies.Add(assembly.Save());
            return data;
        }

        static PartData Describe(PartBody part)
        {
            Pose(part, out var position, out var rotation);
            var record = new PartData
            {
                uid = part.Uid, id = part.PartId, name = part.name, position = position, rotation = rotation,
                frozen = part.Body != null && part.Body.isKinematic,
            };
            if (part.TryGetComponent(out PartModifications mods))
            {
                var edits = new List<(int step, EditData edit)>();
                foreach (var cut in mods.cuts)
                    edits.Add((cut.step, new EditData { kind = EditKind.Cut, point = cut.point, normal = cut.keepNormal }));
                foreach (var hole in mods.holes)
                    edits.Add((hole.step, new EditData { kind = EditKind.Drill, point = hole.center, normal = hole.normal, diameter = hole.diameter, depth = hole.depth }));
                edits.Sort((x, y) => x.step.CompareTo(y.step));
                foreach (var (_, edit) in edits)
                    record.edits.Add(edit);
            }
            if (part.TryGetComponent(out HoverTarget hover))
            {
                foreach (var pair in hover.Paints)
                    record.paints.Add(new PaintData { material = pair.Key.name, color = pair.Value });
                record.paints.Sort((x, y) => string.CompareOrdinal(x.material, y.material));
            }
            return record;
        }

        /// <summary>
        /// Where a part is in the physics world. Moving bodies are drawn in between physics steps
        /// (interpolation), so the transform can lag the body a little; the body is what counts.
        /// </summary>
        static void Pose(PartBody part, out Vector3 position, out Quaternion rotation)
        {
            if (part.Body != null)
            {
                position = part.Body.position;
                rotation = part.Body.rotation;
                return;
            }
            var assembly = Assembly.Of(part);
            if (assembly == null || assembly.Body == null)
            {
                position = part.transform.position;
                rotation = part.transform.rotation;
                return;
            }
            var root = assembly.transform;
            var toRoot = Quaternion.Inverse(root.rotation);
            var body = assembly.Body;
            position = body.position + body.rotation * (toRoot * (part.transform.position - root.position));
            rotation = body.rotation * (toRoot * part.transform.rotation);
        }

        /// <summary>
        /// Makes the workshop match <paramref name="data"/>. Returns how many parts couldn't be made (a part
        /// missing from the catalog, an edit that no longer works).
        /// </summary>
        public static int Apply(BuildData data, Func<string, PartEntry> lookup, Transform parent)
        {
            var problems = 0;
            var live = new Dictionary<int, PartBody>();
            foreach (var part in LiveParts())
                live[part.Uid] = part;
            var wanted = new HashSet<int>();
            foreach (var record in data.parts)
                wanted.Add(record.uid);

            // Assemblies and screw joints are rebuilt from the data, so take them all apart first.
            foreach (var assembly in Object.FindObjectsByType<Assembly>(FindObjectsSortMode.None))
                assembly.Dissolve(parent);
            foreach (var joint in Object.FindObjectsByType<ScrewJoint>(FindObjectsSortMode.None))
                Object.DestroyImmediate(joint);

            foreach (var pair in live)
                if (!wanted.Contains(pair.Key))
                    Remove(pair.Value);

            var parts = new Dictionary<int, PartBody>();
            foreach (var record in data.parts)
            {
                live.TryGetValue(record.uid, out var part);
                if (part != null && !record.SameShape(Describe(part)))
                {
                    Remove(part);
                    part = null;
                }
                if (part == null)
                    part = Create(record, lookup, parent, ref problems);
                if (part == null)
                    continue;
                parts[record.uid] = part;

                if (part.name != record.name && !string.IsNullOrEmpty(record.name))
                {
                    part.name = record.name;
                    if (part.TryGetComponent(out HoverTarget titled))
                        titled.Configure(titled.Cursor, record.name, titled.Hint);
                }
                if (part.TryGetComponent(out HoverTarget hover) && !record.SamePaint(Describe(part)))
                    Paint(hover, record.paints);

                PartLayers.SetFastened(part, false);
                if (part.transform.parent != parent)
                    part.transform.SetParent(parent, true);
                var body = part.RestoreBody();
                part.transform.SetPositionAndRotation(record.position, record.rotation);
                body.position = record.position;
                body.rotation = record.rotation;
                body.isKinematic = record.frozen;
                if (!body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
            }
            Physics.SyncTransforms();

            foreach (var assembly in data.assemblies)
                Assembly.Restore(assembly, parts, parent);
            foreach (var screw in data.screws)
                ScrewJoint.Restore(screw, parts);
            Physics.SyncTransforms();
            return problems;
        }

        static void Remove(PartBody part)
        {
            part.gameObject.SetActive(false);  // gone for every lookup this frame (Destroy waits for the frame's end)
            Object.Destroy(part.gameObject);
        }

        static PartBody Create(PartData record, Func<string, PartEntry> lookup, Transform parent, ref int problems)
        {
            var entry = lookup(record.id);
            var prefab = entry == null ? null : entry.prefab != null ? entry.prefab : entry.model;
            if (prefab == null)
            {
                problems++;
                Debug.LogWarning($"[Build] No part \"{record.id}\" in the catalog; left out.");
                return null;
            }
            var go = Object.Instantiate(prefab, record.position, record.rotation, parent);
            go.name = entry.displayName;
            if (!go.TryGetComponent(out PartBody part))
            {
                problems++;
                Object.Destroy(go);
                return null;
            }
            part.SetUid(record.uid);
            foreach (var edit in record.edits)
            {
                try
                {
                    if (edit.kind == EditKind.Cut)
                        SawTool.Replay(go, edit.point, edit.normal);
                    else if (!DrillTool.Replay(part, edit.point, edit.normal, edit.diameter))
                        problems++;
                }
                catch (Exception e)
                {
                    problems++;
                    Debug.LogException(e);
                }
            }
            return part;
        }

        static void Paint(HoverTarget hover, List<PaintData> paints)
        {
            hover.SetPaint(null);
            foreach (var material in hover.Materials)
                foreach (var paint in paints)
                    if (paint.material == material.name)
                        hover.SetPaint(material, paint.color);
            if (hover.TryGetComponent(out PartModifications mods))
                mods.RecordPaint(hover);
        }
    }
}
