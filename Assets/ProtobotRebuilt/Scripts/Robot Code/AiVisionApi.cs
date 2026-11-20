using System;
using System.Collections.Generic;
using System.Linq;
using Protobot.Devices;
using Protobot.Scripting;
using UnityEngine;
using static Protobot.RobotCode.VexModule;

namespace Protobot.RobotCode {
    // V2: the AI Vision Sensor in robot code, on the simulated sensor (Devices/AiVision.cs):
    //   VEXcode Python: ai_vision_1__Red = Colordesc(1, 207, 19, 25, 10, 0.2); ai_vision_1__Code = Codedesc(1, red, blue);
    //     ai_vision_1 = AiVision(Ports.PORT1, ai_vision_1__Red, AiVision.ALL_AIOBJS); objs = ai_vision_1.take_snapshot(
    //     ai_vision_1__Red | AiVision.ALL_AIOBJS | AiVision.ALL_TAGS, count); objects: id, centerX/Y, originX/Y, width,
    //     height, angle, exists, score (largest first);
    //   VEXcode C++: aivision / aivision::colordesc / aivision::codedesc, takeSnapshot returns the count, objects[i],
    //     objectCount, largestObject;
    //   PROS: pros::AIVision (get_object_count, get_object(i) -> aivision_object_s_t {id, type, object.color/tag/element},
    //     set_color({id, r, g, b, hue_range, saturation_range}), set_code, enable/disable_detection_types...).
    internal static class AiVisionApi {
        public const int AllAiObjs = 1, AllTags = 2, AllColors = 3, AllCodes = 4, AllObjects = 5;

        public static void Register(RobotProgram program, PyModule m) {
            var cls = new PyClass("AiVision", null) { construct = (_, a) => new VexAiVision(program, a) };
            cls.dict["ALL_AIOBJS"] = new PyEnum("AiVisionType", "ALL_AIOBJS", AllAiObjs);
            cls.dict["ALL_TAGS"] = new PyEnum("AiVisionType", "ALL_TAGS", AllTags);
            cls.dict["ALL_COLORS"] = new PyEnum("AiVisionType", "ALL_COLORS", AllColors);
            cls.dict["ALL_CODES"] = new PyEnum("AiVisionType", "ALL_CODES", AllCodes);
            cls.dict["ALL_OBJECTS"] = new PyEnum("AiVisionType", "ALL_OBJECTS", AllObjects);
            var color = new PyClass("Colordesc", null) { construct = (_, a) => new VexAiColor(a) };
            var code = new PyClass("Codedesc", null) { construct = (_, a) => new VexAiCode(a) };
            cls.dict["colordesc"] = color;
            cls.dict["codedesc"] = code;
            m.globals["AiVision"] = cls;
            m.globals["Colordesc"] = color;
            m.globals["Codedesc"] = code;
        }

        /// <summary>Which objects a snapshot argument asks for.</summary>
        public static Func<AiObject, bool> Filter(object what) => what switch {
            VexAiColor c => o => o.kind == AiKind.Color && o.id == c.color.id,
            VexAiCode c => o => o.kind == AiKind.Code && o.id == AiVisionSensor.CodeId(c.ids),
            PyEnum e when e.type == "AiVisionType" => e.value switch {
                AllAiObjs => o => o.kind == AiKind.Object,
                AllTags => o => o.kind == AiKind.Tag,
                AllColors => o => o.kind == AiKind.Color,
                AllCodes => o => o.kind == AiKind.Code,
                _ => o => true,
            },
            long n => o => o.kind == AiKind.Color && o.id == n,
            _ => o => true,
        };
    }

    internal sealed class VexAiColor : VexObject {
        public override string TypeName => "Colordesc";
        public readonly AiColor color;

        public VexAiColor(Args a) : this(Read(a)) { }

        public VexAiColor(AiColor c) {
            color = c;
            Field("id", (long)c.id);
            Field("red", (long)c.r);
            Field("green", (long)c.g);
            Field("blue", (long)c.b);
            Field("hangle", (double)c.hue);
            Field("hdsat", (double)c.saturation);
        }

        private static AiColor Read(Args a) {
            int I(int i, string k) => (int)Math.Round(a.Number(i, k, 0));
            var c = new AiColor { id = I(0, "index"), r = I(1, "red"), g = I(2, "green"), b = I(3, "blue"), hue = (float)a.Number(4, "hangle", 10), saturation = (float)a.Number(5, "hdsat", 0.2) };
            c.name = "COLOR_" + c.id;
            if (!c.Valid) throw new PyError("ValueError", "a Colordesc's index is 1 to 7, then red, green, blue (0-255), the hue range (1-40) and the saturation range (0.1-1)");
            c.r = Mathf.Clamp(c.r, 0, 255); c.g = Mathf.Clamp(c.g, 0, 255); c.b = Mathf.Clamp(c.b, 0, 255);
            return c;
        }
    }

    internal sealed class VexAiCode : VexObject {
        public override string TypeName => "Codedesc";
        public readonly int[] ids;
        public readonly List<AiColor> colors = new();

        public VexAiCode(Args a) {
            var items = a.pos.ToList();
            if (items.Count > 0 && (items[0] is long || items[0] is double)) items.RemoveAt(0);   // Codedesc(index, colors...)
            foreach (var s in items) {
                if (s is VexAiColor c) colors.Add(c.color);
                else if (s is long n && n >= 1 && n <= 7) colors.Add(new AiColor { id = (int)n });
            }
            if (colors.Count < 2 || colors.Count > 5) throw new PyError("ValueError", "a Codedesc takes 2 to 5 Colordescs");
            ids = colors.Select(c => c.id).ToArray();
            Field("id", (long)AiVisionSensor.CodeId(ids));
        }
    }

    internal sealed class VexAiObject : VexObject {
        public override string TypeName => "AiVisionObject";

        public VexAiObject(AiObject? found) {
            var o = found ?? default;
            Field("id", (long)o.id);
            Field("centerX", (long)o.CenterX);
            Field("centerY", (long)o.CenterY);
            Field("originX", (long)o.x);
            Field("originY", (long)o.y);
            Field("center_x", (long)o.CenterX);
            Field("center_y", (long)o.CenterY);
            Field("origin_x", (long)o.x);
            Field("origin_y", (long)o.y);
            Field("width", (long)o.width);
            Field("height", (long)o.height);
            Field("angle", (double)o.angle);
            Field("score", (long)o.score);
            Field("exists", found.HasValue);
            Field("classname", found.HasValue && o.kind == AiKind.Object && o.id >= 0 && o.id < AiVisionSensor.ClassNames.Length ? AiVisionSensor.ClassNames[o.id] : "");
        }
    }

    internal sealed class VexAiVision : VexObject {
        public override string TypeName => "AiVision";
        private readonly RobotProgram p;
        private readonly int port;
        private readonly PyList objects = new();

        public VexAiVision(RobotProgram p, Args a) {
            this.p = p;
            (port, _) = Port(a.Need(0, "port"), "AI Vision");
            for (var i = 0; i < AiVisionSensor.MaxObjects; i++) objects.items.Add(new VexAiObject(null));
            Field("objects", objects);
            Field("object_count", 0L);
            if (p.Cpp) Field("largest_object", new VexAiObject(null));
            else Method("largest_object", _ => objects.items[0]);
            var sensor = Sensor(false);
            if (sensor != null)
                foreach (var s in a.pos.Skip(1)) Add(sensor, s);
            Method("take_snapshot", a2 => {
                var found = Snap(AiVisionApi.Filter(a2.Need(0, "type")), (int)a2.Number(1, "count", 8));
                if (p.Cpp) return (long)found.Count;
                return new PyTuple(found.Select(o => (object)new VexAiObject(o)).ToArray());
            });
            Method("tag_detection", a2 => { var s = Sensor(true); if (s != null) s.tags = Ops.Truthy(a2.Need(0, "enable")); return PyNone.Value; });
            Method("color_detection", a2 => {
                var s = Sensor(true);
                if (s != null) {
                    s.colors = Ops.Truthy(a2.Need(0, "enable"));
                    s.merge = a2.Bool(1, "merge", false);
                }
                return PyNone.Value;
            });
            Method("model_detection", a2 => { var s = Sensor(true); if (s != null) s.model = Ops.Truthy(a2.Need(0, "enable")); return PyNone.Value; });
            Method("set_color", a2 => { var s = Sensor(true); if (s != null) Add(s, a2.Need(0, "color")); return PyNone.Value; });
            Method("start_awb", _ => PyNone.Value);
            Method("installed", _ => V5Brain.Get<AiVisionSensor>(port) != null);
            Method("timestamp", _ => Math.Round(p.Time * 1000));
        }

        private AiVisionSensor Sensor(bool warn) => warn ? p.Device<AiVisionSensor>(port, false, "AI Vision Sensor") : V5Brain.Get<AiVisionSensor>(port);

        private static void Add(AiVisionSensor sensor, object s) {
            switch (s) {
                case VexAiColor c: sensor.SetColor(c.color); break;
                case VexAiCode c:
                    foreach (var color in c.colors.Where(x => x.r + x.g + x.b > 0)) sensor.SetColor(color);
                    if (!sensor.codes.Any(k => k.SequenceEqual(c.ids))) sensor.codes.Add(c.ids);
                    break;
                case PyEnum e when e.type == "AiVisionType":
                    if (e.value == AiVisionApi.AllAiObjs) sensor.model = true;
                    if (e.value == AiVisionApi.AllTags) sensor.tags = true;
                    break;
            }
        }

        private List<AiObject> Snap(Func<AiObject, bool> which, int count) {
            var s = Sensor(true);
            var found = s != null ? s.Snapshot(which, count) : new List<AiObject>();
            for (var i = 0; i < objects.items.Count; i++) objects.items[i] = new VexAiObject(i < found.Count ? found[i] : (AiObject?)null);
            Add("object_count", (long)found.Count);
            if (p.Cpp) Add("largest_object", objects.items[0]);
            return found;
        }
    }

    /// <summary>PROS's pros::AIVision on the simulated sensor (no snapshots: get_object reads the latest picture).</summary>
    internal sealed class ProsAiVision : VexObject {
        public override string TypeName => "AIVision";
        // AivisionModeType bits
        private const int Tags = 1, Colors = 2, Objects = 4, Merge = 16;

        public ProsAiVision(RobotProgram p, Args a) {
            var port = (int)Ops.ToLong(a.Need(0, "port"));
            AiVisionSensor S() => p.Device<AiVisionSensor>(port, false, "AI Vision Sensor");
            List<AiObject> All() {
                var s = S();
                if (s == null) return new List<AiObject>();
                return s.Snapshot(o => true, AiVisionSensor.MaxObjects);
            }
            int Bits(AiVisionSensor s) => (s.tags ? Tags : 0) | (s.colors ? Colors : 0) | (s.model ? Objects : 0) | (s.merge ? Merge : 0);
            void SetBits(AiVisionSensor s, int bits) {
                s.tags = (bits & Tags) != 0;
                s.colors = (bits & Colors) != 0;
                s.model = (bits & Objects) != 0;
                s.merge = (bits & Merge) != 0;
            }
            int Mask(Args a2) => a2.pos.Aggregate(0, (n, x) => n | (int)Ops.ToLong(x is PyEnum e ? (object)(long)e.value : x));
            Method("reset", _ => { S()?.PowerOn(); return 1L; });
            Method("get_enabled_detection_types", _ => { var s = S(); return s == null ? 0L : (long)Bits(s); });
            Method("set_enabled_detection_types", a2 => {
                var s = S();
                if (s != null) {
                    var bits = (int)Ops.ToLong(a2.Need(0, "bits"));
                    var mask = (int)a2.Number(1, "bitmask", 0xFF);
                    SetBits(s, (Bits(s) & ~mask) | (bits & mask));
                }
                return 1L;
            });
            Method("enable_detection_types", a2 => { var s = S(); if (s != null) SetBits(s, Bits(s) | Mask(a2)); return 1L; });
            Method("disable_detection_types", a2 => { var s = S(); if (s != null) SetBits(s, Bits(s) & ~Mask(a2)); return 1L; });
            Method("set_tag_family", _ => 1L);
            Method("set_color", a2 => {
                var s = S();
                var c = ProsColor.From(a2.Need(0, "color"));
                if (s != null && c.HasValue) s.SetColor(c.Value);
                return 1L;
            });
            Method("get_color", a2 => {
                var s = S();
                var id = (int)a2.Number(0, "id", 1);
                var c = s != null && id >= 1 && id <= 7 ? s.Colors[id - 1] : null;
                return new ProsColor(c ?? new AiColor { id = id });
            });
            Method("set_code", a2 => {
                var s = S();
                var c = ProsCode.From(a2.Need(0, "code"));
                if (s != null && c != null) {
                    s.codes.RemoveAll(k => k.SequenceEqual(c));
                    s.codes.Add(c);
                }
                return 1L;
            });
            Method("get_code", a2 => {
                var s = S();
                var id = (int)a2.Number(0, "id", 1);
                var c = s != null && id >= 1 && id <= s.codes.Count ? s.codes[id - 1] : new int[0];
                return new ProsCode(id, c);
            });
            Method("start_awb", _ => 1L);
            Method("set_usb_bounding_box_overlay", _ => 1L);
            Method("get_class_name", a2 => {
                var id = (int)a2.Number(0, "id", 0);
                if (id == -1) return (long)AiVisionSensor.ClassNames.Length;
                return id >= 0 && id < AiVisionSensor.ClassNames.Length ? AiVisionSensor.ClassNames[id] : (object)PyNone.Value;
            });
            Method("get_object_count", _ => (long)All().Count);
            Method("get_object", a2 => {
                var all = All();
                var i = (int)a2.Number(0, "object_index", 0);
                return new ProsAiObject(i >= 0 && i < all.Count ? all[i] : (AiObject?)null);
            });
            Method("get_all_objects", _ => new PyList(All().Select(o => (object)new ProsAiObject(o)).ToList()));
            Method("get_temperature", _ => 35.0);
            Method("get_port", _ => (long)port);
            Method("is_installed", _ => V5Brain.Get<AiVisionSensor>(port) != null);
        }

        public static void Register(RobotProgram program, PyModule m) {
            var cls = new PyClass("AIVision", null) { construct = (_, a) => new ProsAiVision(program, a) };
            cls.dict["Color"] = new PyClass("Color", null) { construct = (_, a) => new ProsColor(a) };
            cls.dict["Code"] = new PyClass("Code", null) { construct = (_, a) => new ProsCode(a) };
            cls.dict["is_type"] = new PyBuiltin("is_type", a => a.Need(0, "object") is ProsAiObject o && (o.Type & (int)Ops.ToLong(a.Need(1, "type") is PyEnum e ? (object)(long)e.value : a.Need(1, "type"))) != 0);
            m.globals["AIVision"] = cls;
            m.globals["AiVision"] = cls;
            m.globals["aivision_color_s_t"] = cls.dict["Color"];
            m.globals["aivision_code_s_t"] = cls.dict["Code"];
            var detect = new PyClass("AivisionDetectType", null);
            detect.dict["color"] = 1L; detect.dict["code"] = 2L; detect.dict["object"] = 4L; detect.dict["tag"] = 8L;
            m.globals["AivisionDetectType"] = detect;
            var mode = new PyClass("AivisionModeType", null);
            mode.dict["tags"] = 1L; mode.dict["colors"] = 2L; mode.dict["objects"] = 4L; mode.dict["color_merge"] = 16L; mode.dict["all"] = 7L;
            m.globals["AivisionModeType"] = mode;
            var family = new PyClass("AivisionTagFamily", null);
            family.dict["tag_21H7"] = 0L; family.dict["tag_16H5"] = 1L; family.dict["tag_25H9"] = 2L; family.dict["tag_61H11"] = 3L;
            m.globals["AivisionTagFamily"] = family;
            foreach (var (n, v) in new[] { ("E_AIVISION_DETECTED_COLOR", 1L), ("E_AIVISION_DETECTED_CODE", 2L), ("E_AIVISION_DETECTED_OBJECT", 4L), ("E_AIVISION_DETECTED_TAG", 8L),
                         ("E_AIVISION_MODE_TAGS", 1L), ("E_AIVISION_MODE_COLORS", 2L), ("E_AIVISION_MODE_OBJECTS", 4L), ("E_AIVISION_MODE_COLOR_MERGE", 16L), ("E_AIVISION_MODE_ALL", 7L),
                         ("AIVISION_MAX_OBJECT_COUNT", 24L), ("AIVISION_MAX_CLASSNAME_COUNT", 20L) })
                m.globals[n] = v;
        }

        /// <summary>aivision_color_s_t: id, red, green, blue, hue_range, saturation_range.</summary>
        internal sealed class ProsColor : VexObject {
            public override string TypeName => "aivision_color_s_t";
            public ProsColor(Args a) : this(new AiColor {
                id = (int)a.Number(0, "id", 1), r = (int)a.Number(1, "red", 0), g = (int)a.Number(2, "green", 0), b = (int)a.Number(3, "blue", 0),
                hue = (float)a.Number(4, "hue_range", 10), saturation = (float)a.Number(5, "saturation_range", 0.2) }) { }

            public ProsColor(AiColor c) {
                Field("id", (long)c.id);
                Field("red", (long)c.r);
                Field("green", (long)c.g);
                Field("blue", (long)c.b);
                Field("hue_range", (double)c.hue);
                Field("saturation_range", (double)c.saturation);
            }

            public static AiColor? From(object o) {
                double N(VexObject v, string name) => v.TryGetAttr(name, out var x) ? Ops.ToDouble(x) : 0;
                AiColor c;
                if (o is VexObject v) c = new AiColor { id = (int)N(v, "id"), r = (int)N(v, "red"), g = (int)N(v, "green"), b = (int)N(v, "blue"), hue = (float)N(v, "hue_range"), saturation = (float)N(v, "saturation_range") };
                else if (o is PyList l && l.items.Count >= 6) c = new AiColor { id = (int)Ops.ToDouble(l.items[0]), r = (int)Ops.ToDouble(l.items[1]), g = (int)Ops.ToDouble(l.items[2]), b = (int)Ops.ToDouble(l.items[3]), hue = (float)Ops.ToDouble(l.items[4]), saturation = (float)Ops.ToDouble(l.items[5]) };
                else return null;
                c.name = "COLOR_" + c.id;
                return c.Valid ? c : (AiColor?)null;
            }
        }

        /// <summary>aivision_code_s_t: id, length, c1..c5.</summary>
        internal sealed class ProsCode : VexObject {
            public override string TypeName => "aivision_code_s_t";
            public ProsCode(Args a) : this((int)a.Number(0, "id", 1), Enumerable.Range(0, (int)a.Number(1, "length", 0)).Select(i => (int)a.Number(2 + i, "c" + (i + 1), 0)).ToArray()) { }

            public ProsCode(int id, int[] colors) {
                Field("id", (long)id);
                Field("length", (long)colors.Length);
                for (var i = 0; i < 5; i++) Field("c" + (i + 1), i < colors.Length ? (long)colors[i] : 0L);
            }

            public static int[] From(object o) {
                if (o is VexObject v && v.TryGetAttr("length", out var len)) {
                    var n = Mathf.Clamp((int)Ops.ToLong(len), 0, 5);
                    var ids = new int[n];
                    for (var i = 0; i < n; i++) ids[i] = v.TryGetAttr("c" + (i + 1), out var c) ? (int)Ops.ToLong(c) : 0;
                    return n >= 2 ? ids : null;
                }
                if (o is PyList l && l.items.Count >= 4) {
                    var n = Mathf.Clamp((int)Ops.ToLong(l.items[1]), 0, 5);
                    return n >= 2 ? l.items.Skip(2).Take(n).Select(x => (int)Ops.ToLong(x)).ToArray() : null;
                }
                return null;
            }
        }

        /// <summary>aivision_object_s_t: id, type (AivisionDetectType) and object.color / .tag / .element.</summary>
        internal sealed class ProsAiObject : VexObject {
            public override string TypeName => "aivision_object_s_t";
            public readonly int Type;

            public ProsAiObject(AiObject? found) {
                var o = found ?? default;
                Type = found.HasValue ? (int)o.kind : 0;
                Field("id", (long)o.id);
                Field("type", (long)Type);
                var color = new Struct("aivision_object_color_s_t", ("xoffset", (long)o.x), ("yoffset", (long)o.y), ("width", (long)o.width), ("height", (long)o.height), ("angle", (long)Mathf.RoundToInt(o.angle * 10f)));
                var element = new Struct("aivision_object_element_s_t", ("xoffset", (long)o.x), ("yoffset", (long)o.y), ("width", (long)o.width), ("height", (long)o.height), ("score", (long)o.score));
                var tag = new Struct("aivision_object_tag_s_t", ("x0", (long)o.x), ("y0", (long)o.y), ("x1", (long)(o.x + o.width)), ("y1", (long)o.y),
                    ("x2", (long)(o.x + o.width)), ("y2", (long)(o.y + o.height)), ("x3", (long)o.x), ("y3", (long)(o.y + o.height)));
                Field("object", new Struct("object", ("color", color), ("element", element), ("tag", tag)));
            }
        }

        private sealed class Struct : VexObject {
            private readonly string type;
            public override string TypeName => type;
            public Struct(string type, params (string name, object value)[] fields) {
                this.type = type;
                foreach (var (n, v) in fields) Field(n, v);
            }
        }
    }
}
