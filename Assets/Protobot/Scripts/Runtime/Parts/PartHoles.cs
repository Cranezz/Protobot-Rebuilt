using System;
using System.Collections.Generic;
using UnityEngine;

namespace Protobot.Parts
{
    /// <summary>What a hole does to a screw or shaft passing through it.</summary>
    public enum HoleKind
    {
        Clearance,  // a screw or shaft passes through loosely (structure's square holes, spacers)
        Threaded,   // a screw bites into it (nuts, standoff ends, motor cases)
        ShaftLock,  // a square bore that turns with a square shaft (gears, wheels, collars, motor output)
        Bearing,    // a round hole a shaft spins in
    }

    [Serializable]
    public struct Hole
    {
        public Vector3 center;   // middle of the wall it goes through (part space)
        public Vector3 axis;     // unit, part space; for a blind hole it points in from the open face
        public float size;       // square side or round diameter (m)
        public float depth;      // wall thickness (through), or how deep it goes (blind)
        public bool square;
        public bool through;
        public HoleKind kind;

        public Vector3 Entry => center - axis * (depth / 2f);
        public Vector3 Exit => center + axis * (depth / 2f);
    }

    /// <summary>A hole on a part as it is in the world right now.</summary>
    public struct HoleAt
    {
        public PartBody part;
        public int index;        // into PartHoles' list (-1 for one the drill added)
        public Hole local;
        public Vector3 center;
        public Vector3 axis;

        public float Size => local.size;
        public float Depth => local.depth;
        public Vector3 Entry => center - axis * (local.depth / 2f);
        public Vector3 Exit => center + axis * (local.depth / 2f);
    }

    /// <summary>
    /// The holes of a part, found from its model when the part library is built (HoleScanner). Saw cuts take
    /// away the holes on the cut-off side, and holes made by the drill become round ones of the bit's size.
    /// </summary>
    public class PartHoles : MonoBehaviour
    {
        [SerializeField] List<Hole> holes = new();

        public IReadOnlyList<Hole> Scanned => holes;

        public void Set(List<Hole> scanned)
        {
            holes.Clear();
            holes.AddRange(scanned);
        }

        /// <summary>The holes the part has now (part space), after cuts and drilling.</summary>
        public List<Hole> Current()
        {
            var result = new List<Hole>(holes.Count);
            var mods = GetComponent<PartModifications>();
            foreach (var hole in holes)
            {
                if (mods != null && !OnKeptSide(hole, mods))
                    continue;
                result.Add(mods != null ? Drilled(hole, mods) : hole);
            }
            if (mods != null)
            {
                // Drilled where there was no hole found (shouldn't happen, but keep them usable).
                foreach (var drilled in mods.holes)
                {
                    var known = false;
                    foreach (var hole in holes)
                        known |= Matches(hole, drilled);
                    if (!known)
                        result.Add(new Hole
                        {
                            center = drilled.center - drilled.normal.normalized * (drilled.depth / 2f),
                            axis = -drilled.normal.normalized, size = drilled.diameter, depth = drilled.depth,
                            square = false, through = true, kind = HoleKind.Bearing,
                        });
                }
            }
            return result;
        }

        static bool OnKeptSide(Hole hole, PartModifications mods)
        {
            foreach (var cut in mods.cuts)
                if (Vector3.Dot(hole.center - cut.point, cut.keepNormal.normalized) < hole.size * 0.25f)
                    return false;  // cut off, or cut through (a notch is no longer a hole)
            return true;
        }

        static bool Matches(Hole hole, PartModifications.DrilledHole drilled)
        {
            var normal = drilled.normal.normalized;
            if (Mathf.Abs(Vector3.Dot(normal, hole.axis)) < 0.95f)
                return false;
            var offset = drilled.center - hole.center;
            var along = Vector3.Dot(offset, hole.axis);
            return (offset - hole.axis * along).magnitude < hole.size / 2f && Mathf.Abs(along) <= hole.depth / 2f + 0.001f;
        }

        static Hole Drilled(Hole hole, PartModifications mods)
        {
            foreach (var drilled in mods.holes)
            {
                if (!Matches(hole, drilled))
                    continue;
                hole.square = false;
                hole.size = Mathf.Max(hole.size, drilled.diameter);
                if (hole.kind == HoleKind.Clearance || hole.kind == HoleKind.ShaftLock)
                    hole.kind = HoleKind.Bearing;  // a round hole: a shaft spins in it
            }
            return hole;
        }

        public HoleAt World(Hole hole, int index)
        {
            var t = transform;
            return new HoleAt
            {
                part = GetComponent<PartBody>(), index = index, local = hole,
                center = t.TransformPoint(hole.center), axis = t.TransformDirection(hole.axis).normalized,
            };
        }

        /// <summary>
        /// The hole whose opening is nearest <paramref name="worldPoint"/> (measured to the hole's axis between
        /// its two faces), within <paramref name="maxDistance"/>.
        /// </summary>
        public bool TryNearest(Vector3 worldPoint, float maxDistance, out HoleAt nearest)
        {
            nearest = default;
            var best = maxDistance;
            var current = Current();
            for (var i = 0; i < current.Count; i++)
            {
                var hole = World(current[i], i);
                var offset = worldPoint - hole.center;
                var along = Mathf.Clamp(Vector3.Dot(offset, hole.axis), -hole.Depth / 2f, hole.Depth / 2f);
                var distance = (offset - hole.axis * along).magnitude;
                if (distance < best)
                {
                    best = distance;
                    nearest = hole;
                }
            }
            return nearest.part != null;
        }

        /// <summary>All current holes in world space.</summary>
        public List<HoleAt> WorldHoles()
        {
            var current = Current();
            var result = new List<HoleAt>(current.Count);
            for (var i = 0; i < current.Count; i++)
                result.Add(World(current[i], i));
            return result;
        }
    }
}
