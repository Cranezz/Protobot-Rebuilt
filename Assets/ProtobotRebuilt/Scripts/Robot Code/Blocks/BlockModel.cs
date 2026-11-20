using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace Protobot.RobotCode.Blocks {
    /// <summary>V2 (Blocks): one block on the canvas: its kind, what's in its slots (a typed value or a block dropped
    /// in), the block under it and the first block in each of its mouths (C blocks).</summary>
    public sealed class Block {
        public string type;
        public List<string> fields = new();
        public List<Block> inputs = new();
        public bool toggle;
        public Block next;
        public List<Block> mouths = new();
        public float x, y;   // where a stack's top block sits on the canvas

        [JsonIgnore] public BlockDef Def => BlockDefs.ById.TryGetValue(type ?? "", out var d) ? d : null;

        public static Block Make(BlockDef def) {
            var b = new Block { type = def.id, toggle = def.toggleDefault };
            foreach (var slot in def.slots) {
                b.fields.Add(slot.initial ?? "");
                b.inputs.Add(null);
            }
            for (var i = 0; i < def.Mouths; i++) b.mouths.Add(null);
            return b;
        }

        /// <summary>Slots and mouths as its kind has them (a program saved by another version).</summary>
        public void Normalize() {
            var def = Def;
            if (def == null) return;
            while (fields.Count < def.slots.Count) fields.Add(def.slots[fields.Count].initial ?? "");
            while (inputs.Count < def.slots.Count) inputs.Add(null);
            while (mouths.Count < def.Mouths) mouths.Add(null);
            if (fields.Count > def.slots.Count) fields.RemoveRange(def.slots.Count, fields.Count - def.slots.Count);
            if (inputs.Count > def.slots.Count) inputs.RemoveRange(def.slots.Count, inputs.Count - def.slots.Count);
            if (mouths.Count > def.Mouths) mouths.RemoveRange(def.Mouths, mouths.Count - def.Mouths);
            foreach (var i in inputs) i?.Normalize();
            foreach (var m in mouths) for (var b = m; b != null; b = b.next) b.Normalize();
            next?.Normalize();
        }

        /// <summary>This block and every block inside it (slots, mouths) and under it.</summary>
        public IEnumerable<Block> All() {
            for (var b = this; b != null; b = b.next) {
                yield return b;
                foreach (var i in b.inputs) if (i != null) foreach (var x in i.All()) yield return x;
                foreach (var m in b.mouths) if (m != null) foreach (var x in m.All()) yield return x;
            }
        }

        public Block Clone() => JsonConvert.DeserializeObject<Block>(JsonConvert.SerializeObject(this, BlockProgram.Settings), BlockProgram.Settings);

        public Block Last() {
            var b = this;
            while (b.next != null) b = b.next;
            return b;
        }
    }

    /// <summary>V2 (Blocks): a blocks program: its stacks on the canvas, its variables and messages, and the robot
    /// configuration it was made with (VEXcode's devices, from Insert devices). Saved as Python with the blocks in a
    /// comment at the top, so a blocks program is an ordinary program everywhere else (saved builds, Run, the game,
    /// online matches, VS Code).</summary>
    public sealed class BlockProgram {
        public const string Marker = "# Protobot blocks:";
        public const string Notice = "# Made from blocks in the code window (Blocks): changes to this text are replaced when the blocks change.";

        public List<Block> stacks = new();
        public List<string> numbers = new() { "myVariable" };
        public List<string> booleans = new() { "myBoolean" };
        public List<string> events = new() { "message1" };
        public string config = "";

        internal static readonly JsonSerializerSettings Settings = new() {
            NullValueHandling = NullValueHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Ignore,
            MaxDepth = 4000,
            ObjectCreationHandling = ObjectCreationHandling.Replace,   // (the default lists aren't added to)
            Formatting = Formatting.None,
        };

        public static bool IsBlocks(string source) => source != null && source.StartsWith(Marker, StringComparison.Ordinal);

        /// <summary>The blocks in a program's first line, or null when it isn't a blocks program (or they can't be read).</summary>
        public static BlockProgram FromSource(string source) {
            if (!IsBlocks(source)) return null;
            var end = source.IndexOf('\n');
            var data = (end < 0 ? source : source.Substring(0, end)).Substring(Marker.Length).Trim();
            try {
                var json = Inflate(Convert.FromBase64String(data));
                var program = JsonConvert.DeserializeObject<BlockProgram>(json, Settings) ?? new BlockProgram();
                program.stacks.RemoveAll(s => s == null || s.Def == null);
                foreach (var s in program.stacks) s.Normalize();
                return program;
            }
            catch (Exception e) {
                UnityEngine.Debug.LogWarning("[Blocks] couldn't read the blocks: " + e.Message);
                return null;
            }
        }

        public string Json() => JsonConvert.SerializeObject(this, Settings);

        public static BlockProgram FromJson(string json) {
            var p = JsonConvert.DeserializeObject<BlockProgram>(json, Settings) ?? new BlockProgram();
            foreach (var s in p.stacks) s.Normalize();
            return p;
        }

        /// <summary>The whole program as saved: the blocks line, the notice, then the Python they make.</summary>
        public string ToSource(out BlockCode code) {
            code = BlockCode.Generate(this);
            return Marker + " " + Convert.ToBase64String(Deflate(Json())) + "\n" + Notice + "\n" + code.python;
        }

        /// <summary>Where a block is: the block it's under, the block whose slot or mouth it's in, or the canvas.</summary>
        public (Block parent, int slot, int mouth) Find(Block target) {
            foreach (var top in stacks) {
                if (top == target) return (null, -1, -1);
                var found = Find(top, target);
                if (found.parent != null) return found;
            }
            return (null, -2, -2);
        }

        private static (Block parent, int slot, int mouth) Find(Block from, Block target) {
            for (var b = from; b != null; b = b.next) {
                if (b.next == target) return (b, -1, -1);
                for (var i = 0; i < b.inputs.Count; i++) {
                    if (b.inputs[i] == null) continue;
                    if (b.inputs[i] == target) return (b, i, -1);
                    var f = Find(b.inputs[i], target);
                    if (f.parent != null) return f;
                }
                for (var m = 0; m < b.mouths.Count; m++) {
                    if (b.mouths[m] == null) continue;
                    if (b.mouths[m] == target) return (b, -1, m);
                    var f = Find(b.mouths[m], target);
                    if (f.parent != null) return f;
                }
            }
            return (null, -2, -2);
        }

        /// <summary>Takes the block (and what's under it) out of where it is; it's then in no stack.</summary>
        public void Detach(Block target) {
            var (parent, slot, mouth) = Find(target);
            if (parent == null) {
                if (slot == -1) stacks.Remove(target);
                return;
            }
            if (slot >= 0) parent.inputs[slot] = null;
            else if (mouth >= 0) parent.mouths[mouth] = null;
            else parent.next = null;
        }

        public IEnumerable<Block> Everything() => stacks.SelectMany(s => s.All());

        private static byte[] Deflate(string text) {
            using var output = new MemoryStream();
            using (var zip = new DeflateStream(output, CompressionLevel.Optimal)) {
                var bytes = Encoding.UTF8.GetBytes(text);
                zip.Write(bytes, 0, bytes.Length);
            }
            return output.ToArray();
        }

        private static string Inflate(byte[] data) {
            using var input = new DeflateStream(new MemoryStream(data), CompressionMode.Decompress);
            using var reader = new StreamReader(input, Encoding.UTF8);
            return reader.ReadToEnd();
        }
    }
}
