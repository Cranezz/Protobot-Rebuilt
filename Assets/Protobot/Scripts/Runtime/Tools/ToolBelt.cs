using System;
using System.Collections.Generic;
using Protobot.Core;
using Protobot.Interaction;
using Protobot.Menus;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Protobot.Tools
{
    /// <summary>
    /// The tools the player carries and which one is in their hand. Number keys pick a tool directly;
    /// holding Tab opens a tool wheel around the reticle (move the mouse towards a tool, let go of Tab
    /// to pick it; the centre is empty hands). Routes the mouse buttons and wheel to the equipped tool.
    /// Clicking something clickable (a bin) works with any tool in hand.
    /// </summary>
    public class ToolBelt : MonoBehaviour, ICursorOverride
    {
        const float HoldTime = 0.45f;

        [SerializeField] CursorController cursor;
        [SerializeField] Hud hud;
        [SerializeField] Camera viewCamera;
        [SerializeField] Transform viewModelAnchor;
        [SerializeField] PlayerTool[] tools;

        PlayerTool current;
        readonly Dictionary<PlayerTool, GameObject> viewModels = new();
        bool suspended;
        float rightDownTime = -1f;
        bool rightHoldFired;
        Vector2 wheelAim;
        Vector3 anchorRest;
        Vector3 swayVelocity;

        public CursorController Cursor => cursor;
        public Hud Hud => hud;
        public Camera ViewCamera => viewCamera;
        public IReadOnlyList<PlayerTool> Tools => tools;
        public PlayerTool Current => current;

        /// <summary>True while Tab is held and the wheel is showing.</summary>
        public bool WheelOpen { get; private set; }

        /// <summary>Tool the wheel points at (-1 = centre / empty hands).</summary>
        public int WheelHover { get; private set; } = -1;

        public event Action<PlayerTool> Equipped;

        void Awake()
        {
            anchorRest = viewModelAnchor.localPosition;
            foreach (var tool in tools)
            {
                GameObject model = null;
                if (tool.ViewModelPrefab != null)
                {
                    model = Instantiate(tool.ViewModelPrefab, viewModelAnchor, false);
                    foreach (var t in model.GetComponentsInChildren<Transform>(true))
                        t.gameObject.layer = viewModelAnchor.gameObject.layer;
                    foreach (var c in model.GetComponentsInChildren<Collider>(true))
                        Destroy(c);
                    model.SetActive(false);
                }
                viewModels[tool] = model;
                tool.Bind(this, model);
            }
            cursor.Override = this;
        }

        void Start() => Equip(tools[0]);

        AudioSource audioSource;

        /// <summary>Sounds from the tool in hand (not positioned: it's right next to the player's ears).</summary>
        public void PlaySound(AudioClip clip, float volume)
        {
            if (clip == null)
                return;
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.spatialBlend = 0f;
            }
            audioSource.pitch = UnityEngine.Random.Range(0.95f, 1.05f);  // so repeats don't sound identical
            audioSource.PlayOneShot(clip, volume);
        }

        public void Equip(PlayerTool tool)
        {
            if (tool == current || tool == null)
                return;
            if (current != null)
            {
                current.OnUnequip();
                if (viewModels[current] != null)
                    viewModels[current].SetActive(false);
            }
            current = tool;
            if (viewModels[current] != null)
            {
                viewModels[current].SetActive(true);
                // Start below the view; the sway smoothing brings it up into the hand.
                viewModelAnchor.localPosition = anchorRest + Vector3.down * 0.12f;
            }
            current.OnEquip();
            Equipped?.Invoke(current);
        }

        void Update()
        {
            if (MenuState.AnyOpen)
            {
                if (!suspended)
                {
                    suspended = true;
                    CloseWheel(false);
                    current.OnSuspend();
                }
                return;
            }
            suspended = false;

            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard == null || mouse == null)
                return;

            UpdateWheel(keyboard, mouse);
            if (WheelOpen)
                return;

            for (var i = 0; i < tools.Length && i < 9; i++)
                if (keyboard[Key.Digit1 + i].wasPressedThisFrame)
                    Equip(tools[i]);

            if (mouse.leftButton.wasPressedThisFrame)
            {
                if (cursor.CurrentInReach && cursor.Current != null && cursor.Current.GetComponentInParent<IInteractable>() is { } interactable)
                    interactable.Interact();
                else
                    current.PrimaryDown();
            }
            else if (mouse.leftButton.wasReleasedThisFrame)
            {
                current.PrimaryUp();
            }

            if (mouse.rightButton.wasPressedThisFrame)
            {
                rightDownTime = Time.unscaledTime;
                rightHoldFired = false;
            }
            if (mouse.rightButton.isPressed && rightDownTime >= 0f && !rightHoldFired && Time.unscaledTime - rightDownTime >= HoldTime)
            {
                rightHoldFired = true;
                current.SecondaryHold();
            }
            if (mouse.rightButton.wasReleasedThisFrame)
            {
                if (!rightHoldFired && rightDownTime >= 0f)
                    current.SecondaryTap();
                rightDownTime = -1f;
            }

            var scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
                current.Scroll(scroll > 0f ? 1 : -1);

            current.Tick();
            Sway(mouse);
        }

        void UpdateWheel(Keyboard keyboard, Mouse mouse)
        {
            if (keyboard.tabKey.wasPressedThisFrame)
            {
                WheelOpen = true;
                wheelAim = Vector2.zero;
                InputLocks.LookSuppressed = true;
            }
            if (!WheelOpen)
                return;

            wheelAim = Vector2.ClampMagnitude(wheelAim + mouse.delta.ReadValue() * 0.01f, 1f);
            WheelHover = wheelAim.magnitude < 0.35f ? -1 : WheelSlot(wheelAim);

            if (!keyboard.tabKey.isPressed)
                CloseWheel(true);
        }

        /// <summary>Which of the non-hands tools (index 1..n) a wheel direction points at.</summary>
        int WheelSlot(Vector2 direction)
        {
            var count = tools.Length - 1;
            var angle = Mathf.Atan2(direction.x, direction.y) * Mathf.Rad2Deg;  // 0 = up, clockwise
            if (angle < 0f)
                angle += 360f;
            var slice = 360f / count;
            return 1 + Mathf.FloorToInt((angle + slice / 2f) % 360f / slice);
        }

        /// <summary>Angle (degrees, 0 = up, clockwise) of a tool's slot on the wheel.</summary>
        public float WheelAngle(int toolIndex) => (toolIndex - 1) * 360f / (tools.Length - 1);

        void CloseWheel(bool select)
        {
            if (!WheelOpen)
                return;
            WheelOpen = false;
            InputLocks.LookSuppressed = false;
            if (select)
                Equip(WheelHover < 0 ? tools[0] : tools[WheelHover]);
            WheelHover = -1;
        }

        /// <summary>Lets the held tool lag a little behind the view, so it feels like it's in a hand.</summary>
        void Sway(Mouse mouse)
        {
            var delta = InputLocks.LookSuppressed ? Vector2.zero : mouse.delta.ReadValue();
            var target = anchorRest + new Vector3(-delta.x, -delta.y, 0f) * 0.00035f;
            viewModelAnchor.localPosition = Vector3.SmoothDamp(viewModelAnchor.localPosition, target, ref swayVelocity, 0.08f);
        }

        public bool Describe(HoverTarget target, bool inReach, ref CursorView view)
        {
            if (WheelOpen)
            {
                // The wheel takes the middle of the screen: no reticle or tooltip under it.
                view.title = null;
                view.reticle = null;
                view.reticleColor = Color.clear;
                return true;
            }
            // Things to click (bins) work the same whatever is in hand, so they keep the pointing hand.
            if (target != null && inReach && target.GetComponentInParent<IInteractable>() != null)
                return false;
            return current != null && current.DescribeTarget(target, inReach, ref view);
        }

        void OnDisable() => InputLocks.LookSuppressed = false;
    }
}
