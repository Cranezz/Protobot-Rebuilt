using Protobot.Interaction;
using Protobot.Menus;
using UnityEngine;

namespace Protobot.Tools
{
    /// <summary>
    /// A tool the player can hold (hands, hack saw, spray paint, drill). The <see cref="ToolBelt"/> calls these
    /// only while the tool is equipped and no menu is open. Desktop controls: left click = use,
    /// right click = alternate action (tap) / tool menu (hold), mouse wheel = adjust.
    /// </summary>
    public abstract class PlayerTool : MonoBehaviour
    {
        [SerializeField] string displayName;
        [SerializeField] Sprite icon;
        [SerializeField] GameObject viewModelPrefab;

        public string DisplayName => displayName;
        public Sprite Icon => icon;
        public GameObject ViewModelPrefab => viewModelPrefab;

        protected ToolBelt Belt { get; private set; }
        protected CursorController Cursor => Belt.Cursor;
        protected Hud Hud => Belt.Hud;
        protected Camera View => Belt.ViewCamera;

        /// <summary>The tool's model in the player's hand (null if it has none).</summary>
        protected GameObject ViewModel { get; private set; }

        /// <summary>Plays a sound from the tool in hand.</summary>
        protected void PlaySound(AudioClip clip, float volume = 0.6f) => Belt.PlaySound(clip, volume);

        internal void Bind(ToolBelt belt, GameObject viewModel)
        {
            Belt = belt;
            ViewModel = viewModel;
        }

        public virtual void OnEquip() { }
        public virtual void OnUnequip() { }

        /// <summary>Every frame while equipped and playing (not in a menu).</summary>
        public virtual void Tick() { }

        /// <summary>A menu opened (pause, picker, the tool's own menu): drop anything in progress.</summary>
        public virtual void OnSuspend() { }

        public virtual void PrimaryDown() { }
        public virtual void PrimaryUp() { }

        /// <summary>Right mouse button tapped (released within half a second).</summary>
        public virtual void SecondaryTap() { }

        /// <summary>Right mouse button held for half a second: usually opens the tool's precise menu.</summary>
        public virtual void SecondaryHold() { }

        /// <summary>Mouse wheel, in notches (positive = away from you).</summary>
        public virtual void Scroll(int notches) { }

        /// <summary>
        /// Lets the tool restyle the reticle and tooltip for what's under it. Return false to keep the defaults
        /// (pointing hand over bins, open hand over parts, the object's own hint).
        /// </summary>
        public virtual bool DescribeTarget(HoverTarget target, bool inReach, ref CursorView view) => false;

        /// <summary>Short reminder of this tool's controls, shown under the hotbar.</summary>
        public virtual string ControlsHint => "";
    }
}
