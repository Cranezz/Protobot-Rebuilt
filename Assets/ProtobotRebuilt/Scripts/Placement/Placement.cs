using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Events;

using Protobot.StateSystems;
using Protobot.InputEvents;
using Protobot.Transformations;
using UnityEngine.InputSystem;

namespace Protobot {
    public class Placement : MonoBehaviour { 
        public static int PLACEMENT_LAYER => 14;

        [SerializeField] private MovementManager movementManager;

        public bool placing;
        public static Placement Instance { get; private set; }  // V2
        public static bool IsPlacing => Instance != null && Instance.placing;  // V2
        public string PlacingId => currentPlacementData?.objectId;  // V2: the action log says what's being placed
        /// <summary>V2: a part was just placed (paste copies its look and edits onto it).</summary>
        public static event Action<GameObject> Placed;
        /// <summary>V2: parts a Placed handler made with the placed one (the rest of a pasted group): they're in the same
        /// undo step.</summary>
        public static readonly List<GameObject> PlacedWith = new();
        public bool rotating;

        private PlacementData currentPlacementData;

        [CacheComponent] private MeshRenderer meshRenderer;

        [CacheComponent] private MeshFilter meshFilter;

        [SerializeField] private List<PlaceDisplacement> placePositions;

        [Space(5)]

        [SerializeField] private InputEvent placeInput;
        [SerializeField] private InputEvent toggleRotateInput;
        [SerializeField] private InputEvent stopPlacingInput;
        [SerializeField] private InputEvent dupInput;

        [SerializeField] private UnityEvent OnStartPlacing;
        [SerializeField] private UnityEvent OnStopPlacing;
        [SerializeField] private UnityEvent OnStartRotate;
        [SerializeField] private UnityEvent OnStopRotate;

        private Displacement currentDisplacement;

        [SerializeField] private Camera refCamera = null; //used for dot product angle comparison

        private Vector3 rotVector => transform.forward;

        private Vector3 initRotVector; //the initial Vector to rotate about

        private Vector3 initMouseVector; //the initial Vector between the mouse position and the selection position
        private Vector3 MouseVector => (MouseInput.Position - refCamera.WorldToScreenPoint(transform.position)).normalized;
        private Quaternion initRot; //the intiial rotation of the selection
        
        // V2: the rotate ring (a child of the placement ghost) used to spin with the part; it stays put now.
        private Transform rotateRing;
        private Quaternion ringRotation;
        private Vector3 ringPosition;

        // V2: the part's own turn (where it started, plus the user's turns away from holes): a hole turns the part
        // to fit while it's over it, and moving off the hole gives the part its own turn back. T sets it back to
        // where placing began.
        private Quaternion freeRotation, startRotation;
        private bool snappedTurn;

        private void Awake() {
            Instance = this;  // V2
            rotateRing = transform.Find("Rotate Ring Canvas");
            AxlePlaceDisplacement.AddTo(this, placePositions);    // V2: round parts onto screws, shafts and holes
            ThreadPlaceDisplacement.AddTo(this, placePositions);  // V2: a cylinder held by its thread
            CappedShaftPlaceDisplacement.AddTo(this, placePositions);  // V2: the capped shaft into holes
            MountPlaceDisplacement.AddTo(this, placePositions);  // V2: pneumatic mounts onto cylinders
            FollowerPlaceDisplacement.AddTo(this, placePositions);  // V2: a cam follower through its mounting bracket
            Motion.SlidePlaceDisplacement.AddTo(this, placePositions);  // V2: slide trucks into slide members
            placing = false;
            rotating = false;
            
            placeInput.performed += () => {
                if (placing && !MouseInput.overUI)
                    Place();
            };

            toggleRotateInput.performed += () => {
                InitRotation();
                if (placing && !MouseInput.overUI) {
                    rotating = !rotating;

                    if (rotating) OnStartRotate?.Invoke();
                    else OnStopRotate?.Invoke();
                }
            };

            /*dupInput.performed += () => {
                if (currentPlacementData != null && placing) {
                    /*if (currentPlacementData.TryParse(out GameObjectPlacementData objPlaceData)) {
                        SetPlacementLayer(objPlaceData.GetGameObject(), false);

                    }#1#
                }
            };

            dupInput.canceled += () => {
                if (currentPlacementData != null && placing) {
                    if (currentPlacementData.TryParse(out GameObjectPlacementData objPlaceData)) {
                        objPlaceData.GetGameObject().GetConnectedObjects(true).ForEach(x => x.SetActive(false));
                    }
                }
            };*/

            stopPlacingInput.performed += () => {
                if (placing)
                    StopPlacing();
            };

            currentDisplacement = new Displacement(transform);
        }

        void Update() {
            ThreadPlaceDisplacement.WatchClicks(this);  // V2
            if (!(placing && rotating)) Interface.RingDial.Clear();  // V2
            if (placing) {
                if (rotating) {
                    /*
                    if (Keyboard.current.xKey.wasPressedThisFrame) {
                        initRotVector = transform.right;
                        initRot = transform.rotation;
                    }
                    */

                    /*
                    if (Keyboard.current.zKey.wasPressedThisFrame) {
                        initRotVector = transform.forward;
                        initRot = transform.rotation;
                    }
                    */

                    /*
                    if (Keyboard.current.yKey.wasPressedThisFrame) {
                        initRotVector = transform.up;
                        initRot = transform.rotation;
                    }*/
                    
                    Rotate();
                    if (!snappedTurn) freeRotation = currentDisplacement.rotation.Orientation;  // V2
                    // V2: the dial (tick marks and the angle turned so far) round the turning point.
                    Interface.RingDial.Draw(ringPosition, -initRotVector * Mathf.Sign(Vector3.Dot(refCamera.transform.forward, initRotVector)), initMouseScreen, MouseInput.Position, lastAngle, 22.5f);
                    if (rotateRing != null)
                        rotateRing.SetPositionAndRotation(ringPosition, ringRotation);
                }
                else {
                    // V2: T: back to the turn placing began with.
                    if (Tools.Keybinds.Pressed("ResetTurn")
                        && !InputEvents.TextInput.IsTyping && !InputEvents.TextInput.ShortcutsPaused) {
                        freeRotation = startRotation;
                        currentDisplacement = new Displacement(currentDisplacement.translation.Position, freeRotation);
                        Interface.Interface.Say("Turn reset");
                    }
                    foreach (PlaceDisplacement placePosition in placePositions) {
                        if (placePosition.TryGetDisplacement(currentPlacementData, out Displacement displacement)) {
                            var newDisplacement = displacement;
                            snappedTurn = placePosition.ModifyRotation;  // V2
                            if (!placePosition.ModifyRotation) {
                                // V2: the part's own turn, not the one the last hole gave it.
                                newDisplacement = new Displacement(displacement.translation.Position, freeRotation);
                            }

                            // V2: a new part on the grid has its model's middle under the cursor (some VEX parts'
                            // models sit away from their origin: chassis rails spawned off to the side).
                            if (placePosition is PlanePlaceDisplacement && currentPlacementData is PartPlacementData && displayMesh != null) {
                                var middle = newDisplacement.rotation.Orientation * displayMesh.bounds.center;
                                newDisplacement = new Displacement(newDisplacement.translation.Position - new Vector3(middle.x, 0f, middle.z), newDisplacement.rotation.Orientation);
                            }

                            currentDisplacement = newDisplacement;
                            newDisplacement.Displace(gameObject, 0.05f);   // V2: was 0.25 s: the part trailed the mouse
                            break;
                        }
                    }
                }
            }

            meshRenderer.enabled = placing;
        }

        private Mesh displayMesh;  // V2

        /// <summary>V2: placing that starts with the part turned this way (paste: the copied part's turn).</summary>
        public void StartPlacing(PlacementData newPlacementData, Quaternion turn) {
            StartPlacing(newPlacementData);
            freeRotation = startRotation = turn;
            transform.rotation = turn;
            currentDisplacement = new Displacement(currentDisplacement.translation.Position, turn);
        }

        public void StartPlacing(PlacementData newPlacementData) {
            // V2: a part from the parts list is placed with the tools down: the drill or saw would act on the click
            // that places it (a screw placed with the drill drilled the hole).
            if (newPlacementData is PartPlacementData && Interface.ToolRail.Current != "Transform" && Interface.ToolRail.Current != "Move")
                Interface.ToolRail.Select("Transform");
            currentPlacementData = newPlacementData;
            placing = true;
            rotating = false;
            // V2: the preview's mesh is made for it each time: free the last one (they piled up, never freed).
            if (displayMesh != null) Destroy(displayMesh);
            displayMesh = newPlacementData.GetDisplayMesh();
            meshFilter.mesh = displayMesh;
            freeRotation = startRotation = Quaternion.identity;  // V2
            snappedTurn = false;

            if (currentPlacementData.TryParse(out GameObjectPlacementData objPlaceData)) {
                var objTransform = objPlaceData.GetGameObject().transform;
                transform.rotation = objTransform.rotation;
                transform.position = objTransform.position;
                currentDisplacement = new Displacement(transform);
                freeRotation = startRotation = objTransform.rotation;  // V2
                
                SetPlacementLayer(objPlaceData.GetGameObject(), true);
            }

            OnStartPlacing?.Invoke();
        }

        public void SetPlacementLayer(GameObject obj, bool value) {
            obj.GetConnectedObjects(true).ForEach(x => {
                if (value && x.layer == 0)
                    x.layer = Placement.PLACEMENT_LAYER;
                
                if (!value && x.layer == PLACEMENT_LAYER)
                    x.layer = 0;
            });
        }

        IEnumerator WaitStopPlacing() {
            yield return new WaitForEndOfFrame();
            OnStopPlacing?.Invoke();
            placing = false;
        }

        public void StopPlacing() {
            ThreadPlaceDisplacement.Grabbed = null;  // V2
            if (currentPlacementData.TryParse(out GameObjectPlacementData objPlaceData))
                SetPlacementLayer(objPlaceData.GetGameObject(), false);
            
            transform.rotation = Quaternion.identity;
            OnStopRotate?.Invoke();

            StartCoroutine(WaitStopPlacing());
        }

        /// <summary>V2: places what's being placed where it is now (quick placing lets go of a key to place).</summary>
        public void PlaceNow() {
            if (placing && currentPlacementData != null) Place();
        }

        private void Place() {
            bool isPartPlacement = currentPlacementData.GetType() == typeof(PartPlacementData);
            bool isGameObjectPlacement = currentPlacementData.GetType() == typeof(GameObjectPlacementData);

            var displaceRot = currentDisplacement.rotation.Orientation;
            var displacePos = currentDisplacement.translation.Position;
            
            if (isPartPlacement) {
                var generator = ((PartPlacementData)currentPlacementData).partGenerator;
                GameObject placedObj = generator.Generate(displacePos, displaceRot);
                Interface.PartsRecent.Use(generator.GetId());   // V2: Recent lists what was placed, not only picked

                // Call OutputPartsList to update the weight display after placing a part
                PartListOutput partListOutput = FindObjectOfType<PartListOutput>();
                if (partListOutput != null) {
                    partListOutput.CalculatePartsList(); // Recalculate and notify weight update
                }

                ObjectElement prevElement = new ObjectElement(placedObj);
                prevElement.existing = false;
                StateSystem.AddElement(prevElement);

                PlacedWith.Clear();
                Placed?.Invoke(placedObj);   // V2 (before the undo step records it: a paste's edits are part of it)

                ObjectElement objElement = new ObjectElement(placedObj);
                if (PlacedWith.Count == 0) StateSystem.AddState(objElement);
                else {
                    // V2: the rest of a pasted group comes and goes with it
                    var elements = new List<IElement> { objElement };
                    foreach (var other in PlacedWith) {
                        if (other == null) continue;
                        StateSystem.AddElement(new ObjectElement(other) { existing = false });
                        elements.Add(new ObjectElement(other));
                    }
                    StateSystem.AddState(new State(elements));
                    PlacedWith.Clear();
                }
            }
            else if (isGameObjectPlacement) {
                movementManager.DisplaceTo(currentDisplacement, 0);
                StopPlacing();
            }

            rotating = false;
            OnStopRotate?.Invoke();
        }
        private Vector2 initMouseScreen;   // V2: the rotate dial
        private float lastAngle;

        public void InitRotation() {
            initMouseVector = MouseVector;
            initMouseScreen = MouseInput.Position;
            lastAngle = 0f;
            ringPosition = transform.position;
            if (rotateRing != null) {
                ringRotation = rotateRing.rotation;
                // V2: on the point the part turns about (the hole it's held by), so the ring stays on the part
                // while it turns instead of being left where the part's middle was.
                ringPosition = transform.position;
            }

            initRot = transform.rotation;
            initRotVector = transform.forward;
        }

        public void Rotate() {
            float angle = Vector2.SignedAngle(MouseVector, initMouseVector);
            angle = Mathf.Round(angle * 1 / 22.5f) * 22.5f; //force values to round at 22.5 degree increments
            lastAngle = angle;  // V2
            float camDot = Vector3.Dot(refCamera.transform.forward, initRotVector); //finds the dot product between the camera direction vs. the look direction of this rotate ring
            Quaternion newRot = Quaternion.AngleAxis(angle, -initRotVector * Mathf.Sign(camDot)) * initRot;

            Rotation rot = new Rotation(newRot);
            currentDisplacement = new Displacement(currentDisplacement.translation.Position, newRot);
            rot.Rotate(gameObject);
        }
    }
}