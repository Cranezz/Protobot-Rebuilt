using System.Collections.Generic;
using Protobot.Parts;
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

        public bool Has(PartBody part) => a == part || b == part;
        public PartBody Other(PartBody part) => a == part ? b : a;
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
        /// first assembly did; it's frozen if either side was frozen.
        /// </summary>
        public static Assembly Join(PartBody a, PartBody b, LinkKind kind)
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

            // Upgrade an existing link rather than adding a second one.
            var found = false;
            for (var i = 0; i < result.links.Count; i++)
            {
                var link = result.links[i];
                if (!(link.Has(a) && link.Has(b)))
                    continue;
                found = true;
                if (kind == LinkKind.Clamp)
                    link.kind = LinkKind.Clamp;
                result.links[i] = link;
            }
            if (!found)
                result.links.Add(new Link { a = a, b = b, kind = kind });
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
    }
}
