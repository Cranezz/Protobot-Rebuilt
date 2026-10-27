using System;
using System.Collections.Generic;
using Protobot.Building;
using UnityEngine;

namespace Protobot.Saving
{
    /// <summary>
    /// A robot (every part in the workshop) as data: each part with where it is and what was done to it, and
    /// how parts are joined. Undo keeps these in memory; save files are the same thing as JSON. Positions are
    /// metres, Unity space; edits are in each part's own space.
    /// </summary>
    [Serializable]
    public class BuildData
    {
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;
        public string name;
        public string savedAt;   // ISO 8601, local time
        public List<PartData> parts = new();
        public List<AssemblyData> assemblies = new();
        public List<ScrewData> screws = new();

        /// <summary>
        /// Whether two builds are the same for the player: the same parts, joined the same way, each within a
        /// millimetre and half a degree of where it was (things settling on the bench don't count as a change).
        /// </summary>
        public bool SameAs(BuildData other)
        {
            if (other == null || parts.Count != other.parts.Count || assemblies.Count != other.assemblies.Count || screws.Count != other.screws.Count)
                return false;
            for (var i = 0; i < parts.Count; i++)
                if (!parts[i].SameAs(other.parts[i]))
                    return false;
            for (var i = 0; i < assemblies.Count; i++)
                if (!assemblies[i].SameAs(other.assemblies[i]))
                    return false;
            for (var i = 0; i < screws.Count; i++)
                if (!screws[i].SameAs(other.screws[i]))
                    return false;
            return true;
        }
    }

    [Serializable]
    public class PartData
    {
        public int uid;
        public string id;        // catalog id
        public string name;      // shown name (cut stock is renamed to its new length)
        public Vector3 position;
        public Quaternion rotation;
        public bool frozen;
        public List<EditData> edits = new();
        public List<PaintData> paints = new();

        public bool SameShape(PartData other)
        {
            if (id != other.id || edits.Count != other.edits.Count)
                return false;
            for (var i = 0; i < edits.Count; i++)
                if (!edits[i].SameAs(other.edits[i]))
                    return false;
            return true;
        }

        public bool SamePaint(PartData other)
        {
            if (paints.Count != other.paints.Count)
                return false;
            for (var i = 0; i < paints.Count; i++)
                if (paints[i].material != other.paints[i].material || paints[i].color != other.paints[i].color)
                    return false;
            return true;
        }

        public bool SameAs(PartData other) =>
            uid == other.uid && frozen == other.frozen && SameShape(other) && SamePaint(other)
            && Close(position, other.position, rotation, other.rotation);

        public static bool Close(Vector3 p1, Vector3 p2, Quaternion r1, Quaternion r2) =>
            (p1 - p2).sqrMagnitude < 0.001f * 0.001f && Quaternion.Angle(r1, r2) < 0.5f;
    }

    public enum EditKind { Cut, Drill }

    /// <summary>A saw cut (keeps the side <see cref="normal"/> points to) or a drilled hole (bit into -normal).</summary>
    [Serializable]
    public struct EditData
    {
        public EditKind kind;
        public Vector3 point;
        public Vector3 normal;
        public float diameter;
        public float depth;

        public bool SameAs(EditData other) =>
            kind == other.kind && (point - other.point).sqrMagnitude < 0.00005f * 0.00005f && (normal - other.normal).sqrMagnitude < 1e-6f
            && Mathf.Abs(diameter - other.diameter) < 1e-6f;
    }

    [Serializable]
    public struct PaintData
    {
        public string material;
        public Color color;
    }

    [Serializable]
    public class AssemblyData
    {
        public Vector3 position;
        public Quaternion rotation;
        public bool frozen;
        public List<int> parts = new();
        public List<LinkData> links = new();

        public bool SameAs(AssemblyData other)
        {
            if (frozen != other.frozen || parts.Count != other.parts.Count || links.Count != other.links.Count)
                return false;
            for (var i = 0; i < parts.Count; i++)
                if (parts[i] != other.parts[i])
                    return false;
            for (var i = 0; i < links.Count; i++)
                if (links[i].a != other.links[i].a || links[i].b != other.links[i].b || links[i].kind != other.links[i].kind)
                    return false;
            return true;
        }
    }

    [Serializable]
    public struct LinkData
    {
        public int a, b;
        public LinkKind kind;
        public bool hasPivot;
        public Vector3 pointA, axisA, pointB, axisB;
    }

    [Serializable]
    public class ScrewData
    {
        public int screw;
        public int nut;          // 0: no nut
        public bool threaded;
        public float stackEnd;
        public List<int> stack = new();

        public bool SameAs(ScrewData other)
        {
            if (screw != other.screw || nut != other.nut || stack.Count != other.stack.Count)
                return false;
            for (var i = 0; i < stack.Count; i++)
                if (stack[i] != other.stack[i])
                    return false;
            return true;
        }
    }
}
