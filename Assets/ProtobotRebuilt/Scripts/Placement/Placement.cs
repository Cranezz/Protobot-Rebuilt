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
            ThreadPlaceDisplacement.AddTo(this, placePositions);  // V2: a cylinder held by its thread
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
                    if (rotateRing != null)
                        rotateRing.SetPositionAndRotation(ringPosition, ringRotation);
                }
                else {
                    // V2: T: back to the turn placing began with.
                    if (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.tKey.wasPressedThisFrame
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

                            currentDisplacement = newDisplacement;
                            newDisplacement.Displace(gameObject);
                            break;
                        }
                    }
                }
            }

            meshRenderer.enabled = placing;
        }

        private Mesh displayMesh;  // V2

        public void StartPlacing(PlacementData newPlacementData) {
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

        private void Place() {
            bool isPartPlacement = currentPlacementData.GetType() == typeof(PartPlacementData);
            bool isGameObjectPlacement = currentPlacementData.GetType() == typeof(GameObjectPlacementData);

            var displaceRot = currentDisplacement.rotation.Orientation;
            var displacePos = currentDisplacement.translation.Position;
            
            if (isPartPlacement) {
                var generator = ((PartPlacementData)currentPlacementData).partGenerator;
                GameObject placedObj = generator.Generate(displacePos, displaceRot);

                // Call OutputPartsList to update the weight display after placing a part
                PartListOutput partListOutput = FindObjectOfType<PartListOutput>();
                if (partListOutput != null) {
                    partListOutput.CalculatePartsList(); // Recalculate and notify weight update
                }

                ObjectElement prevElement = new ObjectElement(placedObj);
                prevElement.existing = false;
                StateSystem.AddElement(prevElement);

                ObjectElement objElement = new ObjectElement(placedObj);
                StateSystem.AddState(objElement);
            }
            else if (isGameObjectPlacement) {
                movementManager.DisplaceTo(currentDisplacement, 0);
                StopPlacing();
            }

            rotating = false;
            OnStopRotate?.Invoke();
        }
        public void InitRotation() {
            initMouseVector = MouseVector;
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
            float camDot = Vector3.Dot(refCamera.transform.forward, initRotVector); //finds the dot product between the camera direction vs. the look direction of this rotate ring
            Quaternion newRot = Quaternion.AngleAxis(angle, -initRotVector * Mathf.Sign(camDot)) * initRot;

            Rotation rot = new Rotation(newRot);
            currentDisplacement = new Displacement(currentDisplacement.translation.Position, newRot);
            rot.Rotate(gameObject);
        }
    }
}