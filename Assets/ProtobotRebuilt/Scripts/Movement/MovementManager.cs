using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Protobot.Transformations;
using DG.Tweening;
using System;
using Protobot.StateSystems;

namespace Protobot {
    public class MovementManager : MonoBehaviour {
        [SerializeField] private ObjectLink objectLink;
        public GameObject MovingObj => objectLink.obj;

        private Pivot movementPivot;

        private Tween movementTween;

        private bool displaceContinous = false;

        public Action OnStartMoving;
        public Action OnFinishMoving;

        List<GameObject> movingObjs;

        // V2: joints and grounded parts (Motion.Linkage): what the pivot carries is the moved body only, the bodies on
        // joints to it follow, and a grounded part refuses to move (a part on a joint to one only turns about it).
        private Motion.Linkage.Plan plan;
        private bool refused;

        /// <summary>V2: how the next displacement was asked for (the Transform tool's arrows or rings).</summary>
        public Motion.Linkage.MoveKind NextKind { get; set; } = Motion.Linkage.MoveKind.Any;

        private bool StartPlan() {
            plan = Motion.Linkage.MakePlan(MovingObj, movingObjs);
            if (plan != null && plan.refusal != null) {
                Interface.Interface.Say(plan.refusal, false);
                Audio.Sounds.Play(Audio.Sounds.Error);
                plan = null;
                return false;
            }
            return true;
        }

        private List<GameObject> PivotObjs() => plan != null ? plan.PivotObjects(movingObjs) : movingObjs;

        private void LateUpdate() {
            if (plan != null && !movementPivot.IsEmpty)
                plan.Follow();
        }


        private void Start() { 
            OnStartMoving += () => {
                ObjectElement.AddObjectElements(GetMovingConnectedObjs());
            };

            OnFinishMoving += () => {
                StateSystem.AddEmptyState();
                ObjectElement.AddObjectElements(GetMovingConnectedObjs());
            };

            movementPivot = Pivot.Create("Movement Pivot");
        }

        private void Update() {
            if (!movementPivot.IsEmpty && movementTween != null && !movementTween.active && !displaceContinous) {
                // V2: empty the pivot first, so a part removed meanwhile (the saw swaps the cut part) can't make this
                // throw every frame
                if (plan != null) plan.Follow();  // V2
                movementPivot.SetEmpty();
                if (MovingObj != null)
                    OnFinishMoving?.Invoke();
                plan = null;  // V2
            }
        }

        public List<GameObject> GetMovingConnectedObjs() => MovingObj.GetConnectedObjects(true) ?? new List<GameObject>(); // V2: none once the part is gone

        public void FormPivot(List<GameObject> objs) {
            movementPivot.SetEmpty();
            movementPivot.transform.position = MovingObj.transform.position;
            movementPivot.transform.rotation = MovingObj.transform.rotation;
            
            movementPivot.AddObjects(objs);
        }

        public void StopContinuous() {
            displaceContinous = false;
            refused = false;  // V2
        }

        /// <summary>
        /// Prepares movementmanager by setting up all pivots and actions for the displacements to be running every frame
        /// </summary>
        /// <param name="value"></param>
        public void StartContinuous() {
            displaceContinous = true;

            if (displaceContinous) {
                movingObjs = GetMovingConnectedObjs();
                // V2: a grounded part doesn't move at all.
                refused = !StartPlan();
                if (refused) return;

                FormPivot(PivotObjs());
                OnStartMoving?.Invoke();
            }
        }

        public void DisplaceTo(Displacement displacement, float duration = 0.25f) {
            var kind = NextKind;  // V2
            NextKind = Motion.Linkage.MoveKind.Any;
            if (displaceContinous && refused) return;  // V2
            if (!displaceContinous) {
                movingObjs = GetMovingConnectedObjs();
                if (!StartPlan()) return;  // V2
                OnStartMoving?.Invoke();
            }

            // V2: on a joint to a grounded part, the move becomes the turn about the joint that gets closest (at once,
            // so the chain stays together).
            if (plan != null && plan.constrained) {
                displacement = plan.Constrain(displacement, kind);
                duration = 0f;
            }

            if (!displaceContinous) {
                FormPivot(PivotObjs());
            }
            movementTween = displacement.Displace(movementPivot.gameObject, duration);
        }

        public void MoveTo(Vector3 position) {
            NextKind = Motion.Linkage.MoveKind.Translate;  // V2
            var displacement = new Displacement(position, MovingObj.transform.rotation);
            DisplaceTo(displacement);
        }

        public void RotateTo(Quaternion rotation) {
            NextKind = Motion.Linkage.MoveKind.Rotate;  // V2
            var displacement = new Displacement(MovingObj.transform.position, rotation);
            DisplaceTo(displacement);
        }
    }
}