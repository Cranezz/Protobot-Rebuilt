using UnityEngine;

namespace Protobot.Parts
{
    /// <summary>
    /// The geometry that matters for joining parts, measured from the model when the part library is built.
    /// A screw: where the underside of its head is, which way it points, and how far its thread reaches.
    /// A nut: one face's centre, the way through it, and its thickness.
    /// </summary>
    public class Fastener : MonoBehaviour
    {
        [SerializeField] PartRole role;
        [Tooltip("Screw: centre of the head's underside. Nut: centre of one face. Part space.")]
        [SerializeField] Vector3 seat;
        [Tooltip("Screw: from the head towards the tip. Nut: through it from the seat face. Part space, unit.")]
        [SerializeField] Vector3 axis = Vector3.forward;
        [Tooltip("Screw: from under the head to the tip. Nut: its thickness. Metres.")]
        [SerializeField] float length;
        [SerializeField] float diameter;
        [SerializeField] float headDiameter;

        public PartRole Role => role;
        public Vector3 Seat => seat;
        public Vector3 Axis => axis;
        public float Length => length;
        public float Diameter => diameter;
        public float HeadDiameter => headDiameter;

        public Vector3 WorldSeat => transform.TransformPoint(seat);
        public Vector3 WorldAxis => transform.TransformDirection(axis).normalized;
        /// <summary>Screw: the tip. Nut: the centre of its far face.</summary>
        public Vector3 WorldTip => WorldSeat + WorldAxis * length;

        public void Configure(PartRole newRole, Vector3 newSeat, Vector3 newAxis, float newLength, float newDiameter, float newHeadDiameter)
        {
            role = newRole;
            seat = newSeat;
            axis = newAxis.normalized;
            length = newLength;
            diameter = newDiameter;
            headDiameter = newHeadDiameter;
        }
    }
}
