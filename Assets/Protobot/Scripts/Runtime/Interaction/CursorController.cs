using System;
using Protobot.Core;
using Protobot.Menus;
using UnityEngine;

namespace Protobot.Interaction
{
    /// <summary>What the reticle and tooltip show for the current target.</summary>
    public struct CursorView
    {
        public Sprite reticle;
        public Color reticleColor;
        public bool large;
        public string title;
        public string hint;
        public bool available;
    }

    /// <summary>Lets the equipped tool restyle the reticle/tooltip (see Tools.ToolBelt).</summary>
    public interface ICursorOverride
    {
        bool Describe(HoverTarget target, bool inReach, ref CursorView view);
    }

    /// <summary>
    /// In play the mouse is locked to the centre of the screen and drawn as a reticle: a dot normally,
    /// a pointing hand over things to click, an open hand over parts (closed while holding one). A label
    /// next to it names what you're aiming at. The equipped tool can change both (e.g. the saw shows
    /// whether a part can be cut). When a menu opens the normal mouse pointer comes back.
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
        public LayerMask RaycastMask => raycastMask;

        /// <summary>Set while the player carries something, so the cursor shows a closed hand.</summary>
        public HoverTarget Holding { get; set; }

        /// <summary>The equipped tool's say over the reticle and tooltip.</summary>
        public ICursorOverride Override { get; set; }

        /// <summary>Ray from the centre of the screen.</summary>
        public Ray AimRay => viewCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

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
                Show(new CursorView
                {
                    reticle = StyleFor(CursorKind.Grabbing).sprite, reticleColor = Color.white, large = true,
                    title = Holding.Title, hint = "Release to drop  ·  Scroll: closer / further", available = true,
                });
                return;
            }

            HoverTarget target = null;
            if (Physics.Raycast(AimRay, out var hit, 30f, raycastMask, QueryTriggerInteraction.Collide))
            {
                target = hit.collider.GetComponentInParent<HoverTarget>();
                CurrentHit = hit;
            }

            SetHover(target);
            CurrentInReach = target != null && hit.distance <= reach;

            var kind = CurrentInReach ? target.Cursor : CursorKind.Default;
            var view = new CursorView
            {
                reticle = kind == CursorKind.Default ? null : StyleFor(kind).sprite,
                reticleColor = Color.white,
                large = kind != CursorKind.Default,
                title = target != null ? target.Title : null,
                hint = target == null ? null : CurrentInReach ? target.Hint : "Move closer to use",
                available = CurrentInReach,
            };
            Override?.Describe(target, CurrentInReach, ref view);
            Show(view);
        }

        void Show(CursorView view)
        {
            hud.SetReticle(view.reticle, view.large, view.reticleColor);
            if (string.IsNullOrEmpty(view.title))
                hud.HideTooltip();
            else
                hud.ShowTooltip(view.title, view.hint, view.available);
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

        public CursorStyle StyleFor(CursorKind kind)
        {
            foreach (var style in styles)
                if (style.kind == kind)
                    return style;
            return default;
        }
    }
}
