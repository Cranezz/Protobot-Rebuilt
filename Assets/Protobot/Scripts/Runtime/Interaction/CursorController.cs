using System;
using Protobot.Core;
using Protobot.Menus;
using UnityEngine;

namespace Protobot.Interaction
{
    /// <summary>
    /// In play the mouse is locked to the centre of the screen and drawn as a reticle: a dot normally,
    /// a pointing hand over things to click, an open hand over parts (closed while holding one). A label
    /// next to it names what you're aiming at. When a menu opens the normal mouse pointer comes back.
    /// </summary>
    public class CursorController : MonoBehaviour
    {
        [Serializable]
        public struct CursorStyle
        {
            public CursorKind kind;
            public Sprite sprite;
            public Vector2 hotspot;
        }

        [SerializeField] Camera viewCamera;
        [SerializeField] Hud hud;
        [SerializeField] CursorStyle[] styles;

        [Tooltip("How far away (m) the player can reach things. Further away they still show their name.")]
        [SerializeField] float reach = 2.5f;
        [SerializeField] LayerMask raycastMask = ~(1 << 2);  // everything except Ignore Raycast (the player)

        HoverTarget current;
        bool menuMode;

        public HoverTarget Current => current;
        public bool CurrentInReach { get; private set; }
        public RaycastHit CurrentHit { get; private set; }
        public Camera ViewCamera => viewCamera;
        public float Reach => reach;

        /// <summary>Set by <see cref="PlayerHands"/> while it carries something, so the cursor shows a closed hand.</summary>
        public HoverTarget Holding { get; set; }

        void OnEnable()
        {
            menuMode = !MenuState.AnyOpen;  // forces the first Update to apply the right mode
        }

        void OnDisable()
        {
            SetHover(null);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void Update()
        {
            if (MenuState.AnyOpen)
            {
                EnterMenuMode();
                return;
            }
            EnterPlayMode();

            if (Holding != null)
            {
                SetHover(null);
                ShowKind(CursorKind.Grabbing);
                hud.ShowTooltip(Holding.Title, "Release to drop  ·  Scroll to move closer/further", true);
                return;
            }

            HoverTarget target = null;
            var ray = viewCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            if (Physics.Raycast(ray, out var hit, 30f, raycastMask, QueryTriggerInteraction.Collide))
            {
                target = hit.collider.GetComponentInParent<HoverTarget>();
                CurrentHit = hit;
            }

            SetHover(target);
            CurrentInReach = target != null && hit.distance <= reach;
            ShowKind(CurrentInReach ? target.Cursor : CursorKind.Default);

            if (target == null)
                hud.HideTooltip();
            else
                hud.ShowTooltip(target.Title, CurrentInReach ? target.Hint : "Move closer to use", CurrentInReach);
        }

        void EnterMenuMode()
        {
            if (menuMode)
                return;
            menuMode = true;
            SetHover(null);
            hud.SetReticleVisible(false);
            hud.HideTooltip();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            var arrow = StyleFor(CursorKind.Default);
            Cursor.SetCursor(arrow.sprite != null ? arrow.sprite.texture : null, arrow.hotspot, CursorMode.Auto);
        }

        void EnterPlayMode()
        {
            if (!menuMode)
            {
                // The editor (or alt-tab) can release the lock; take it back whenever we're playing.
                if (Cursor.lockState != CursorLockMode.Locked && Application.isFocused)
                    Cursor.lockState = CursorLockMode.Locked;
                return;
            }
            menuMode = false;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            hud.SetReticleVisible(true);
        }

        void SetHover(HoverTarget target)
        {
            if (target == current)
                return;
            if (current != null)
                current.SetHighlighted(false);
            current = target;
            if (current != null)
                current.SetHighlighted(true);
            if (current == null)
                CurrentInReach = false;
        }

        void ShowKind(CursorKind kind)
        {
            var style = StyleFor(kind);
            hud.SetReticle(kind == CursorKind.Default ? null : style.sprite, kind != CursorKind.Default);
        }

        CursorStyle StyleFor(CursorKind kind)
        {
            foreach (var style in styles)
                if (style.kind == kind)
                    return style;
            return default;
        }
    }
}
