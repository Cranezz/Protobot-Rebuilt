using System.Collections.Generic;
using Protobot.Parts;
using UnityEngine;

namespace Protobot.Building
{
    /// <summary>
    /// A screw that's been put in: the parts it passes through (head first), and its nut. Without a nut the
    /// screw only holds the stack lined up (tacked); a nut, or a threaded hole in the stack (a standoff, a
    /// motor), clamps the stack into one rigid piece.
    /// </summary>
    public class ScrewJoint : MonoBehaviour
    {
        readonly List<PartBody> stack = new();
        float stackEnd;   // how far down the screw the last wall's far face is
        bool threaded;

        public PartBody Nut { get; private set; }
        public IReadOnlyList<PartBody> StackParts => stack;
        public float StackEnd => stackEnd;
        public bool Fastened => Nut != null || threaded;
        Fastener Screw => GetComponent<Fastener>();
        PartBody Part => GetComponent<PartBody>();

        /// <summary>
        /// Puts a seated screw in: finds what it passes through and joins them. Returns the joint, or null if
        /// the screw doesn't go through anything.
        /// </summary>
        public static ScrewJoint Insert(PartBody screw)
        {
            if (!screw.TryGetComponent(out Fastener fastener))
                return null;
            var found = HoleSnap.Stack(fastener.WorldSeat, fastener.WorldAxis, fastener.Length, screw);
            if (found.Count == 0)
                return null;
            var joint = screw.gameObject.GetComponent<ScrewJoint>() ?? screw.gameObject.AddComponent<ScrewJoint>();
            joint.stack.Clear();
            joint.threaded = false;
            joint.stackEnd = 0f;
            foreach (var (part, hole, far) in found)
            {
                joint.stack.Add(part);
                joint.stackEnd = Mathf.Max(joint.stackEnd, far);
                joint.threaded |= hole.local.kind == HoleKind.Threaded;
            }
            joint.Rejoin();
            return joint;
        }

        /// <summary>Room left on the screw past the stack for a nut this thick.</summary>
        public bool FitsNut(float thickness) => stackEnd + thickness <= Screw.Length + 0.0006f;

        public void AddNut(PartBody nut)
        {
            Nut = nut;
            Rejoin();
        }

        /// <summary>The nut came off: the stack is only tacked again (unless it's threaded).</summary>
        public void RemoveNut()
        {
            var nut = Nut;
            Nut = null;
            if (nut != null)
                Assembly.Unlink(nut);
            Rejoin();
        }

        /// <summary>The screw came out: everything it held lets go of it.</summary>
        public void Remove()
        {
            if (Nut != null)
                Assembly.Unlink(Nut);
            Nut = null;
            Assembly.Unlink(Part);
            stack.Clear();
            Destroy(this);
        }

        void Rejoin()
        {
            var kind = Fastened ? LinkKind.Clamp : LinkKind.Tack;
            foreach (var part in stack)
                if (part != null)
                    Assembly.Join(Part, part, kind);
            if (Nut != null)
                Assembly.Join(Nut, Part, LinkKind.Clamp);
            if (kind == LinkKind.Tack)
                Downgrade();
        }

        /// <summary>Clamp links of this screw become tacks again (its nut came off).</summary>
        void Downgrade()
        {
            var assembly = Assembly.Of(Part);
            if (assembly != null)
                assembly.Loosen(Part);
        }
    }
}
