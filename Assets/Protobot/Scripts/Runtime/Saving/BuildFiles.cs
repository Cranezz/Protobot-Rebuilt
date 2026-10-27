using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Protobot.Saving
{
    /// <summary>A saved build as listed in the menu.</summary>
    public struct BuildFile
    {
        public string path;
        public string name;
        public int parts;
        public DateTime saved;
        public bool autosave;
    }

    /// <summary>
    /// Builds saved as JSON files in the player's data folder (Builds/), one per name. The autosave is kept
    /// apart from the named ones and is written every minute and when the game closes.
    /// </summary>
    public static class BuildFiles
    {
        public const string AutosaveName = "Autosave";
        const string AutosaveFile = "_autosave.json";

        public static string Folder => Path.Combine(Application.persistentDataPath, "Builds");
        public static string AutosavePath => Path.Combine(Folder, AutosaveFile);

        public static string PathFor(string name) => Path.Combine(Folder, FileName(name) + ".json");

        /// <summary>A file name from a build name: characters Windows doesn't allow become '_'.</summary>
        static string FileName(string name)
        {
            var chars = name.Trim().ToCharArray();
            var invalid = Path.GetInvalidFileNameChars();
            for (var i = 0; i < chars.Length; i++)
                if (Array.IndexOf(invalid, chars[i]) >= 0)
                    chars[i] = '_';
            var result = new string(chars);
            return result.Length == 0 || result.StartsWith("_autosave", StringComparison.OrdinalIgnoreCase) ? "Build" : result;
        }

        public static bool Exists(string name) => File.Exists(PathFor(name));

        public static void Write(string path, BuildData data)
        {
            Directory.CreateDirectory(Folder);
            data.version = BuildData.CurrentVersion;
            data.savedAt = DateTime.Now.ToString("s", CultureInfo.InvariantCulture);
            // Write beside it first, so a crash mid-write never leaves a half-written save.
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonUtility.ToJson(data, true));
            if (File.Exists(path))
                File.Delete(path);
            File.Move(temp, path);
        }

        public static BuildData Read(string path)
        {
            var data = JsonUtility.FromJson<BuildData>(File.ReadAllText(path));
            if (data == null || data.parts == null)
                throw new InvalidDataException("Not a build file");
            if (data.version > BuildData.CurrentVersion)
                throw new InvalidDataException("Saved by a newer version of the game");
            data.assemblies ??= new List<AssemblyData>();
            data.screws ??= new List<ScrewData>();
            return data;
        }

        public static void Delete(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
        }

        /// <summary>Every saved build, newest first (the autosave first of all).</summary>
        public static List<BuildFile> List()
        {
            var result = new List<BuildFile>();
            if (!Directory.Exists(Folder))
                return result;
            foreach (var path in Directory.GetFiles(Folder, "*.json"))
            {
                var file = new BuildFile { path = path, autosave = Path.GetFileName(path) == AutosaveFile };
                try
                {
                    var data = Read(path);
                    file.name = file.autosave ? AutosaveName : !string.IsNullOrEmpty(data.name) ? data.name : Path.GetFileNameWithoutExtension(path);
                    file.parts = data.parts.Count;
                    file.saved = DateTime.TryParse(data.savedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var when)
                        ? when : File.GetLastWriteTime(path);
                }
                catch (Exception)
                {
                    continue;  // not a build (or damaged): not listed
                }
                result.Add(file);
            }
            result.Sort((a, b) => a.autosave != b.autosave ? (a.autosave ? -1 : 1) : b.saved.CompareTo(a.saved));
            return result;
        }
    }
}
