using System.Collections.Generic;
using Protobot.Parts;
using Protobot.Saving;
using UnityEngine;

namespace Protobot.Building
{
    /// <summary>How two parts are held together.</summary>
    public enum LinkKind
    {
        Tack,   // placed hole to hole (or a screw with no nut yet): stays put, but pulls off when grabbed
        Clamp,  // screwed and nutted, or screwed into a threaded hole: one rigid piece
    }

    public struct Link
    {
        public PartBody a, b;
        public LinkKind kind;

        // The hole line the two parts meet on (each in its own part's space), if they were put together by a
        // hole: a tacked part turns about it.
        public bool hasPivot;
        public Vector3 pointA, axisA, pointB, axisB;

        public bool Has(PartBody part) => a == part || b == part;
        public PartBody Other(PartBody part) => a == part ? b : a;

        /// <summary>The pivot in the world, seen from <paramref name="part"/>'s side.</summary>
        public void Pivot(PartBody part, out Vector3 point, out Vector3 axis)
        {
            var t = part.transform;
            point = t.TransformPoint(part == a ? pointA : pointB);
            axis = t.TransformDirection(part == a ? axisA : axisB).normalized;
        }
    }

    /// <summary>
    /// Parts joined together, simulated as one rigid body: the parts give up their own Rigidbodies and their
    /// colliders make up this one (mass and centre of mass from the parts). Which part is joined to which, and
    /// how, is kept as links, so taking a part off splits the assembly where it falls apart. Later the same
    /// links will become joints (pivots, shafts) for simulating mechanisms.
    /// </summary>
    public class Assembly : MonoBehaviour
    {
        readonly List<Link> links = new();
        Rigidbody body;

        public Rigidbody Body => body;
        public IReadOnlyList<Link> Links => links;

        public static Assembly Of(Component part) => part != null ? part.GetComponentInParent<Assembly>() : null;

        /// <summary>The parts in this assembly.</summary>
        public List<PartBody> Parts()
        {
            var parts = new List<PartBody>();
            foreach (Transform child in transform)
                if (child.TryGetComponent(out PartBody part))
                    parts.Add(part);
            return parts;
        }

        /// <summary>Whether a part is held on by a fastener (otherwise it's only tacked and pulls off).</summary>
        public static bool IsFastened(PartBody part)
        {
            var assembly = Of(part);
            if (assembly == null)
                return false;
            foreach (var link in assembly.links)
                if (link.kind == LinkKind.Clamp && link.Has(part))
                    return true;
            return false;
        }

        /// <summary>
        /// Joins two parts (and whatever each is already joined to). The result keeps moving the way the
        /// first assembly did; it's frozen if either side was frozen. <paramref name="pivot"/> and
        /// <paramref name="axis"/> (world) are the hole line they meet on, if any.
        /// </summary>
        public static Assembly Join(PartBody a, PartBody b, LinkKind kind, Vector3? pivot = null, Vector3? axis = null)
        {
            if (a == null || b == null || a == b)
                return Of(a);
            var assemblyA = Of(a);
            var assemblyB = Of(b);
            var frozen = IsFrozen(a) || IsFrozen(b);
            Assembly result;
            if (assemblyA == null && assemblyB == null)
            {
                result = Create(b.transform);
                result.Adopt(b);
                result.Adopt(a);
            }
            else if (assemblyA != null && assemblyB == null)
            {
                result = assemblyA;
                result.Adopt(b);
            }
            else if (assemblyA == null)
            {
                result = assemblyB;
                result.Adopt(a);
            }
            else
            {
                result = assemblyB;
                if (assemblyA != assemblyB)
                    result.Absorb(assemblyA);
            }

            var link = new Link { a = a, b = b, kind = kind };
            if (pivot != null && axis != null)
            {
                link.hasPivot = true;
                link.pointA = a.transform.InverseTransformPoint(pivot.Value);
                link.axisA = a.transform.InverseTransformDirection(axis.Value).normalized;
                link.pointB = b.transform.InverseTransformPoint(pivot.Value);
                link.axisB = b.transform.InverseTransformDirection(axis.Value).normalized;
            }

            // Upgrade an existing link rather than adding a second one.
            var found = false;
            for (var i = 0; i < result.links.Count; i++)
            {
                var existing = result.links[i];
                if (!(existing.Has(a) && existing.Has(b)))
                    continue;
                found = true;
                if (kind == LinkKind.Clamp)
                    existing.kind = LinkKind.Clamp;
                if (!existing.hasPivot && link.hasPivot)
                {
                    existing.hasPivot = true;
                    existing.pointA = existing.a == a ? link.pointA : link.pointB;
                    existing.axisA = existing.a == a ? link.axisA : link.axisB;
                    existing.pointB = existing.a == a ? link.pointB : link.pointA;
                    existing.axisB = existing.a == a ? link.axisB : link.axisA;
                }
                result.links[i] = existing;
            }
            if (!found)
                result.links.Add(link);
            result.body.isKinematic |= frozen;
            result.Recompute();
            return result;
        }

        static bool IsFrozen(PartBody part)
        {
            var body = Of(part) is { } assembly ? assembly.body : part.Body;
            return body != null && body.isKinematic;
        }

        static Assembly Create(Transform like)
        {
            var go = new GameObject("Assembly") { layer = like.gameObject.layer };
            go.transform.SetPositionAndRotation(like.position, like.rotation);
            go.transform.SetParent(like.parent, true);
            var assembly = go.AddComponent<Assembly>();
            assembly.body = go.AddComponent<Rigidbody>();
            assembly.body.interpolation = RigidbodyInterpolation.Interpolate;
            assembly.body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            assembly.body.maxDepenetrationVelocity = PartBody.Depenetration;
            return assembly;
        }

        void Adopt(PartBody part)
        {
            if (part.Body != null)
            {
                body.linearVelocity = part.Body.linearVelocity;
                part.RemoveBody();
            }
            part.transform.SetParent(transform, true);
        }

        void Absorb(Assembly other)
        {
            foreach (var part in other.Parts())
                part.transform.SetParent(transform, true);
            links.AddRange(other.links);
            body.isKinematic |= other.body.isKinematic;
            Destroy(other.gameObject);
        }

        /// <summary>
        /// Takes a part off: its tack links go (a fastened part can't be pulled off). Whatever no longer holds
        /// together becomes separate again. Returns false if the part is fastened.
        /// </summary>
        public static bool Detach(PartBody part)
        {
            var assembly = Of(part);
            if (assembly == null)
                return true;
            if (IsFastened(part))
                return false;
            assembly.links.RemoveAll(link => link.Has(part));
            assembly.Split();
            return true;
        }

        /// <summary>Removes every link of a part (e.g. its screw was taken out) and splits what falls apart.</summary>
        public static void Unlink(PartBody part)
        {
            var assembly = Of(part);
            if (assembly == null)
                return;
            assembly.links.RemoveAll(link => link.Has(part));
            assembly.Split();
        }

        /// <summary>The part's clamp links become tacks (e.g. its nut came off).</summary>
        public void Loosen(PartBody part)
        {
            for (var i = 0; i < links.Count; i++)
            {
                if (!links[i].Has(part))
                    continue;
                var link = links[i];
                link.kind = LinkKind.Tack;
                links[i] = link;
            }
        }

        /// <summary>
        /// The hole a part can be turned about without coming off: its one link to the rest of the assembly
        /// that has a pivot and isn't clamped, and the parts that turn with it (those only held on through
        /// it). False if it's fastened or held at more than one place.
        /// </summary>
        public bool TryHinge(PartBody part, out Link hinge, out List<PartBody> turning)
        {
            hinge = default;
            turning = null;
            var found = false;
            foreach (var link in links)
            {
                if (!link.Has(part) || link.kind != LinkKind.Tack || !link.hasPivot)
                    continue;
                var side = SideOf(part, link);
                if (side.Contains(link.Other(part)))
                    continue;  // still held on another way round: turning would tear it off
                // The part that was put onto the other is the one that turns: prefer its own placing link,
                // then the smaller side.
                if (!found || (link.a == part && hinge.a != part) || ((link.a == part) == (hinge.a == part) && side.Count < turning.Count))
                {
                    hinge = link;
                    turning = side;
                    found = true;
                }
            }
            return found;
        }

        /// <summary>The parts still joined to <paramref name="part"/> if <paramref name="cut"/> were gone.</summary>
        List<PartBody> SideOf(PartBody part, Link cut)
        {
            var side = new List<PartBody> { part };
            for (var i = 0; i < side.Count; i++)
            {
                foreach (var link in links)
                {
                    if (!link.Has(side[i]) || (link.a == cut.a && link.b == cut.b))
                        continue;
                    var other = link.Other(side[i]);
                    if (other != null && !side.Contains(other))
                        side.Add(other);
                }
            }
            return side;
        }

        /// <summary>Re-forms the assembly from its links: groups that still hold together stay joined.</summary>
        void Split()
        {
            var parts = Parts();
            var groups = new List<List<PartBody>>();
            var seen = new HashSet<PartBody>();
            foreach (var start in parts)
            {
                if (!seen.Add(start))
                    continue;
                var group = new List<PartBody> { start };
                for (var i = 0; i < group.Count; i++)
                {
                    foreach (var link in links)
                    {
                        if (!link.Has(group[i]))
                            continue;
                        var other = link.Other(group[i]);
                        if (other != null && seen.Add(other))
                            group.Add(other);
                    }
                }
                groups.Add(group);
            }
            if (groups.Count <= 1 && parts.Count > 1)
            {
                Recompute();
                return;
            }

            // The biggest group stays in this assembly; the others leave (single parts get their body back).
            groups.Sort((x, y) => y.Count.CompareTo(x.Count));
            var velocity = body.linearVelocity;
            var frozen = body.isKinematic;
            for (var g = 1; g < groups.Count; g++)
                Release(groups[g], velocity, frozen);
            if (groups.Count > 0 && groups[0].Count == 1)
                Release(groups[0], velocity, frozen);
            if (Parts().Count == 0)
                Destroy(gameObject);
            else
                Recompute();
        }

        void Release(List<PartBody> group, Vector3 velocity, bool frozen)
        {
            if (group.Count == 1)
            {
                var part = group[0];
                part.transform.SetParent(transform.parent, true);
                PartLayers.SetFastened(part, false);
                var partBody = part.RestoreBody();
                partBody.isKinematic = frozen;
                if (!frozen)
                    partBody.linearVelocity = velocity;
                partBody.WakeUp();
                return;
            }
            var assembly = Create(group[0].transform);
            foreach (var part in group)
                part.transform.SetParent(assembly.transform, true);
            assembly.links.AddRange(links.FindAll(link => group.Contains(link.a)));
            links.RemoveAll(link => group.Contains(link.a));
            assembly.body.isKinematic = frozen;
            assembly.Recompute();
        }

        /// <summary>Mass and centre of mass from the parts (after joining, splitting or editing one).</summary>
        public void Recompute()
        {
            var total = 0f;
            var centre = Vector3.zero;
            foreach (var part in Parts())
            {
                var m = part.Mass;
                total += m;
                centre += transform.InverseTransformPoint(part.transform.TransformPoint(part.LocalCenterOfMass)) * m;
            }
            if (total <= 0f)
                return;
            body.mass = total;
            body.centerOfMass = centre / total;
            body.ResetInertiaTensor();
            body.WakeUp();
        }

        // ---------------------------------------------------------------- saving

        public AssemblyData Save()
        {
            var data = new AssemblyData { position = body.position, rotation = body.rotation, frozen = body.isKinematic };
            foreach (var part in Parts())
                data.parts.Add(part.Uid);
            foreach (var link in links)
            {
                if (link.a == null || link.b == null)
                    continue;
                data.links.Add(new LinkData
                {
                    a = link.a.Uid, b = link.b.Uid, kind = link.kind, hasPivot = link.hasPivot,
                    pointA = link.pointA, axisA = link.axisA, pointB = link.pointB, axisB = link.axisB,
                });
            }
            return data;
        }

        /// <summary>Joins parts (already where they belong, each with its own body) back into an assembly.</summary>
        public static Assembly Restore(AssemblyData data, Dictionary<int, PartBody> parts, Transform parent)
        {
            var members = new List<PartBody>();
            foreach (var uid in data.parts)
                if (parts.TryGetValue(uid, out var part) && part != null)
                    members.Add(part);
            if (members.Count < 2)
                return null;
            var go = new GameObject("Assembly") { layer = members[0].gameObject.layer };
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(data.position, data.rotation);
            var assembly = go.AddComponent<Assembly>();
            assembly.body = go.AddComponent<Rigidbody>();
            assembly.body.interpolation = RigidbodyInterpolation.Interpolate;
            assembly.body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            assembly.body.maxDepenetrationVelocity = PartBody.Depenetration;
            assembly.body.position = data.position;
            assembly.body.rotation = data.rotation;
            foreach (var part in members)
            {
                part.RemoveBody();
                part.transform.SetParent(go.transform, true);
            }
            foreach (var link in data.links)
            {
                if (!parts.TryGetValue(link.a, out var a) || !parts.TryGetValue(link.b, out var b) || a == null || b == null)
                    continue;
                assembly.links.Add(new Link
                {
                    a = a, b = b, kind = link.kind, hasPivot = link.hasPivot,
                    pointA = link.pointA, axisA = link.axisA, pointB = link.pointB, axisB = link.axisB,
                });
            }
            assembly.body.isKinematic = data.frozen;
            assembly.Recompute();
            return assembly;
        }

        /// <summary>Lets go of every part (they keep where they are, without bodies) and goes away.</summary>
        public void Dissolve(Transform parent)
        {
            foreach (var part in Parts())
                part.transform.SetParent(parent, true);
            links.Clear();
            gameObject.SetActive(false);
            Destroy(gameObject);
        }
    }
}
