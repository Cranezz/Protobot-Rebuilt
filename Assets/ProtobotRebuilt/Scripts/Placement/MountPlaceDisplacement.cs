using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Protobot.Motion;
using Protobot.Transformations;
using UnityEngine;

namespace Protobot {
    // V2: VEX's pneumatic mounts go where they belong, from the parts list or the Move tool:
    // - the Cylinder Mount (a U clevis) onto a cylinder's rod end: its base hole on the rod's thread, the base against
    //   the rod's shoulder, the clevis pointing out along the rod (a Piston Nut on the thread holds it);
    // - the Cylinder Rod Pivot (a bracket with a tab between two ears) onto a cylinder's rear clevis: its ears either
    //   side of the clevis, their holes on the clevis pin's line, its base plate behind the cylinder.
    public class MountPlaceDisplacement : PlaceDisplacement {
        private MouseCast mouseCast;
        public override bool ModifyRotation => true;

        public static void AddTo(Placement placement, List<PlaceDisplacement> list) {
            var displacement = placement.gameObject.AddComponent<MountPlaceDisplacement>();
            foreach (var other in list)
                if (other is HolePlaceDisplacement hole)
                    displacement.mouseCast = typeof(HolePlaceDisplacement).GetField("mouseCast", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(hole) as MouseCast;
            list.Insert(0, displacement);
        }

        private enum Kind { None, ClevisMount, RearPivot }

        private static Kind KindOf(string id) =>
            id == null ? Kind.None : id.EndsWith("-Cylinder Mount") ? Kind.ClevisMount : id.EndsWith("-Cylinder Rod Pivot") ? Kind.RearPivot : Kind.None;

        public override bool TryGetDisplacement(PlacementData placementData, out Displacement displacement) {
            displacement = null;
            if (mouseCast == null || mouseCast.gameObject == null) return false;
            string id = null;
            GameObject self = null;
            Mesh mesh = null;
            if (placementData is PartPlacementData part && part.partGenerator != null) {
                id = part.partGenerator.GetId();
                mesh = part.partGenerator.GetMesh();
            }
            else if (placementData.TryParse(out GameObjectPlacementData moving) && moving.GetGameObject() != null && moving.GetGameObject().TryGetComponent(out SavedObject saved)) {
                self = moving.GetGameObject();
                id = saved.id;
                mesh = self.GetComponent<MeshFilter>()?.sharedMesh;
            }
            var cylinder = mouseCast.gameObject.GetComponentInParent<PneumaticCylinder>();
            if (cylinder == null || cylinder.gameObject == self) return false;
            return For(id, mesh, cylinder, out displacement);
        }

        /// <summary>Where a mount (by its id and model) goes on this cylinder.</summary>
        public static bool For(string id, Mesh mesh, PneumaticCylinder cylinder, out Displacement displacement) {
            displacement = null;
            var kind = KindOf(id);
            if (kind == Kind.None || mesh == null || cylinder == null) return false;

            if (kind == Kind.ClevisMount) {
                var thread = cylinder.Thread;
                if (thread == null) return false;
                // The base hole (the hole along the clevis's length) and the base's face toward the cylinder.
                if (!BaseOf(mesh, out var basePoint)) return false;
                var turn = Quaternion.LookRotation(thread.transform.forward, thread.transform.up);
                displacement = new Displacement(thread.transform.position - turn * basePoint, turn);
                return true;
            }
            // The rear clevis: the cylinder body's two pin holes (plain, across it), their middle and line.
            var pins = cylinder.GetComponentsInChildren<HoleCollider>(true)
                .Where(h => h.transform.parent == cylinder.transform && h.holeType == HoleCollider.HoleType.Normal).ToList();
            if (pins.Count < 2) return false;
            var middle = (pins[0].transform.position + pins[1].transform.position) / 2f;
            var pinLine = (pins[1].transform.position - pins[0].transform.position).normalized;
            if (pinLine.sqrMagnitude < 0.5f) pinLine = pins[0].transform.forward;
            // Behind the cylinder: away from its rod.
            var rodWay = cylinder.Thread != null ? (cylinder.Thread.transform.position - middle).normalized : cylinder.transform.right;
            var back = Vector3.ProjectOnPlane(-rodWay, pinLine).normalized;
            if (!EarsOf(mesh, out var earMiddle, out var earAxis, out var plateWay)) return false;
            // Its ears' axis on the pin line, its plate behind.
            var local = Quaternion.LookRotation(plateWay, earAxis);
            var world = Quaternion.LookRotation(back, pinLine);
            var rotation = world * Quaternion.Inverse(local);
            displacement = new Displacement(middle - rotation * earMiddle, rotation);
            return true;
        }

        private static readonly Dictionary<Mesh, Vector3> bases = new();

        // The clevis mount's base: on its long axis (+z in VEX's model), at the base's outer face.
        private static bool BaseOf(Mesh mesh, out Vector3 point) {
            if (bases.TryGetValue(mesh, out point)) return true;
            if (!mesh.isReadable) return false;
            var b = mesh.bounds;
            point = new Vector3(b.center.x, b.center.y, b.max.z);
            bases[mesh] = point;
            return true;
        }

        private static readonly Dictionary<Mesh, (Vector3 middle, Vector3 axis, Vector3 plate)> ears = new();

        // The rod pivot's ears: in VEX's model the pin holes are along y either side (y = ±0.31), at z = -0.09; its
        // base plate (with its two mounting holes) is at +z.
        private static bool EarsOf(Mesh mesh, out Vector3 middle, out Vector3 axis, out Vector3 plate) {
            if (ears.TryGetValue(mesh, out var known)) {
                (middle, axis, plate) = known;
                return true;
            }
            var b = mesh.bounds;
            middle = new Vector3(b.center.x, b.center.y, -0.091f);
            axis = Vector3.up;
            plate = Vector3.forward;
            ears[mesh] = (middle, axis, plate);
            return true;
        }
    }
}
