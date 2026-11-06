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
            Stroke = float.TryParse(Size.Replace("mm", ""), out var mm) ? mm / 25.4f : 1f;
            if (!Build(Size)) {
                enabled = false;
                return;
            }
            var startExtended = ids.Length > 2 && ids[2] == "Extended";
            SetInstant(startExtended);
        }

        private void OnDestroy() => All.Remove(this);

        private void Start() {
            // A loaded build's saved state comes after the part is made.
            var token = Dynamics.Token(gameObject, "piston");
            if (token != null) SetInstant(token == "1");
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
                foreach (var generator in Resources.LoadAll<PartGenerator>("Part Prefabs"))
                    if (generator.GetComponent<PartType>()?.id == "PNMT")
                        foreach (var data in generator.GetComponentsInChildren<PartData>(true))
                            if (data.param1Value == size && data.param2Value == "Normal")
                                target = data.GetComponent<MeshFilter>().sharedMesh;
                var points = bodyMesh.vertices.Concat(rodMesh.vertices).ToList();
                var (turn, shift, _) = MeshAlign.Find(points, target.vertices);
                // The rod runs along the models' +x; its tip is its far end.
                model = (MeshAlign.Moved(bodyMesh, turn, shift), body.GetComponent<MeshRenderer>().sharedMaterials,
                         MeshAlign.Moved(rodMesh, turn, shift), rodModel.GetComponent<MeshRenderer>().sharedMaterials,
                         turn * Vector3.right, turn * new Vector3(rodMesh.bounds.max.x, rodMesh.bounds.center.y, rodMesh.bounds.center.z) + shift);
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

        private void SetInstant(bool extended) {
            Extended = extended;
            position = extended ? 1f : 0f;
            if (rod != null) rod.localPosition = axis * (Stroke * position);
            Dynamics.SetToken(gameObject, "piston", extended ? "1" : null);
        }

        // The thread is marked (like a hole) while the mouse is on the cylinder, so it can be found.
        private void Update() {
            if (threadMark == null) return;
            var show = false;
            var camera = Camera.main;
            if (camera != null && !gameObject.IsDeleted() && !MouseInput.overUI
                && Physics.Raycast(camera.ScreenPointToRay(MouseInput.Position), out var hit, Mathf.Infinity, LayerMask.GetMask("Default", "HoleCollisions")))
                show = hit.collider.transform.IsChildOf(transform);
            // While placing (a nut), too.
            threadMark.enabled = show || Placement.IsPlacing;
        }

        // ------------------------------------------------------------------ moving

        /// <summary>Extends or retracts; what's fastened to the rod moves with it (one undo step).</summary>
        public void Set(bool extend) {
            var name = Size + " cylinder";
            if (extend == Extended && moving == null) {
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
            before.Add(new CylinderElement(this, Extended));
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
            after.Add(new CylinderElement(this, extend));
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
            private readonly bool extended;

            public CylinderElement(PneumaticCylinder cylinder, bool extended) {
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

        private IEnumerator Ease(bool extended) {
            var start = position;
            var end = extended ? 1f : 0f;
            for (var time = 0f; time < 0.25f; time += Time.deltaTime) {
                position = Mathf.Lerp(start, end, time / 0.25f);
                rod.localPosition = axis * (Stroke * position);
                yield return null;
            }
            SetInstant(extended);
        }
    }
}
