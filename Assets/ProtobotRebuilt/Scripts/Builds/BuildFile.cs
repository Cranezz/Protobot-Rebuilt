using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using Protobot.Custom;
using UnityEngine;

namespace Protobot.Builds {
    // V2: the .pbb file. It's now a zip: "build.json" (the BuildData, readable) and "parts/<key>.json" for every
    // custom part the build uses (imported models, polycarbonate pieces), so a build sent to someone else opens
    // with its parts, which also go into their parts library. Files from the original (a BinaryFormatter
    // stream) still open; they're written in the new form the next time they're saved.
    public static class BuildFile {
        public const int FormatVersion = 2;

        [Serializable]
        private class Manifest {
            public int format = FormatVersion;
            public string app = AppData.Version;
            public string[] customParts = Array.Empty<string>();
        }

        // V2: the old format is only read now (every part is cut from full-length stock, which old versions
        // don't have); an old file is saved in the new format, with a copy of the old file kept next to it.
        public static bool IsOld(string path) => File.Exists(path) && !IsZip(path);

        /// <summary>Saves an old file in the new format now, keeping a copy of the old one next to it.</summary>
        public static string Upgrade(string path, BuildData build) {
            var backup = Path.Combine(Path.GetDirectoryName(path) ?? "", Path.GetFileNameWithoutExtension(path) + " (old format).pbb");
            File.Copy(path, backup, true);
            WriteNew(path, build);
            return backup;
        }

        /// <summary>How many parts in the build are custom (imported or polycarbonate): old files can't hold them.</summary>
        public static int CustomPartCount(BuildData build) => build.parts.Count(p => CustomParts.Find(p.partId) != null || IsCustomId(p.partId));

        private static bool IsCustomId(string id) => id != null && (id.StartsWith(CustomParts.Imported + "-") || id.StartsWith(CustomParts.Polycarbonate + "-"));

        public static void Write(string path, BuildData build) {
            if (IsOld(path)) {
                Upgrade(path, build);
                return;
            }
            WriteNew(path, build);
        }

        public static string BackupFolder => Path.Combine(Application.persistentDataPath, "Backups");
        private const int BackupsKept = 10;
        private static readonly TimeSpan BackupEvery = TimeSpan.FromMinutes(10);

        /// <summary>
        /// V2 (the original's issue #82, broken files losing parts): before a build file is replaced, the old one is
        /// copied to Backups/&lt;build name&gt;/&lt;time&gt;.pbb, at most every 10 minutes (autosave saves every minute),
        /// keeping the newest 10.
        /// </summary>
        private static void Backup(string path) {
            try {
                var folder = Path.Combine(BackupFolder, Path.GetFileNameWithoutExtension(path));
                Directory.CreateDirectory(folder);
                var existing = new DirectoryInfo(folder).GetFiles("*.pbb").OrderByDescending(f => f.CreationTimeUtc).ToList();
                if (existing.Count > 0 && DateTime.UtcNow - existing[0].CreationTimeUtc < BackupEvery) return;
                File.Copy(path, Path.Combine(folder, DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + ".pbb"), true);
                foreach (var old in existing.Skip(BackupsKept - 1)) old.Delete();
            }
            catch (Exception e) {
                Debug.LogWarning("[Builds] Couldn't back up " + path + ": " + e.Message);
            }
        }

        private static void WriteNew(string path, BuildData build) {
            var used = new HashSet<string>(build.parts.Select(p => p.partId).Where(id => CustomParts.Find(id) != null));
            var temp = path + ".saving";
            using (var stream = File.Create(temp))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create)) {
                AddText(zip, "build.json", JsonUtility.ToJson(build, true));
                var keys = new List<string>();
                foreach (var id in used) {
                    var part = CustomParts.Find(id);
                    AddText(zip, "parts/" + part.key + ".json", JsonUtility.ToJson(part));
                    keys.Add(part.key);
                }
                AddText(zip, "manifest.json", JsonUtility.ToJson(new Manifest { customParts = keys.ToArray() }, true));
            }
            // Replace the old file only once the new one is complete.
            if (File.Exists(path)) {
                Backup(path);
                File.Delete(path);
            }
            File.Move(temp, path);
        }

        public static BuildData Read(string path) {
            if (!File.Exists(path)) return null;
            if (IsZip(path)) {
                using var stream = File.OpenRead(path);
                using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
                foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith("parts/") && e.FullName.EndsWith(".json"))) {
                    try {
                        var part = JsonUtility.FromJson<CustomPartData>(ReadText(entry));
                        // A part deleted from the library only serves this build (it isn't put back).
                        if (part != null && !string.IsNullOrEmpty(part.key))
                            CustomParts.AddFromBuild(part);
                    }
                    catch (Exception e) {
                        Debug.LogWarning("[Build file] Couldn't read " + entry.FullName + ": " + e.Message);
                    }
                }
                var buildEntry = zip.GetEntry("build.json");
                return buildEntry != null ? JsonUtility.FromJson<BuildData>(ReadText(buildEntry)) : null;
            }
            // The original's format.
            using var file = File.Open(path, FileMode.Open);
            return (BuildData)new BinaryFormatter().Deserialize(file);
        }

        private static bool IsZip(string path) {
            using var file = File.OpenRead(path);
            return file.Length > 4 && file.ReadByte() == 'P' && file.ReadByte() == 'K';
        }

        private static void AddText(ZipArchive zip, string name, string text) {
            var entry = zip.CreateEntry(name, System.IO.Compression.CompressionLevel.Optimal);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(text);
        }

        private static string ReadText(ZipArchiveEntry entry) {
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
            return reader.ReadToEnd();
        }
    }
}
