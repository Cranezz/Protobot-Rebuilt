using System.Collections.Generic;
using Protobot.Parts;
using UnityEngine;

namespace Protobot.Building
{
    /// <summary>
    /// Where things go when they're put on a hole: a part with one of its holes lined up on another part's hole
    /// (faces flush, the two parts' hole grids lined up in steps), a screw seated in a hole, a nut on a screw.
    /// </summary>
    public static class HoleSnap
    {
        /// <summary>The face of a hole that faces the viewer (a blind hole only opens one way), and its outward normal.</summary>
        public static void NearFace(HoleAt hole, Vector3 viewer, out Vector3 face, out Vector3 outward)
        {
            outward = !hole.local.through || Vector3.Dot(viewer - hole.center, hole.axis) < 0f ? -hole.axis : hole.axis;
            face = hole.center + outward * (hole.Depth / 2f);
        }

        /// <summary>
        /// The pose that puts the held part's <paramref name="anchor"/> hole (part space) on the target hole's near
        /// face. Of the two ways round (either face of the anchor hole can touch), it picks the one that keeps the
        /// held part's body (<paramref name="bodyCentre"/>, part space) out of the target; <paramref name="flip"/>
        /// takes the other. <paramref name="handRotation"/> is how it's held, which decides the closest way to
        /// turn it; <paramref name="twist"/> turns it about the hole after its grid is lined up with the target's
        /// in <paramref name="step"/> degree steps.
        /// </summary>
        public static void PlacePart(Hole anchor, Vector3 bodyCentre, Quaternion handRotation, HoleAt target, Vector3 viewer, bool flip,
            float twist, float step, out Vector3 position, out Quaternion rotation)
        {
            NearFace(target, viewer, out var face, out var outward);
            Solve(anchor, 1f, handRotation, target, face, outward, twist, step, out var positionA, out var rotationA);
            Solve(anchor, -1f, handRotation, target, face, outward, twist, step, out var positionB, out var rotationB);
            var outsideA = Vector3.Dot(positionA + rotationA * bodyCentre - face, outward);
            var outsideB = Vector3.Dot(positionB + rotationB * bodyCentre - face, outward);
            var useA = outsideA >= outsideB - 1e-4f;
            if (flip)
                useA = !useA;
            position = useA ? positionA : positionB;
            rotation = useA ? rotationA : rotationB;
        }

        static void Solve(Hole anchor, float sign, Quaternion handRotation, HoleAt target, Vector3 face, Vector3 outward,
            float twist, float step, out Vector3 position, out Quaternion rotation)
        {
            var localOut = anchor.axis * sign;  // part-space direction that will point out of the target's face
            var touching = anchor.center - localOut * (anchor.depth / 2f);
            var aligned = Quaternion.FromToRotation(handRotation * localOut, outward) * handRotation;

            var reference = GridDirection(target.part.transform.rotation, outward);
            var mine = GridDirection(aligned, outward);
            var angle = Vector3.SignedAngle(reference, mine, outward);
            var snapped = Mathf.Round(angle / step) * step;
            rotation = Quaternion.AngleAxis(snapped - angle + twist, outward) * aligned;
            position = face - rotation * touching;
        }

        /// <summary>The part's own axis lying most flat in the plane with this normal, flattened onto it.</summary>
        static Vector3 GridDirection(Quaternion rotation, Vector3 normal)
        {
            var best = Vector3.zero;
            var bestDot = 2f;
            foreach (var axis in new[] { Vector3.right, Vector3.up, Vector3.forward })
            {
                var world = rotation * axis;
                var d = Mathf.Abs(Vector3.Dot(world, normal));
                if (d < bestDot - 1e-3f)
                {
                    bestDot = d;
                    best = world;
                }
            }
            return Vector3.ProjectOnPlane(best, normal).normalized;
        }

        /// <summary>A screw seated in a hole: head on the near face, pointing in.</summary>
        public static void PlaceScrew(Fastener screw, Quaternion handRotation, HoleAt target, Vector3 viewer, out Vector3 position, out Quaternion rotation)
        {
            NearFace(target, viewer, out var face, out var outward);
            rotation = Quaternion.FromToRotation(handRotation * screw.Axis, -outward) * handRotation;
            position = face - rotation * screw.Seat;
        }

        /// <summary>A nut on a screw, its face <paramref name="along"/> metres down the screw from under the head.</summary>
        public static void PlaceNut(Fastener nut, Quaternion handRotation, Fastener screw, float along, out Vector3 position, out Quaternion rotation)
        {
            var axis = screw.WorldAxis;
            rotation = Quaternion.FromToRotation(handRotation * nut.Axis, axis) * handRotation;
            position = screw.WorldSeat + axis * along - rotation * nut.Seat;
        }

        /// <summary>
        /// The parts a screw passes through between its head and tip: those with a hole on its line. Each comes
        /// with the hole and how far down the screw that hole's far face is.
        /// </summary>
        public static List<(PartBody part, HoleAt hole, float far)> Stack(Vector3 seat, Vector3 axis, float length, PartBody screw)
        {
            var result = new List<(PartBody, HoleAt, float)>();
            var seen = new HashSet<PartBody>();
            foreach (var collider in Physics.OverlapCapsule(seat, seat + axis * length, 0.004f, ~0, QueryTriggerInteraction.Ignore))
            {
                var part = collider.GetComponentInParent<PartBody>();
                if (part == null || part == screw || !seen.Add(part) || !part.TryGetComponent(out PartHoles holes))
                    continue;
                foreach (var hole in holes.WorldHoles())
                {
                    if (Mathf.Abs(Vector3.Dot(hole.axis, axis)) < 0.97f)
                        continue;
                    var offset = hole.center - seat;
                    var along = Vector3.Dot(offset, axis);
                    if ((offset - axis * along).magnitude > 0.0015f)
                        continue;
                    var half = hole.Depth / 2f;
                    if (along + half < -0.0005f || along - half > length + 0.0005f)
                        continue;
                    result.Add((part, hole, along + half));
                    break;
                }
            }
            result.Sort((x, y) => x.Item3.CompareTo(y.Item3));
            return result;
        }
    }
}
