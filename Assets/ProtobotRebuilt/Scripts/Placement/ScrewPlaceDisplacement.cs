using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Protobot.Transformations;

namespace Protobot {
    public class ScrewPlaceDisplacement : PlaceDisplacement {
        [SerializeField] private HoleFace currentHoleFace;
        public override bool ModifyRotation => true;

        // V2: an 8-32 screw's shank. In a hole with room around it (a slot, a big square or round hole) the screw
        // follows the mouse inside the hole instead of sitting in the middle, like it would slide in real life.
        public const float ScrewDiameter = 0.164f;
        private const float MinimumPlay = 0.02f;

        public override bool TryGetDisplacement(PlacementData placementData, out Displacement displacement) {
            if (currentHoleFace.direction != Vector3.zero) // this has to return true if it doesn't this script wont do anything
            {
                //TODO: have a list of ids that are checked for screw placement instead of hardcoding it for better compatibility in the future
                // V2: a pneumatic fitting threads only into a port its size (a threaded hole within 0.05").
                if (FittingOf(placementData, out var thread)) {
                    var hole = currentHoleFace.hole;
                    if (!currentHoleFace.gameObject.activeInHierarchy || hole == null || hole.source == null || !hole.source.TryGetComponent(out HoleCollider port) || port.holeType != HoleCollider.HoleType.Threaded
                        || Mathf.Abs(Mathf.Min(hole.size.x, hole.size.y) - thread) > (thread > 0.3f ? 0.05f : 0.02f)) {   // V2: M5 apart from a 4mm socket
                        displacement = null;
                        return false;
                    }
                    displacement = new Displacement(currentHoleFace.position, currentHoleFace.LookRotation);
                    return true;
                }
                var shoulder = ShoulderOf(placementData);   // V2: VEX shoulder screws
                if (StudOf(placementData, out var insert) && currentHoleFace.gameObject.activeInHierarchy) {
                    // V2: a threaded stud goes in tip first: half a coupler, or the rubber link's stud (its rubber on
                    // the face); the rest stands out for the next standoff or part.
                    var inward = -currentHoleFace.direction.normalized;
                    displacement = new Displacement(currentHoleFace.position + SlideOffset(currentHoleFace) + inward * insert, Quaternion.LookRotation(inward, currentHoleFace.transform.up));
                    return true;
                }
                if (placementData.objectId.Contains("SCRW") || placementData.objectId.Contains("RBMP") || shoulder > 0f) // || is OR in C# so in coding if I have a integer and I want it to return true only if the value is 1 or 2 I would do if (value == 1 || value == 2)
                {
                    if (currentHoleFace.gameObject.activeInHierarchy)
                    {
                        var rise = shoulder > 0f ? ShoulderRise(currentHoleFace, shoulder, ShankOf(placementData)) : 0f;
                        displacement = new Displacement(currentHoleFace.position + SlideOffset(currentHoleFace) + currentHoleFace.direction.normalized * rise, currentHoleFace.LookRotation);
                        return true;
                    }
                }
            }

            displacement = null;
                return false;
        }

        // V2: a shoulder screw's smooth shoulder (0.18" across) goes into plain holes like the rest of the screw, but not
        // into a thread: in a standoff, a coupler, a nut or a tapped hole the shoulder's end stops on it and only the
        // thread goes in (the head stands off the face by what's left of the shoulder; a part on the shoulder pivots).
        private static float ShoulderOf(PlacementData data) {
            if (data is PartPlacementData part && part.partGenerator is ChildPartGenerator && ChildPartGenerator.IsShoulderScrew(part.partGenerator.GetId()))
                return ChildPartGenerator.ShoulderLength(part.partGenerator.GetId());
            if (data.TryParse(out GameObjectPlacementData moving) && moving.GetGameObject() != null && moving.GetGameObject().TryGetComponent(out SavedObject saved)
                && ChildPartGenerator.IsShoulderScrew(saved.id))
                return ChildPartGenerator.ShoulderLength(saved.id);
            return 0f;
        }

        // A stud being placed, and how far its tip goes in: a coupler half its length, the rubber link its stud.
        private static readonly Dictionary<Mesh, float> linkStuds = new();

        private static bool FittingOf(PlacementData data, out float diameter) {
            diameter = 0f;
            Mesh mesh = null;
            if (data is PartPlacementData part && part.partGenerator != null) mesh = part.partGenerator.GetMesh();
            else if (data.TryParse(out GameObjectPlacementData moving) && moving.GetGameObject() != null) mesh = moving.GetGameObject().GetComponent<MeshFilter>()?.sharedMesh;
            return mesh != null && ChildPartGenerator.fittingDiameters.TryGetValue(mesh, out diameter);
        }

        private static bool StudOf(PlacementData data, out float insert) {
            insert = 0f;
            Mesh mesh = null;
            string id = null;
            if (data is PartPlacementData part && part.partGenerator != null) {
                id = part.partGenerator.GetId();
                mesh = part.partGenerator.GetMesh();
            }
            else if (data.TryParse(out GameObjectPlacementData moving) && moving.GetGameObject() != null && moving.GetGameObject().TryGetComponent(out SavedObject saved)) {
                id = saved.id;
                mesh = moving.GetGameObject().GetComponent<MeshFilter>()?.sharedMesh;
            }
            if (id == null || mesh == null) return false;
            var name = id.IndexOf('-') > 0 ? id.Substring(id.IndexOf('-') + 1) : id;
            if (!ChildPartGenerator.IsStud(id, name)) return false;
            var length = mesh.bounds.size.z;
            insert = length / 2f;
            if (name == "Rubber Link") {
                // The stud: from the tip (z = 0) down to where the rubber starts (wider than the thread).
                if (!linkStuds.TryGetValue(mesh, out var top)) {
                    top = float.MinValue;
                    foreach (var v in mesh.vertices)
                        if (new Vector2(v.x, v.y).magnitude > 0.12f) top = Mathf.Max(top, v.z);
                    linkStuds[mesh] = top;
                }
                insert = top > float.MinValue && top < 0f ? -top : length / 2f;
            }
            return true;
        }

        private static float ShankOf(PlacementData data) {
            if (data is PartPlacementData part && part.partGenerator != null) {
                var mesh = part.partGenerator.GetMesh();
                return mesh != null ? mesh.bounds.size.z - 0.087f : 0.5f;
            }
            if (data.TryParse(out GameObjectPlacementData moving) && moving.GetGameObject() != null && moving.GetGameObject().TryGetComponent(out Screw screw))
                return screw.ScrewLength;
            return 0.5f;
        }

        /// <summary>V2: how far a shoulder screw's head stays off the face: the shoulder's end on the first threaded
        /// hole along the screw (if the screw reaches it), never below the face.</summary>
        public static float ShoulderRise(HoleFace face, float shoulder, float shank) {
            var inward = -face.direction.normalized;
            var start = face.position - inward * 0.01f;
            var hits = Physics.RaycastAll(start, inward, shank + 0.01f, 1 << HoleCollider.HOLE_COLLISIONS_LAYER, QueryTriggerInteraction.Collide);
            var nearest = float.MaxValue;
            foreach (var hit in hits) {
                if (!hit.collider.TryGetComponent(out HoleCollider hole) || hole.holeType != HoleCollider.HoleType.Threaded) continue;
                nearest = Mathf.Min(nearest, hit.distance - 0.01f);
            }
            if (nearest == float.MaxValue) return 0f;
            return Mathf.Max(0f, shoulder - Mathf.Max(0f, nearest));
        }

        /// <summary>V2: where the screw goes in the hole, from its middle: toward the mouse, as far as the hole lets it
        /// (any shape: round, square, a slot, or a custom part's exact outline). Near the middle it snaps to the
        /// middle. While it can move, the hole is outlined with a dot at its middle.</summary>
        public static Vector3 SlideOffset(HoleFace face) {
            var hole = face.hole;
            if (hole == null || Camera.main == null) return Vector3.zero;
            var room = new Vector2(hole.size.x - ScrewDiameter, hole.size.y - ScrewDiameter) / 2f;
            if (room.x < MinimumPlay && room.y < MinimumPlay) return Vector3.zero;
            // The mouse on the hole's face.
            var ray = Camera.main.ScreenPointToRay(MouseInput.Position);
            var plane = new Plane(face.direction, face.position);
            if (!plane.Raycast(ray, out var distance)) return Vector3.zero;
            var offset = ray.GetPoint(distance) - face.position;
            // The hole's sides, as its collider is turned (the face on the far side looks the other way, which would
            // mirror a custom outline).
            var right = hole.rotation * Vector3.right;
            var up = hole.rotation * Vector3.up;
            var local = new Vector2(Vector3.Dot(offset, right), Vector3.Dot(offset, up));
            var round = hole.shape != null && HoleShapes.instance != null && hole.shape != HoleShapes.instance.GetShapeMesh("square");
            var radius = ScrewDiameter / 2f;
            if (hole.outline != null && hole.outline.Length >= 3)
                local = InsideOutline(hole.outline, local, radius);
            else if (round) {
                var reach = Mathf.Min(room.x, room.y);
                if (reach < MinimumPlay) return Vector3.zero;
                local = Vector2.ClampMagnitude(local, reach);
            }
            else
                local = new Vector2(room.x < MinimumPlay ? 0f : Mathf.Clamp(local.x, -room.x, room.x),
                                    room.y < MinimumPlay ? 0f : Mathf.Clamp(local.y, -room.y, room.y));
            // Close to the middle: in the middle.
            var snap = Mathf.Min(0.06f, Mathf.Max(room.x, room.y) * 0.4f);
            if (local.magnitude < snap) local = Vector2.zero;

            DrawHole(face, hole, round, right, up, local);
            return right * local.x + up * local.y;
        }

        // The farthest point toward the mouse, from the outline's middle, where the screw still fits inside it.
        private static Vector2 InsideOutline(Vector2[] outline, Vector2 target, float radius) {
            bool Fits(Vector2 p) {
                if (!Inside(outline, p)) return false;
                for (var i = 0; i < outline.Length; i++)
                    if (Interface.Overlay.DistanceToSegment(p, outline[i], outline[(i + 1) % outline.Length]) < radius) return false;
                return true;
            }
            if (Fits(target)) return target;
            if (!Fits(Vector2.zero)) return Vector2.zero;
            float low = 0f, high = 1f;
            for (var i = 0; i < 14; i++) {
                var mid = (low + high) / 2f;
                if (Fits(target * mid)) low = mid;
                else high = mid;
            }
            return target * low;
        }

        private static bool Inside(Vector2[] polygon, Vector2 p) {
            var inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
                if ((polygon[i].y > p.y) != (polygon[j].y > p.y)
                    && p.x < (polygon[j].x - polygon[i].x) * (p.y - polygon[i].y) / (polygon[j].y - polygon[i].y) + polygon[i].x)
                    inside = !inside;
            return inside;
        }

        // The hole's outline, a dot at its middle and a ring where the screw is, over the scene.
        private static void DrawHole(HoleFace face, HoleData hole, bool round, Vector3 right, Vector3 up, Vector2 screw) {
            Interface.Overlay.Begin();
            var color = Interface.Theme.Accent;
            var center = face.position;
            var outline = new List<Vector3>();
            if (hole.outline != null && hole.outline.Length >= 3)
                foreach (var p in hole.outline) outline.Add(center + right * p.x + up * p.y);
            else if (round)
                for (var i = 0; i < 40; i++) {
                    var a = i * Mathf.PI * 2f / 40f;
                    outline.Add(center + (right * Mathf.Cos(a) * hole.size.x + up * Mathf.Sin(a) * hole.size.y) / 2f);
                }
            else {
                var hx = right * hole.size.x / 2f;
                var hy = up * hole.size.y / 2f;
                outline.AddRange(new[] { center - hx - hy, center + hx - hy, center + hx + hy, center - hx + hy });
            }
            for (var i = 0; i < outline.Count; i++)
                Interface.Overlay.Line(outline[i], outline[(i + 1) % outline.Count], color, 2f);
            if (Interface.Overlay.ToScreen(center, out var middle))
                Interface.Overlay.Disc(middle, screw == Vector2.zero ? 4.5f : 3f, color);
            Interface.Overlay.WorldCircle(center + right * screw.x + up * screw.y, face.direction, ScrewDiameter / 2f, Color.white, 1.5f);
        }
    }
}
