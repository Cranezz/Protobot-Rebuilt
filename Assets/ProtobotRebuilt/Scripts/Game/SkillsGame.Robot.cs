using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Protobot.Game {
    // V2: what of the build plays, as on a real field: the robot is its V5 Brain and everything connected to it (by
    // screws, shafts, chains, bands, cables: Connected); parts floating elsewhere in the build stay behind (hidden while
    // the game is open). Game elements saved in the build don't become extra pieces: one Pin touching the robot is the
    // Preload (where it's placed); more than one, or a Cup or Goal, has to go before a run (the rules allow one Preload).
    public partial class SkillsGame {
        private readonly List<GameObject> leftBehind = new();

        /// <summary>The Preload's pose when it isn't at one of the standard spots: the build's own Pin, or where it was
        /// dragged to (SetupTools' Preload mode). World, round the robot (the robot never moves in a game).</summary>
        public (Vector3 position, Quaternion rotation)? customPreload;

        /// <summary>The game elements saved in the build that touch the robot (the Preload when there's one Pin).</summary>
        public readonly List<GameObject> buildElements = new();

        /// <summary>Set when the build's game elements break the rules (shown in the setup panel; a run is refused).</summary>
        public string ElementProblem => elementProblem ?? (buildPinColors != null && buildPinColors != PreloadColors
            ? $"The Pin on your robot is {LoadName(buildPinColors)}: the Preload is a {LoadName(PreloadColors)} ({(Red ? "red" : "blue")} Alliance). Change it in the build"
            : null);
        private string elementProblem, buildPinColors;

        /// <summary>Parts left out because nothing connects them to the Brain.</summary>
        public int LeftOutCount { get; private set; }

        public static IEnumerable<GameObject> LeftBehindParts => Current != null ? Current.leftBehind.Where(p => p != null).ToList() : new List<GameObject>();

        public static bool IsGameElement(string id) =>
            id.StartsWith("OVPN") || id.StartsWith("OVCP") || id.StartsWith("OVGL") || id.StartsWith("OVSG") || id.StartsWith("OVTG") || id.StartsWith("OVAG")
            || id.StartsWith("BLOK") || id.StartsWith("RING") || id.StartsWith("DISC");

        private static bool IsFieldPart(string id) => id.StartsWith("OVFL") || id.StartsWith("OVLD") || id.StartsWith("OVTO") || id.StartsWith("PUBA")
                                                      || id.StartsWith("LGOL") || id.StartsWith("CGOL") || id.StartsWith("LOAD") || id.StartsWith("FELD") || id.StartsWith("SAKE");

        public static bool IsBrain(SavedObject s) => s.id == "BRAN" || s.id.StartsWith("BRAN-") || (s.id.Contains("Brain") && !s.id.Contains("Mount") && !s.id.StartsWith("SNSR"));

        /// <summary>The parts that play: the Brain's connected group. Null (and why) when there's no Brain.</summary>
        private static List<SavedObject> RobotOf(List<SavedObject> parts, out string problem) {
            problem = null;
            var brain = parts.FirstOrDefault(IsBrain);
            if (brain == null) {
                problem = "Put a V5 Brain on your robot: on the field the robot is the Brain and everything connected to it";
                return null;
            }
            var keep = Connected(brain.gameObject, parts);
            return parts.Where(p => keep.Contains(p.gameObject) && !IsGameElement(p.id) && !IsFieldPart(p.id)).ToList();
        }

        /// <summary>Everything connected to `start` some way: fastened (GroupManager), a screw's, shaft's or standoff's
        /// holes (HoleDetector: joints and pins too), a shaft and what turns on it and in (GearTrain's rotors: the arm on
        /// its shaft, wheels), a chain and its sprockets, a band, tube or cable and what it's on.</summary>
        private static HashSet<GameObject> Connected(GameObject start, List<SavedObject> parts) {
            var links = new Dictionary<GameObject, HashSet<GameObject>>();
            void Link(GameObject a, GameObject b) {
                if (a == null || b == null || a == b) return;
                if (!links.TryGetValue(a, out var la)) links[a] = la = new HashSet<GameObject>();
                if (!links.TryGetValue(b, out var lb)) links[b] = lb = new HashSet<GameObject>();
                la.Add(b);
                lb.Add(a);
            }
            GameObject Owner(Component c) => c == null ? null : c.GetComponentInParent<SavedObject>()?.gameObject;
            foreach (var p in parts) {
                foreach (var n in GroupManager.Neighbours(p.gameObject)) Link(p.gameObject, n);
                foreach (var detector in p.GetComponentsInChildren<HoleDetector>(true))
                    foreach (var hole in detector.holes)
                        if (hole != null) Link(p.gameObject, Owner(hole));
                foreach (var connecting in p.GetComponents<ConnectingPart>()) {
                    List<GameObject> attached = null;
                    try { attached = connecting.GetAttachedParts(); } catch { }
                    if (attached != null) foreach (var a in attached) Link(p.gameObject, a);
                }
                if (p.TryGetComponent(out Motion.ElasticBand band)) foreach (var post in band.posts) Link(p.gameObject, post.part);
                if (p.TryGetComponent(out Motion.PneumaticTube tube)) foreach (var node in tube.nodes) Link(p.gameObject, node.part);
            }
            try {
                foreach (var rotor in Motion.GearTrain.Rotors()) {
                    foreach (var part in rotor.parts) Link(rotor.shaft, part);
                    foreach (var (bearing, _) in rotor.bearings) Link(rotor.shaft, bearing);
                }
            }
            catch (System.Exception e) { Debug.LogWarning("V2: robot connections (rotors): " + e.Message); }
            var seen = new HashSet<GameObject> { start };
            var queue = new Queue<GameObject>();
            queue.Enqueue(start);
            while (queue.Count > 0) {
                var at = queue.Dequeue();
                if (!links.TryGetValue(at, out var next)) continue;
                foreach (var n in next)
                    if (n != null && seen.Add(n)) queue.Enqueue(n);
            }
            return seen;
        }

        /// <summary>Hides what doesn't play (put back by RestoreLeftBehind); notes the build's game elements on the robot.</summary>
        private void LeaveBehind(List<SavedObject> all, List<SavedObject> robot) {
            var robotSet = new HashSet<SavedObject>(robot);
            var bounds = robot.SelectMany(p => p.GetComponentsInChildren<Renderer>()).Where(r => r.enabled).Select(r => r.bounds).ToList();
            buildElements.Clear();
            foreach (var p in all) {
                if (robotSet.Contains(p)) continue;
                if (IsGameElement(p.id)) {
                    var b = BoundsOf(p.gameObject);
                    b.Expand(0.1f);
                    if (bounds.Any(r => r.Intersects(b))) buildElements.Add(p.gameObject);
                }
                else LeftOutCount++;
                p.gameObject.SetActive(false);
                leftBehind.Add(p.gameObject);
            }
            elementProblem = null;
            buildPinColors = null;
            var pins = buildElements.Where(e => e.GetComponent<SavedObject>().id.StartsWith("OVPN")).ToList();
            if (buildElements.Count > 1)
                elementProblem = $"Your build has {buildElements.Count} game elements on the robot: only one Pin (the Preload) may start on it. Delete the others in the build";
            else if (buildElements.Count == 1 && pins.Count == 0)
                elementProblem = "Only a Pin can be the Preload (SG5): take the " + Interface.PartLabels.Short(buildElements[0]) + " off the robot in the build";
            if (pins.Count == 1) {
                // It's the Preload, where it's placed.
                customPreload = (BoundsOf(pins[0]).center, PinRotation(pins[0]));
                buildPinColors = BuildPinColors(pins[0]);
                preload = true;
            }
        }

        private List<Bounds> robotBoxes;

        /// <summary>V2: the Preload drawn at a pose being tried (null: back where it is).</summary>
        public void ShowPreloadAt((Vector3, Quaternion)? pose) {
            var keep = customPreload;
            if (pose != null) customPreload = pose;
            PlacePreloadGhost();
            customPreload = keep;
        }

        /// <summary>V2: the Preload placed by hand on the robot (kept for this build).</summary>
        public void SetCustomPreload((Vector3, Quaternion) pose) {
            customPreload = pose;
            preload = true;
            PlacePreloadGhost();
            Changed?.Invoke();
        }

        /// <summary>V2: back to one of the standard spots.</summary>
        public void ClearCustomPreload() {
            customPreload = null;
            PlacePreloadGhost();
            Changed?.Invoke();
        }

        /// <summary>V2: whether any part of the robot touches a Toggle (its bar or bracket) where it stands now: each
        /// part's box against the Toggle's own box in the Toggle's frame. (It was a guess from the footprint: any robot
        /// over 11.5" tall near the middle of its wall failed, touching or not.)</summary>
        public bool TouchesToggle() {
            if (field == null) return false;
            robotBoxes ??= FindObjectsByType<SavedObject>(FindObjectsSortMode.None).Where(p => p.gameObject.activeInHierarchy && !p.gameObject.IsDeleted())
                .SelectMany(p => p.GetComponentsInChildren<MeshRenderer>()).Where(r => r.enabled && r.gameObject.layer != HoleCollider.HOLE_COLLISIONS_LAYER)
                .Select(r => r.bounds).ToList();
            foreach (var toggle in field.ToggleRenderers()) {
                if (toggle == null) continue;
                var local = toggle.localBounds;
                local.Expand(0.04f);
                var t = toggle.transform;
                foreach (var box in robotBoxes) {
                    if (!box.Intersects(toggle.bounds)) continue;
                    var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                    var max = -min;
                    for (var i = 0; i < 8; i++) {
                        var corner = new Vector3((i & 1) == 0 ? box.min.x : box.max.x, (i & 2) == 0 ? box.min.y : box.max.y, (i & 4) == 0 ? box.min.z : box.max.z);
                        var p = t.InverseTransformPoint(corner);
                        min = Vector3.Min(min, p);
                        max = Vector3.Max(max, p);
                    }
                    var b = new Bounds();
                    b.SetMinMax(min, max);
                    if (b.Intersects(local)) return true;
                }
            }
            return false;
        }

        private void RestoreLeftBehind() {
            foreach (var p in leftBehind)
                if (p != null) p.SetActive(true);
            leftBehind.Clear();
        }

        private static Bounds BoundsOf(GameObject go) {
            var rs = go.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        /// <summary>A build Pin's colors as the field names them ("Red-Neutral"...), from its part name.</summary>
        private static string BuildPinColors(GameObject pin) {
            var id = pin.GetComponent<SavedObject>().id;
            bool red = id.Contains("Red"), blue = id.Contains("Blue"), yellow = id.Contains("Yellow");
            if (red && yellow) return "Red-Neutral";
            if (blue && yellow) return "Blue-Neutral";
            if (red && blue) return "Red-Blue";
            if (yellow) return "Neutral-Neutral";
            return red ? "Red-Neutral" : blue ? "Blue-Neutral" : null;
        }

        // A field pin stands along its local z (PreloadPose): the build Pin's long axis (its mesh's longest side) is that.
        private static Quaternion PinRotation(GameObject pin) {
            var filter = pin.GetComponentInChildren<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) return pin.transform.rotation;
            var size = filter.sharedMesh.bounds.size;
            var local = size.x >= size.y && size.x >= size.z ? Vector3.right : size.y >= size.z ? Vector3.up : Vector3.forward;
            var along = filter.transform.TransformDirection(local).normalized;
            var side = Vector3.Cross(along, Mathf.Abs(Vector3.Dot(along, Vector3.up)) > 0.9f ? Vector3.forward : Vector3.up);
            return Quaternion.LookRotation(along, Vector3.Cross(side, along));
        }
    }
}
