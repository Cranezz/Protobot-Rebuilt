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
        public string comment;   // the note beside it (right-click > Add comment); a # line above its Python

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

    /// <summary>V2 (Blocks): a My Block (VEXcode's Make a Block): its name, then labels and inputs in order.</summary>
    public sealed class MyBlock {
        public string key;    // stable id ("call:key", "define:key")
        public string name;
        public List<MyBlockPart> parts = new();

        [JsonIgnore] public IEnumerable<MyBlockPart> Inputs => parts.Where(p => p.kind != "label");
        [JsonIgnore] public string Function => BlockCode.Ident(name);
    }

    public sealed class MyBlockPart {
        public string kind;   // "number", "boolean" or "label"
        public string text;   // the input's name or the label's words
        public string id;     // an input's stable id (calls keep their values when the block is edited)
    }

    /// <summary>V2 (Blocks): a list (VEXcode's Make a List: a fixed number of items) or 2D list (rows x columns), numbers.</summary>
    public sealed class ListVar {
        public string name;
        public int size, rows, cols;
    }

    /// <summary>V2 (Blocks): a comment on the canvas, not on a block.</summary>
    public sealed class Note {
        public string text;
        public float x, y;
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
        public List<ListVar> lists = new(), lists2 = new();
        public List<MyBlock> myBlocks = new();
        public List<Note> notes = new();
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
                return Prepare(JsonConvert.DeserializeObject<BlockProgram>(json, Settings) ?? new BlockProgram());
            }
            catch (Exception e) {
                UnityEngine.Debug.LogWarning("[Blocks] couldn't read the blocks: " + e.Message);
                return null;
            }
        }

        public string Json() => JsonConvert.SerializeObject(this, Settings);

        public static BlockProgram FromJson(string json) {
            return Prepare(JsonConvert.DeserializeObject<BlockProgram>(json, Settings) ?? new BlockProgram());
        }

        // Its own blocks (My Blocks, each list's "set") made known, then every block as its kind has it.
        private static BlockProgram Prepare(BlockProgram p) {
            p.RegisterDefs();
            p.stacks.RemoveAll(s => s == null || s.Def == null);
            foreach (var s in p.stacks) Prune(s);
            foreach (var s in p.stacks) s.Normalize();
            p.SyncMyBlocks();
            return p;
        }

        // Blocks whose kind is gone (a My Block deleted in another version) are dropped from inside stacks.
        private static void Prune(Block from) {
            for (var b = from; b != null; b = b.next) {
                while (b.next != null && b.next.Def == null) b.next = b.next.next;
                for (var i = 0; i < b.inputs.Count; i++) {
                    if (b.inputs[i] == null) continue;
                    if (b.inputs[i].Def == null) b.inputs[i] = null;
                    else Prune(b.inputs[i]);
                }
                for (var m = 0; m < b.mouths.Count; m++) {
                    while (b.mouths[m] != null && b.mouths[m].Def == null) b.mouths[m] = b.mouths[m].next;
                    if (b.mouths[m] != null) Prune(b.mouths[m]);
                }
            }
        }

        /// <summary>The program's own block kinds: a call and a define hat per My Block, a "set" per list.</summary>
        public void RegisterDefs() {
            foreach (var m in myBlocks) BlockDefs.RegisterMyBlock(m);
            foreach (var l in lists) BlockDefs.RegisterSetList(l, false);
            foreach (var l in lists2) BlockDefs.RegisterSetList(l, true);
        }

        /// <summary>Each My Block has its define hat on the canvas, holding a reporter per input (dragged out to use it).</summary>
        public void SyncMyBlocks() {
            var bottom = 10f;
            foreach (var s in stacks) bottom = Math.Max(bottom, s.y + 120f);
            foreach (var m in myBlocks) {
                var hat = stacks.FirstOrDefault(s => s.type == "define:" + m.key);
                if (hat == null) {
                    hat = Block.Make(BlockDefs.ById["define:" + m.key]);
                    hat.x = 10f;
                    hat.y = bottom;
                    bottom += 120f;
                    stacks.Add(hat);
                }
                var inputs = m.Inputs.ToList();
                for (var i = 0; i < inputs.Count && i < hat.inputs.Count; i++) {
                    var arg = Block.Make(BlockDefs.ById[inputs[i].kind == "boolean" ? "argbool" : "arg"]);
                    arg.fields[0] = BlockCode.Ident(inputs[i].text);
                    hat.inputs[i] = arg;
                }
            }
        }

        /// <summary>The names a variable slot of that kind lists.</summary>
        public List<string> Names(string kind) => kind switch {
            "bool" => booleans,
            "event" => events,
            "list" => lists.Select(l => l.name).ToList(),
            "list2" => lists2.Select(l => l.name).ToList(),
            _ => numbers,
        };

        /// <summary>A name is free for a new variable, boolean, message, list or My Block.</summary>
        public bool NameFree(string ident, MyBlock except = null) =>
            !numbers.Contains(ident) && !booleans.Contains(ident) && !events.Contains(ident) && lists.All(l => l.name != ident) && lists2.All(l => l.name != ident)
            && myBlocks.All(m => m == except || m.Function != ident);

        public MyBlock MyBlockOf(Block b) {
            var type = b?.type ?? "";
            if (!(type.StartsWith("call:") || type.StartsWith("define:"))) return null;
            var key = type.Substring(type.IndexOf(':') + 1);
            return myBlocks.FirstOrDefault(m => m.key == key);
        }

        public bool IsDefine(Block b) => b != null && b.type != null && b.type.StartsWith("define:");

        /// <summary>The My Block changed (name, labels, inputs): its calls keep the values of the inputs that stay, and
        /// reporters of a renamed input in its definition take the new name.</summary>
        public void EditMyBlock(MyBlock m, string name, List<MyBlockPart> parts) {
            var oldInputs = m.Inputs.ToList();
            var newInputs = parts.Where(p => p.kind != "label").ToList();
            var hat = stacks.FirstOrDefault(s => s.type == "define:" + m.key);
            if (hat != null)
                foreach (var old in oldInputs) {
                    var now = newInputs.FirstOrDefault(p => p.id == old.id);
                    if (now == null || now.text == old.text) continue;
                    foreach (var b in hat.All())
                        if ((b.type == "arg" || b.type == "argbool") && b.fields.Count > 0 && b.fields[0] == BlockCode.Ident(old.text)) b.fields[0] = BlockCode.Ident(now.text);
                }
            foreach (var call in Everything().Where(b => b.type == "call:" + m.key).ToList()) {
                var fields = new List<string>();
                var inputs = new List<Block>();
                foreach (var p in newInputs) {
                    var i = oldInputs.FindIndex(o => o.id == p.id && o.kind == p.kind);
                    fields.Add(i >= 0 && i < call.fields.Count ? call.fields[i] : p.kind == "number" ? "0" : "");
                    inputs.Add(i >= 0 && i < call.inputs.Count ? call.inputs[i] : null);
                }
                call.fields = fields;
                call.inputs = inputs;
            }
            if (hat != null) {
                hat.fields = newInputs.Select(p => p.kind == "number" ? "0" : "").ToList();
                hat.inputs = newInputs.Select(_ => (Block)null).ToList();
            }
            m.name = name;
            m.parts = parts;
            BlockDefs.RegisterMyBlock(m);
            SyncMyBlocks();
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
