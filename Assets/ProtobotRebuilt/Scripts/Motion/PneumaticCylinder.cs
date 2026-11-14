using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Protobot.StateSystems;
using UnityEngine;

namespace Protobot.Motion {
    // V2: a pneumatic cylinder (the original's "PNMT" part) that works: the body and rod are VEX-CAD's separate
    // models (lined up on the original's), the rod slides out its stroke (25, 50 or 75 mm) and back when
    // extended or retracted (right-click), easing like air does; its threaded end is a screw (RodThread), and
    // what's fastened to it moves with it. A placed cylinder starts retracted; the original's "extended"
    // cylinders in old builds start extended. The state is kept in the part's saved state ("piston:1").
    public class PneumaticCylinder : MonoBehaviour {
        private const float ThreadLength = 0.4f;
        private const float Travel = 0.3f;   // seconds, one way

        public static readonly List<PneumaticCylinder> All = new();

        public float Stroke { get; private set; }
        public bool Extended { get; private set; }
        public bool Moving => moving != null;
        public string Size { get; private set; }

        /// <summary>1, 2... in the order they were added.</summary>
        public int Number => All.Where(c => c != null && c.enabled && !c.gameObject.IsDeleted()).ToList().IndexOf(this) + 1;

        /// <summary>Its given name, or "Cylinder 1".</summary>
        public string Label => Controls.GivenName(gameObject) ?? "Cylinder " + Number;

        /// <summary>The rod (a child, slid along <see cref="LocalAxis"/>), its thread, and where it is (0..1).</summary>
        public Transform Rod => rod;
        public RodThread Thread => thread;
        public Vector3 LocalAxis => axis;
        public float Position => position;

        /// <summary>The rod's threaded end and the way the rod goes out, in the world.</summary>
        public Vector3 RodTip => thread != null ? thread.transform.TransformPoint(Vector3.back * ThreadLength) : transform.position;
        public Vector3 ExtendDirection => transform.TransformDirection(axis).normalized;

        // VEX cylinders: 10 mm bore, 100 psi at most (VEX's rules): 12 lbf pushing out; pulling in, less the rod's
        // area (its model is about 4 mm across).
        public const float BoreMm = 10f, RodMm = 4f, Psi = 100f;
        public static float ExtendForceNewtons => Psi * 6894.76f * Mathf.PI * Mathf.Pow(BoreMm / 2000f, 2f);
        public static float RetractForceNewtons => Psi * 6894.76f * Mathf.PI * (Mathf.Pow(BoreMm / 2000f, 2f) - Mathf.Pow(RodMm / 2000f, 2f));

        private float position;    // 0 retracted .. 1 extended
        private Vector3 axis;      // the rod's way out, in the part's space
        private Transform rod;
        private RodThread thread;
        private MeshRenderer threadMark;
        private Coroutine moving;

        private static readonly Dictionary<string, (Mesh body, Material[] bodyMaterials, Mesh rod, Material[] rodMaterials, Vector3 axis, Vector3 tip)> Models = new();

        private void Awake() {
            All.Add(this);
            if (!TryGetComponent(out SavedObject saved) || saved.id == null) return;
            var ids = saved.id.Split('-');
            if (ids.Length < 2) return;
            Size = ids[1];
            // V2: VEX's legacy (SMC) cylinders, double and single acting, have a 2" stroke.
            Stroke = float.TryParse(Size.Replace("mm", ""), out var mm) ? mm / 25.4f : IsLegacy ? LegacyStroke : 1f;
            if (!Build(Size)) {
                enabled = false;
                return;
            }
            var startExtended = ids.Length > 2 && ids[2] == "Extended";
            SetInstant(startExtended);
        }

        private void OnDestroy() => All.Remove(this);

        public const float LegacyStroke = 2f;
        /// <summary>V2: one of VEX's legacy cylinders ("Legacy Double Acting", "Legacy Single Acting").</summary>
        public bool IsLegacy => Size != null && Size.StartsWith("Legacy");
        /// <summary>V2: a single acting cylinder: air pushes the rod out, a spring brings it back (no front port).</summary>
        public bool SingleActing => Size != null && Size.Contains("Single");

        private void Start() {
            // A loaded build's saved state comes after the part is made. V2: anywhere in its stroke ("piston:0.4").
            var token = Dynamics.Token(gameObject, "piston");
            if (token != null) SetInstant(Parse(token));
        }

        private static float Parse(string token) =>
            token == "1" ? 1f : float.TryParse(token, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f) ? Mathf.Clamp01(f) : 0f;

        // ------------------------------------------------------------------ V2: anywhere in its stroke, and loose

        /// <summary>Which ends of the cylinder get air (its ports with a tube on): both (double acting: the key
        /// extends it, then retracts it), the back only (the key pushes the rod out, then lets the air out: the rod
        /// is loose) or the front only (pulls it in, then loose). Saved as "ports:back" / "ports:front".</summary>
        public enum AirPorts { Both, Back, Front }

        public AirPorts Ports {
            get => Dynamics.Token(gameObject, "ports") switch { "back" => AirPorts.Back, "front" => AirPorts.Front, _ => AirPorts.Both };
            set => Dynamics.SetToken(gameObject, "ports", value == AirPorts.Back ? "back" : value == AirPorts.Front ? "front" : null);
        }

        public static readonly string[] PortNames = { "Both ends", "Back only", "Front only" };

        /// <summary>What its key does in the simulation, for tooltips and messages.</summary>
        public static string KeyAction(AirPorts ports) => ports switch {
            AirPorts.Back => "pushes the rod out, again lets the air out (loose), again pushes it out",
            AirPorts.Front => "pulls the rod in, again lets the air out (loose), again pulls it in",
            _ => "extends it, again retracts it",
        };

        private List<GameObject> manualRiders;
        private List<IElement> manualBefore;
        private bool manualBackwards;
        private float manualStart;

        /// <summary>Starts setting the rod by hand (the rod position window): what's on the rod moves with it.</summary>
        public bool BeginManual() {
            if (moving != null) return false;
            var riders = Riders(out var frame);
            if (frame) {
                Interface.Interface.Say("Can't move the rod: what's on it is also fastened to the frame the cylinder is on", false);
                Audio.Sounds.Play(Audio.Sounds.Error);
                return false;
            }
            manualBackwards = riders.Any(Linkage.IsGrounded);
            if (manualBackwards)
                riders = Dynamics.Fastened(new[] { gameObject }, new[] { thread.gameObject }, null, out _).Where(o => o != null && o.GetComponent<SavedObject>() != null).ToList();
            manualRiders = riders;
            manualBefore = riders.Select(r => (IElement)new ObjectElement(r)).ToList();
            manualBefore.Add(new CylinderElement(this, position));
            manualStart = position;
            return true;
        }

        /// <summary>The rod to here (0 in .. 1 out), never past either end of its stroke.</summary>
        public void ManualTo(float fraction) {
            if (manualRiders == null) return;
            Slide(Mathf.Clamp01(fraction), manualRiders, manualBackwards);
        }

        public void EndManual() {
            if (manualRiders == null) return;
            var changed = Mathf.Abs(position - manualStart) > 0.0005f;
            SetInstant(position);
            if (changed) {
                StateSystem.AddElements(manualBefore);
                var after = manualRiders.Select(r => (IElement)new ObjectElement(r)).ToList();
                after.Add(new CylinderElement(this, position));
                StateSystem.AddState(new State(after));
            }
            manualRiders = null;
            manualBefore = null;
        }

        private bool Build(string size) {
            if (!Models.TryGetValue(size, out var model)) {
                var family = Resources.Load<GameObject>("Part Prefabs/VEX/Pneumatics");
                var body = family != null ? family.transform.Find(size + " Piston (body)") : null;
                var rodModel = family != null ? family.transform.Find(size + " Piston (rod)") : null;
                if (body == null || rodModel == null || !TryGetComponent(out MeshFilter ownFilter)) return false;
                var bodyMesh = body.GetComponent<MeshFilter>().sharedMesh;
                var rodMesh = rodModel.GetComponent<MeshFilter>().sharedMesh;
                // The original's retracted cylinder (body and rod in one) is the shape to line up with.
                var target = ownFilter.sharedMesh;
                var targetMaterials = GetComponent<MeshRenderer>().sharedMaterials;
                foreach (var generator in Resources.LoadAll<PartGenerator>("Part Prefabs"))
                    if (generator.GetComponent<PartType>()?.id == "PNMT")
                        foreach (var data in generator.GetComponentsInChildren<PartData>(true))
                            if (data.param1Value == size && data.param2Value == "Normal") {
                                target = data.GetComponent<MeshFilter>().sharedMesh;
                                targetMaterials = data.GetComponent<MeshRenderer>().sharedMaterials;
                            }
                var points = bodyMesh.vertices.Concat(rodMesh.vertices).ToList();
                var (turn, shift, _) = MeshAlign.Find(points, target.vertices);
                // The rod runs out of the body along the models' main axis (V5: +x; legacy: -y); its tip is its far end.
                var outward = rodMesh.bounds.center - bodyMesh.bounds.center;
                var main = Mathf.Abs(outward.x) >= Mathf.Abs(outward.y) && Mathf.Abs(outward.x) >= Mathf.Abs(outward.z) ? 0 : Mathf.Abs(outward.y) >= Mathf.Abs(outward.z) ? 1 : 2;
                var way = Vector3.zero;
                way[main] = Mathf.Sign(outward[main]);
                var tipPoint = rodMesh.bounds.center;
                tipPoint[main] = way[main] > 0f ? rodMesh.bounds.max[main] : rodMesh.bounds.min[main];
                var movedRod = MeshAlign.Moved(rodMesh, turn, shift);
                var movedBody = MeshAlign.Moved(bodyMesh, turn, shift);
                var bodyMaterials = body.GetComponent<MeshRenderer>().sharedMaterials;
                // V2: the V5 cylinders keep the original's body, which has VEX's label on it (the red wrap with the
                // logo and the white band: VEX-CAD's and VEX's own STEP models are plain aluminium); only its rod
                // is left out, as VEX-CAD's rod moves on its own.
                if (!size.StartsWith("Legacy") && target != null && target.isReadable) {
                    var labelled = WithoutRod(target, movedRod, turn * way, turn * tipPoint + shift);
                    if (labelled != null) {
                        movedBody = labelled;
                        bodyMaterials = targetMaterials;
                    }
                }
                model = (movedBody, bodyMaterials,
                         movedRod,
                         // the legacy rods are chromed steel (VEX-CAD's "stainless" came in as gray plastic)
                         size.StartsWith("Legacy") ? new[] { body.GetComponent<MeshRenderer>().sharedMaterials[0] } : rodModel.GetComponent<MeshRenderer>().sharedMaterials,
                         turn * way, turn * tipPoint + shift);
                Models[size] = model;
            }

            axis = model.axis;
            GetComponent<MeshFilter>().sharedMesh = model.body;
            if (TryGetComponent(out MeshCollider collider)) collider.sharedMesh = model.body;
            GetComponent<MeshRenderer>().sharedMaterials = model.bodyMaterials;

            // A copy (duplicate) already has its rod.
            rod = transform.Find("Rod");
            if (rod == null) {
                rod = new GameObject("Rod") { layer = gameObject.layer }.transform;
                rod.SetParent(transform, false);
                rod.gameObject.AddComponent<MeshFilter>().sharedMesh = model.rod;
                rod.gameObject.AddComponent<MeshRenderer>().sharedMaterials = model.rodMaterials;
                rod.gameObject.AddComponent<MeshCollider>().sharedMesh = model.rod;
                rod.gameObject.AddComponent<SelectAs>().target = gameObject;

                var threadObject = new GameObject("Rod Thread") { layer = gameObject.layer, tag = "Screw" };
                threadObject.transform.SetParent(rod, false);
                threadObject.transform.localPosition = model.tip - axis * ThreadLength;
                threadObject.transform.localRotation = Quaternion.LookRotation(-axis, Vector3.Cross(axis, Vector3.right).sqrMagnitude > 0.01f ? Vector3.right : Vector3.up);
                threadObject.AddComponent<MeshFilter>().sharedMesh = RodThread.ThreadMesh(ThreadLength, 0.085f);
                threadMark = threadObject.AddComponent<MeshRenderer>();
                threadMark.sharedMaterial = Dynamics.Highlight;
                threadMark.enabled = false;
                var capsule = threadObject.AddComponent<CapsuleCollider>();
                capsule.direction = 2;
                capsule.radius = 0.1f;
                capsule.height = ThreadLength + 0.2f;
                capsule.center = new Vector3(0f, 0f, -ThreadLength / 2f);
                threadObject.AddComponent<SelectAs>().target = gameObject;
                thread = threadObject.AddComponent<RodThread>();
            }
            else {
                thread = rod.GetComponentInChildren<RodThread>(true);
                threadMark = thread != null ? thread.GetComponent<MeshRenderer>() : null;
                rod.GetComponent<SelectAs>().target = gameObject;
                if (thread != null) thread.GetComponent<SelectAs>().target = gameObject;
            }
            if (thread != null) thread.cylinder = gameObject;
            return true;
        }

        // V2: the original's cylinder (body and rod in one mesh) without its rod: the triangles lying wholly inside
        // the rod's round (its radius from the axis, between its ends) go. Null when nothing (or everything) would.
        private static Mesh WithoutRod(Mesh whole, Mesh rodMesh, Vector3 axis, Vector3 tip) {
            axis = axis.normalized;
            var rodVertices = rodMesh.vertices;
            float radius = 0f, low = float.MaxValue, high = float.MinValue;
            foreach (var v in rodVertices) {
                var t = Vector3.Dot(v - tip, axis);
                low = Mathf.Min(low, t);
                high = Mathf.Max(high, t);
                radius = Mathf.Max(radius, Vector3.ProjectOnPlane(v - tip, axis).magnitude);
            }
            const float Slack = 0.01f;
            var vertices = whole.vertices;
            var inRod = new bool[vertices.Length];
            for (var i = 0; i < vertices.Length; i++) {
                var d = vertices[i] - tip;
                var t = Vector3.Dot(d, axis);
                inRod[i] = t >= low - Slack && t <= high + Slack && Vector3.ProjectOnPlane(d, axis).magnitude <= radius + Slack;
            }
            var result = Object.Instantiate(whole);
            result.name = whole.name + " (body)";
            int kept = 0, dropped = 0;
            for (var s = 0; s < whole.subMeshCount; s++) {
                var triangles = whole.GetTriangles(s);
                var keep = new List<int>(triangles.Length);
                for (var i = 0; i + 2 < triangles.Length; i += 3) {
                    if (inRod[triangles[i]] && inRod[triangles[i + 1]] && inRod[triangles[i + 2]]) {
                        dropped++;
                        continue;
                    }
                    keep.Add(triangles[i]);
                    keep.Add(triangles[i + 1]);
                    keep.Add(triangles[i + 2]);
                    kept++;
                }
                result.SetTriangles(keep, s);
            }
            if (dropped == 0 || kept == 0) {
                Object.Destroy(result);
                return null;
            }
            result.RecalculateBounds();
            return result;
        }

        private void SetInstant(bool extended) => SetInstant(extended ? 1f : 0f);

        private void SetInstant(float fraction) {
            position = Mathf.Clamp01(fraction);
            Extended = position > 0.5f;
            if (rod != null) rod.localPosition = axis * (Stroke * position);
            Dynamics.SetToken(gameObject, "piston", position < 0.0005f ? null : position > 0.9995f ? "1"
                : position.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture));
        }

        // The thread is marked (like a hole) while the mouse is on the cylinder, so it can be found.
        private void Update() {
            if (threadMark == null) return;
            var show = false;
            var camera = Camera.main;
            if (camera != null && !gameObject.IsDeleted() && !MouseInput.overUI
                && Physics.Raycast(camera.ScreenPointToRay(MouseInput.Position), out var hit, Mathf.Infinity, LayerMask.GetMask("Default", "HoleCollisions")))
                show = hit.collider.transform.IsChildOf(transform);
            // While placing (a nut), too. V2: never in the simulation (it showed there when the mouse was on it).
            threadMark.enabled = (show || Placement.IsPlacing) && !Sandbox.Running;
        }

        // ------------------------------------------------------------------ moving

        /// <summary>Extends or retracts; what's fastened to the rod moves with it (one undo step).</summary>
        public void Set(bool extend) {
            var name = Size + " cylinder";
            if (extend == Extended && moving == null && (position < 0.0005f || position > 0.9995f)) {
                Interface.Interface.Say("The " + name + " is already " + (extend ? "extended" : "retracted"), false);
                return;
            }
            if (moving != null) StopCoroutine(moving);

            var riders = Riders(out var frame);
            if (frame) {
                Interface.Interface.Say("Can't " + (extend ? "extend" : "retract") + ": what's on the rod is also fastened to the frame the cylinder is on", false);
                Audio.Sounds.Play(Audio.Sounds.Error);
                return;
            }
            // What's on the rod is grounded: the rod stays and the cylinder (with what it's fastened to) moves the
            // other way, as when a cylinder is mounted backwards.
            var backwards = riders.Any(Linkage.IsGrounded);
            if (backwards) {
                var body = Dynamics.Fastened(new[] { gameObject }, new[] { thread.gameObject }, null, out _)
                           .Where(o => o != null && o.GetComponent<SavedObject>() != null).ToList();
                var grounded = body.FirstOrDefault(Linkage.IsGrounded);
                if (grounded != null) {
                    Interface.Interface.Say("Can't " + (extend ? "extend" : "retract") + ": what's on the rod and " + Interface.PartLabels.Short(grounded) + " on the cylinder's side are both grounded", false);
                    Audio.Sounds.Play(Audio.Sounds.Error);
                    return;
                }
                riders = body;
            }
            var before = riders.Select(r => (IElement)new ObjectElement(r)).ToList();
            before.Add(new CylinderElement(this, position));
            moving = StartCoroutine(Move(extend, riders, before, backwards));
            Audio.Sounds.PlayAt(Audio.Sounds.Air, transform.position, 0.7f);
            var parts = riders.Count(r => r.GetComponent<SavedObject>() != null);
            Interface.Interface.Say((extend ? "Extended" : "Retracted") + " the " + name + " " + Dynamics.Inches(Stroke)
                                    + (backwards ? " (its rod is held: the cylinder moved instead" + (parts > 1 ? ", with " + (parts - 1) + (parts == 2 ? " part" : " parts") : "") + ")"
                                       : parts > 0 ? " (" + parts + (parts == 1 ? " part" : " parts") + " moved with it)" : ""), true);
        }

        /// <summary>What's fastened to the rod's thread: parts it passes to a nut, and what's screwed to those.</summary>
        public List<GameObject> Riders(out bool frame) {
            frame = false;
            if (thread == null) return new List<GameObject>();
            var found = Dynamics.Fastened(new[] { thread.gameObject }, new[] { gameObject }, null, out frame, thread.gameObject);
            found.Remove(thread.gameObject);
            return found.Where(o => o != null && !o.transform.IsChildOf(transform)).ToList();
        }

        private IEnumerator Move(bool extend, List<GameObject> riders, List<IElement> before, bool backwards = false) {
            var start = position;
            var end = extend ? 1f : 0f;
            var time = 0f;
            var duration = Travel * Mathf.Abs(end - start);
            while (time < duration) {
                time += Time.deltaTime;
                // Air: quick off the mark, slowing into the end of the stroke.
                var k = 1f - Mathf.Pow(1f - Mathf.Clamp01(time / duration), 3f);
                Slide(Mathf.Lerp(start, end, k), riders, backwards);
                yield return null;
            }
            Slide(end, riders, backwards);
            Audio.Sounds.PlayAt(Audio.Sounds.Clack, transform.position, 0.35f, 0.8f);
            SetInstant(extend);
            StateSystem.AddElements(before);
            var after = riders.Select(r => (IElement)new ObjectElement(r)).ToList();
            after.Add(new CylinderElement(this, extend ? 1f : 0f));
            StateSystem.AddState(new State(after));
            moving = null;
        }

        // Backwards: the riders are the cylinder's own side (the cylinder too), moved back as the rod goes out, so the
        // rod stays where it is.
        private void Slide(float to, List<GameObject> riders, bool backwards = false) {
            var delta = transform.TransformVector(axis * (Stroke * (to - position)));
            position = to;
            rod.localPosition = axis * (Stroke * position);
            foreach (var rider in riders)
                if (rider != null) rider.transform.position += backwards ? -delta : delta;
        }

        // Undo/redo: the rod goes back out or in with the parts (they ease back over 0.25 s).
        private class CylinderElement : IElement {
            private readonly PneumaticCylinder cylinder;
            private readonly float extended;

            public CylinderElement(PneumaticCylinder cylinder, float extended) {
                this.cylinder = cylinder;
                this.extended = extended;
            }

            public void Load() {
                if (cylinder == null) return;
                if (cylinder.moving != null) cylinder.StopCoroutine(cylinder.moving);
                cylinder.moving = null;
                cylinder.StartCoroutine(cylinder.Ease(extended));
            }
        }

        private IEnumerator Ease(float extended) {
            var start = position;
            var end = extended;
            for (var time = 0f; time < 0.25f; time += Time.deltaTime) {
                position = Mathf.Lerp(start, end, time / 0.25f);
                rod.localPosition = axis * (Stroke * position);
                yield return null;
            }
            SetInstant(extended);
        }
    }
}
