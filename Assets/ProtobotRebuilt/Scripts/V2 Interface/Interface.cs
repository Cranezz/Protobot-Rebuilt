using Protobot.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Protobot.Interface {
    // V2: hosts the new interface: its own overlay canvas (above the original Build Canvas, below its pop-up
    // menus), the tool rail, the tool options bar and the tooltip. Fonts and icons are set by
    // Tools > Protobot > Build V2 Interface.
    public class Interface : MonoBehaviour {
        public static Interface Instance { get; private set; }
        public static RectTransform Root { get; private set; }

        [SerializeField] private TMP_FontAsset regular, medium, bold;
        public Sprite measureIcon, chevronIcon, closeIcon, searchIcon;
        public Sprite sawIcon, polycarbIcon, importIcon, sheetsIcon;
        [UnityEngine.Serialization.FormerlySerializedAs("joinIcon")] public Sprite selectIcon;  // V2: the Join tool was removed
        public Material metalMaterial, plasticMaterial, polycarbMaterial;

        private FeedbackDisplay feedback;

        private void Awake() {
            Instance = this;
            Theme.Regular = regular;
            Theme.Medium = medium != null ? medium : regular;
            Theme.Bold = bold != null ? bold : Theme.Medium;
            feedback = FindFirstObjectByType<FeedbackDisplay>(FindObjectsInactive.Include);

            var canvas = gameObject.GetOrAdd<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5;
            var scaler = gameObject.GetOrAdd<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
            if (GetComponent<GraphicRaycaster>() == null)
                gameObject.AddComponent<GraphicRaycaster>();
            Root = (RectTransform)transform;

            // The original's pop-ups (preferences, launch window, save warning) stay above the new interface.
            foreach (var other in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (other.isRootCanvas && other != canvas && other.renderMode == RenderMode.ScreenSpaceOverlay
                    && other.name != "Build Canvas" && other.name != "Constant Pixel Size Canvas")
                    other.sortingOrder = Mathf.Max(other.sortingOrder, 0) + 10;

            // The feedback line becomes a toast at the bottom (the tool options bar is at the top now).
            if (feedback != null && feedback.TryGetComponent(out UIMovement movement)) {
                var rect = (RectTransform)feedback.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
                rect.pivot = new Vector2(0.5f, 0f);
                movement.activePos = new Vector2(0f, 24f);
                movement.inactivePos = new Vector2(0f, -40f);
                rect.anchoredPosition = movement.inactivePos;
            }

            // The original's pop-ups in the new colors.
            foreach (var other in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (other.isRootCanvas && (other.name == "Preferences Canvas" || other.GetComponentInChildren<UnsavedChangesUI>(true) != null))
                    ClassicRestyle.Apply(other.gameObject);

            Overlay.Create(Root);
            TopBar.Create(Root);
            ToolRail.Create(Root);
            ToolOptions.Create(Root);
            PartsBrowser.Create(Root);
            Tooltip.Create(Root);
            PartMenu.Create(Root);
            Builds.AutoSave.Create(Root);
            CameraButtons.Apply();
            Motion.MotorControls.Create(Root);
            gameObject.AddComponent<Tools.PaintTool>();
        }

        /// <summary>The original's feedback line at the top: success (green tick) or alert.</summary>
        public static void Say(string message, bool success = true) {
            if (Instance != null && Instance.feedback != null)
                Instance.feedback.Show(message, success ? FeedbackDisplay.DisplayType.success : FeedbackDisplay.DisplayType.alert);
        }
    }
}
