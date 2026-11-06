using System.Collections.Generic;
using Protobot.StateSystems;
using UnityEngine;

namespace Protobot.Tools {
    // V2: where the Transform tool's gizmo sits and which way its axes point: at the middle of the part (a long
    // channel's middle, not its first hole), or of the whole group when it's screwed to others; a selected hole
    // keeps it on the hole. The arrows and rings move and turn the group about that point. Kept in the part's own space, so it doesn't drift
    // as the group turns.
    public static class TransformGizmo {
        private static GameObject measured;
        private static Vector3 localCenter;
        private static bool dirty = true;

        /// <summary>World offset from the moving part's position to the gizmo.</summary>
        public static Vector3 Offset { get; private set; }

        /// <summary>The gizmo's axes: the part's own when the Transform tool is set to local.</summary>
        public static Quaternion Frame { get; set; } = Quaternion.identity;

        [RuntimeInitializeOnLoadMethod]
        private static void Init() {
            StateSystem.OnChangeState += () => dirty = true;
        }

        public static void MarkDirty() => dirty = true;

        /// <summary>The gizmo position for a selected object (and updates <see cref="Offset"/>).</summary>
        public static Vector3 PositionFor(GameObject obj) {
            if (obj != measured || dirty) {
                measured = obj;
                dirty = false;
                localCenter = MeasureCenter(obj);
            }
            Offset = obj.transform.rotation * localCenter;
            return obj.transform.position + Offset;
        }

        private static Vector3 MeasureCenter(GameObject obj) {
            if (!obj.TryGetComponent(out SavedObject _))
                return Vector3.zero;  // a pivot of several selected parts already sits at their middle
            // V2: on a joint to a grounded part it can only turn about the joint: the gizmo sits on it.
            var pin = Motion.Linkage.GroundPin(obj);
            if (pin.HasValue)
                return Vector3.Scale(obj.transform.InverseTransformPoint(pin.Value), obj.transform.lossyScale);
            List<GameObject> group = obj.GetConnectedObjects(true);
            if (group == null || group.Count == 0)
                group = new List<GameObject> { obj };

            var root = obj.transform;
            var bounds = new Bounds();
            var any = false;
            foreach (var part in group) {
                foreach (var renderer in part.GetComponentsInChildren<MeshRenderer>()) {
                    var local = renderer.localBounds;
                    for (var i = 0; i < 8; i++) {
                        var corner = local.center + Vector3.Scale(local.extents,
                            new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                        var point = root.InverseTransformPoint(renderer.transform.TransformPoint(corner));
                        if (!any) {
                            bounds = new Bounds(point, Vector3.zero);
                            any = true;
                        }
                        else
                            bounds.Encapsulate(point);
                    }
                }
            }
            // InverseTransformPoint undoes the part's scale too; the offset is applied with rotation only.
            return any ? Vector3.Scale(bounds.center, root.lossyScale) : Vector3.zero;
        }

        private const string MoveStepKey = "TransformMoveStep", TurnStepKey = "TransformTurnStep";

        /// <summary>Snap step for moves, in inches (1/8" unless changed in the tool options).</summary>
        public static float MoveStep {
            get => Mathf.Max(1f / 64f, PlayerPrefs.GetFloat(MoveStepKey, 0.125f));
            set => PlayerPrefs.SetFloat(MoveStepKey, Mathf.Clamp(value, 1f / 64f, 48f));
        }

        /// <summary>Snap step for turns, in degrees (5° unless changed).</summary>
        public static float TurnStep {
            get => Mathf.Clamp(PlayerPrefs.GetFloat(TurnStepKey, 5f), 0.5f, 180f);
            set => PlayerPrefs.SetFloat(TurnStepKey, Mathf.Clamp(value, 0.5f, 180f));
        }

        /// <summary>Rounds a move to <see cref="MoveStep"/> along the gizmo's own axes (local) or the world's (global).</summary>
        public static Vector3 SnapDelta(Vector3 delta, Vector3? alongAxis = null) {
            var step = MoveStep;
            if (alongAxis.HasValue) {
                var axis = alongAxis.Value.normalized;
                return axis * Mathf.Round(Vector3.Dot(delta, axis) / step) * step;
            }
            var local = Quaternion.Inverse(Frame) * delta;
            return Frame * local.Round(step);
        }
    }
}
