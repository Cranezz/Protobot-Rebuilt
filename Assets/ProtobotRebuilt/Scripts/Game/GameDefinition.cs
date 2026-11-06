using UnityEngine;

namespace Protobot.Game {
    // V2: a competition game the app can play: its field models (Assets/Protobot/Field/<game>, full detail and light)
    // and its timing. Kept in Resources/Games so a built app can load the fields. Made by
    // Tools > Protobot > Build Game Definitions (Editor/GameDefinitionBuilder).
    [CreateAssetMenu(menuName = "Protobot/Game Definition")]
    public class GameDefinition : ScriptableObject {
        public string title = "Override";
        public string season = "2026-27";
        public string program = "V5RC";
        [Tooltip("The full-detail field (drawn on Medium and above).")]
        public GameObject field;
        [Tooltip("The light field: drawn on Very Low and Low, and always the source of the collision shapes.")]
        public GameObject fieldLight;
        public float skillsSeconds = 60f;

        public string Name => $"{program} {title} ({season})";

        public static GameDefinition[] All() => Resources.LoadAll<GameDefinition>("Games");
    }
}
