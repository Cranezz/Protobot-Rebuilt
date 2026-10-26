using System;
using Protobot.InputHandling;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Protobot.Interaction
{
    /// <summary>
    /// Finds what the mouse is over and changes the cursor to match: a pointing hand over things to click,
    /// an open hand over parts to pick up, a closed hand while holding the button on a part. Shows a tooltip
    /// with the object's name and what clicking does, and hides the cursor while the player is looking around.
    /// </summary>
    public class CursorController : MonoBehaviour
    {
        [Serializable]
        public struct CursorStyle
        {
            public CursorKind kind;
            public Texture2D texture;
            public Vector2 hotspot;
        }

        [SerializeField] Camera viewCamera;
        [SerializeField] DesktopPlayerInput player;
        [SerializeField] CursorStyle[] styles;
        [SerializeField] HoverTooltip tooltip;

        [Tooltip("How far away (m) the player can reach things. Further away they still show their name.")]
        [SerializeField] float reach = 3f;
        [SerializeField] LayerMask raycastMask = ~(1 << 2);  // everything except Ignore Raycast (the player)

        HoverTarget current;
        CursorKind shownKind = (CursorKind)(-1);

        public HoverTarget Current => current;
        public bool CurrentInReach { get; private set; }

        void OnDisable()
        {
            SetHover(null);
            UnityEngine.Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            shownKind = (CursorKind)(-1);
        }

        void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null)
                return;

            if (player != null && player.IsLooking)
            {
                UnityEngine.Cursor.visible = false;
                SetHover(null);
                tooltip?.Hide();
                return;
            }
            UnityEngine.Cursor.visible = true;

            var screenPos = mouse.position.ReadValue();
            HoverTarget target = null;
            var distance = 0f;
            if (Physics.Raycast(viewCamera.ScreenPointToRay(screenPos), out var hit, 30f, raycastMask, QueryTriggerInteraction.Collide))
            {
                target = hit.collider.GetComponentInParent<HoverTarget>();
                distance = hit.distance;
            }

            SetHover(target);
            CurrentInReach = target != null && distance <= reach;

            var kind = CurrentInReach ? target.Cursor : CursorKind.Default;
            if (kind == CursorKind.Grab && mouse.leftButton.isPressed)
                kind = CursorKind.Grabbing;
            ShowCursor(kind);

            if (target == null)
                tooltip?.Hide();
            else
                tooltip?.Show(target.Title, CurrentInReach ? target.Hint : "Move closer to use", screenPos, CurrentInReach);
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
        }

        void ShowCursor(CursorKind kind)
        {
            if (kind == shownKind)
                return;
            shownKind = kind;
            foreach (var style in styles)
            {
                if (style.kind == kind)
                {
                    UnityEngine.Cursor.SetCursor(style.texture, style.hotspot, CursorMode.Auto);
                    return;
                }
            }
            UnityEngine.Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        }
    }
}
