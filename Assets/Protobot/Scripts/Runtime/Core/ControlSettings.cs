using System;
using System.IO;
using UnityEngine;

namespace Protobot.Core
{
    [Serializable]
    public class ControlSettingsData
    {
        /// <summary>Multiplier on the default mouse speed.</summary>
        public float mouseSensitivity = 1f;
        public bool invertY;
        public float fieldOfView = 70f;

        public ControlSettingsData Clone() => (ControlSettingsData)MemberwiseClone();
    }

    /// <summary>Mouse and camera preferences, saved next to the graphics settings.</summary>
    public static class ControlSettings
    {
        static string FilePath => Path.Combine(Application.persistentDataPath, "Settings", "controls.json");

        static ControlSettingsData current;

        public static ControlSettingsData Current => current ??= Load();

        public static event Action<ControlSettingsData> Changed;

        public static void Apply(ControlSettingsData settings)
        {
            current = settings.Clone();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, JsonUtility.ToJson(current, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Controls] Couldn't save {FilePath}. {e.Message}");
            }
            Changed?.Invoke(current);
        }

        static ControlSettingsData Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonUtility.FromJson<ControlSettingsData>(File.ReadAllText(FilePath)) ?? new ControlSettingsData();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Controls] Couldn't read {FilePath}, using defaults. {e.Message}");
            }
            return new ControlSettingsData();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            current = null;
            Changed = null;
        }
    }
}
