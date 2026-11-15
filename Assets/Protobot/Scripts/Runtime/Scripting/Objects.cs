using System;
using System.Collections.Generic;
using System.Linq;

namespace Protobot.Scripting {
    // V2 (Phase 9, robot code): the values a program works with. Plain C# values stand for Python's: long (int),
    // double (float), bool, string (str), None (PyNone.Value); containers and functions are the classes below.

    public sealed class PyNone {
        public static readonly PyNone Value = new();
        private PyNone() { }
        public override string ToString() => "None";
    }

    public sealed class PyList {
        public readonly List<object> items;
        public PyList() => items = new List<object>();
        public PyList(IEnumerable<object> from) => items = new List<object>(from);
    }

    public sealed class PyTuple {
        public static readonly PyTuple Empty = new(Array.Empty<object>());
        public readonly object[] items;
        public PyTuple(object[] items) => this.items = items;
    }

    public sealed class PyDict {
        public readonly Dictionary<object, object> items = new(PyEquality.Instance);
        // Python keeps insertion order: Dictionary does too until something is removed; keep our own order list.
        public readonly List<object> order = new();

        public void Set(object key, object value) {
            if (!items.ContainsKey(key)) order.Add(key);
            items[key] = value;
        }

        public bool Remove(object key) {
            if (!items.Remove(key)) return false;
            var cmp = PyEquality.Instance;
            for (var i = 0; i < order.Count; i++)
                if (cmp.Equals(order[i], key)) {
                    order.RemoveAt(i);
                    break;
                }
            return true;
        }

        public IEnumerable<object> Keys => order;
    }

    public sealed class PySlice {
        public readonly object start, stop, step;
        public PySlice(object start, object stop, object step) {
            this.start = start;
            this.stop = stop;
            this.step = step;
        }
    }

    /// <summary>A range(...) object (iterated lazily).</summary>
    public sealed class PyRange {
        public readonly long start, stop, step;
        public PyRange(long start, long stop, long step) {
            this.start = start;
            this.stop = stop;
            this.step = step;
        }
        public long Count => step > 0 ? Math.Max(0, (stop - start + step - 1) / step) : Math.Max(0, (start - stop - step - 1) / -step);
    }

    /// <summary>What a built-in function gets: its positional and keyword arguments.</summary>
    public sealed class Args {
        public readonly object[] pos;
        public readonly Dictionary<string, object> kw;
        public readonly Interpreter vm;
        public readonly string name;

        public Args(Interpreter vm, string name, object[] pos, Dictionary<string, object> kw) {
            this.vm = vm;
            this.name = name;
            this.pos = pos;
            this.kw = kw;
        }

        public int Count => pos.Length;

        /// <summary>The argument at this place or with this keyword, else the default.</summary>
        public object Get(int index, string keyword, object fallback = null) {
            if (index >= 0 && index < pos.Length) return pos[index];
            if (keyword != null && kw != null && kw.TryGetValue(keyword, out var v)) return v;
            return fallback;
        }

        public bool Has(int index, string keyword) =>
            index >= 0 && index < pos.Length || keyword != null && kw != null && kw.ContainsKey(keyword);

        public object Need(int index, string keyword) {
            if (Has(index, keyword)) return Get(index, keyword);
            throw new PyError("TypeError", name + "() is missing its argument '" + (keyword ?? "#" + (index + 1)) + "'");
        }

        public double Number(int index, string keyword, double fallback) {
            var v = Get(index, keyword);
            return v == null || v is PyNone ? fallback : Ops.ToDouble(v, name);
        }

        public bool Bool(int index, string keyword, bool fallback) {
            var v = Get(index, keyword);
            return v == null || v is PyNone ? fallback : Ops.Truthy(v);
        }

        public void Max(int count) {
            if (pos.Length > count)
                throw new PyError("TypeError", name + "() takes at most " + count + " argument" + (count == 1 ? "" : "s") + " (" + pos.Length + " given)");
        }
    }

    /// <summary>A function written in C#. It returns a value, or a <see cref="Suspend"/> to make the program wait.</summary>
    public sealed class PyBuiltin {
        public readonly string name;
        public readonly Func<Args, object> fn;
        public PyBuiltin(string name, Func<Args, object> fn) {
            this.name = name;
            this.fn = fn;
        }
    }

    /// <summary>A built-in returns this to pause the thread that called it until <see cref="done"/> says so (or the
    /// time comes); the call's value is <see cref="result"/>.</summary>
    public sealed class Suspend {
        public readonly double until = -1;
        public readonly Func<bool> done;
        public readonly object result;
        public Suspend(double until, object result = null) {
            this.until = until;
            this.result = result;
        }
        public Suspend(Func<bool> done, object result = null) {
            this.done = done;
            this.result = result;
        }
    }

    public sealed class PyFunction {
        public readonly CodeObject code;
        public readonly object[] defaults;     // for the last parameters
        public readonly Scope closure;         // the enclosing function's scope (null at module level)
        public readonly PyModule module;
        public PyFunction(CodeObject code, object[] defaults, Scope closure, PyModule module) {
            this.code = code;
            this.defaults = defaults;
            this.closure = closure;
            this.module = module;
        }
        public string Name => code.name;
    }

    public sealed class PyBoundMethod {
        public readonly object self;
        public readonly object function;
        public PyBoundMethod(object self, object function) {
            this.self = self;
            this.function = function;
        }
    }

    /// <summary>A function's local variables (slots); nested functions keep their parent's alive (closures).</summary>
    public sealed class Scope {
        public readonly object[] slots;
        public readonly Scope parent;
        public Scope(int count, Scope parent) {
            slots = new object[count];
            this.parent = parent;
        }
    }

    public sealed class PyModule {
        public readonly string name;
        public readonly Dictionary<string, object> globals = new();
        public PyModule(string name) => this.name = name;
    }

    /// <summary>A class: written in Python (methods in <see cref="dict"/>), or built in (exceptions, the vex types).</summary>
    public class PyClass {
        public readonly string name;
        public readonly PyClass[] bases;
        public readonly Dictionary<string, object> dict = new();
        /// <summary>Built-in classes make their own instances ((the class called, args) -> object); null: a plain
        /// instance + __init__. Subclasses use their nearest base's.</summary>
        public Func<PyClass, Args, object> construct;
        /// <summary>A built-in type name isinstance(x, int) checks by (int, float, str...).</summary>
        public string builtinType;
        public readonly List<PyClass> mro = new();

        public PyClass(string name, PyClass[] bases) {
            this.name = name;
            this.bases = bases ?? Array.Empty<PyClass>();
            mro.Add(this);
            foreach (var b in this.bases)
                foreach (var c in b.mro)
                    if (!mro.Contains(c)) mro.Add(c);
        }

        public bool Lookup(string attr, out object value) {
            foreach (var c in mro)
                if (c.dict.TryGetValue(attr, out value)) return true;
            value = null;
            return false;
        }

        public bool IsSubclassOf(PyClass other) => mro.Contains(other);
    }

    public sealed class PyInstance {
        public readonly PyClass cls;
        public readonly Dictionary<string, object> dict = new();
        public PyInstance(PyClass cls) => this.cls = cls;
    }

    /// <summary>A C# object a program can use: its attributes come from <see cref="GetAttr"/> (built-in types: vex devices).</summary>
    public interface IPyObject {
        string TypeName { get; }
        bool TryGetAttr(string name, out object value);
        bool TrySetAttr(string name, object value);
    }

    /// <summary>A named constant like FORWARD or DEGREES (VEXcode's enum values).</summary>
    public sealed class PyEnum {
        public readonly string type, name;
        public readonly int value;
        public PyEnum(string type, string name, int value) {
            this.type = type;
            this.name = name;
            this.value = value;
        }
    }

    /// <summary>A code object: a function's (or the module's, or a class body's) instructions.</summary>
    public sealed class CodeObject {
        public string name = "<module>";
        public readonly List<Instr> code = new();
        public readonly List<object> consts = new();
        public readonly List<string> names = new();
        public string[] parameters = Array.Empty<string>();   // slots 0..n-1
        public int slotCount;
        public string[] slotNames = Array.Empty<string>();
        public bool hasStar, hasStarStar;                     // *args (slot = positional count), **kwargs (next)
        public int positionalCount;
        public bool isClassBody, isModule;

        public int Const(object value) {
            for (var i = 0; i < consts.Count; i++)
                if (consts[i] != null && consts[i].GetType() == value.GetType() && Equals(consts[i], value)) return i;
            consts.Add(value);
            return consts.Count - 1;
        }

        public int Name(string n) {
            var i = names.IndexOf(n);
            if (i >= 0) return i;
            names.Add(n);
            return names.Count - 1;
        }
    }

    public struct Instr {
        public Op op;
        public int arg;
        public int line;
        public override string ToString() => op + " " + arg + " (line " + line + ")";
    }

    public enum Op : byte {
        Nop, LoadConst, LoadNone, LoadTrue, LoadFalse,
        LoadFast, StoreFast, DeleteFast, LoadDeref, StoreDeref, LoadGlobal, StoreGlobal, DeleteGlobal, LoadName, StoreName,
        LoadAttr, StoreAttr, LoadIndex, StoreIndex, DeleteIndex, BuildSlice,
        Binary, InPlace, Unary, Compare,
        Jump, JumpIfFalse, JumpIfTrue, JumpIfFalseOrPop, JumpIfTrueOrPop,
        GetIter, ForIter,
        Call, CallKw, CallEx,
        Return, Pop, Dup, Dup2, Rot2, Rot3,
        BuildList, BuildTuple, BuildDict, BuildString, ListAppend, DictSet, ListExtend,
        Unpack, FormatValue,
        MakeFunction, MakeClass,
        Import, ImportFrom, ImportStar,
        SetupExcept, PopBlock, Raise, Reraise, MatchExcept, PopExcept, StoreExcept,
        Assert,
    }

    /// <summary>Python equality and hashing for dictionary keys (1 == 1.0 == True, tuples by items).</summary>
    public sealed class PyEquality : IEqualityComparer<object> {
        public static readonly PyEquality Instance = new();
        public new bool Equals(object a, object b) => Ops.Eq(a, b);
        public int GetHashCode(object o) {
            switch (o) {
                case null: return 0;
                case bool b: return b ? 1 : 0;
                case long l: return l.GetHashCode();
                case double d: return d == Math.Floor(d) && Math.Abs(d) < 9e18 ? ((long)d).GetHashCode() : d.GetHashCode();
                case string s: return s.GetHashCode();
                case PyTuple t: {
                    var h = 17;
                    foreach (var i in t.items) h = h * 31 + GetHashCode(i);
                    return h;
                }
                case PyList or PyDict: throw new PyError("TypeError", "unhashable type: '" + Ops.TypeName(o) + "'");
                default: return o.GetHashCode();
            }
        }
    }

    /// <summary>A Python exception raised in C# (a type name and a message), or carrying a raised instance.</summary>
    public sealed class PyError : Exception {
        public readonly string type;
        public PyInstance instance;   // when raised from Python (or once caught)
        public int line;
        public readonly List<string> trace = new();

        public PyError(string type, string message) : base(message) => this.type = type;

        public PyError(PyInstance instance) : base(Ops.ExceptionMessage(instance)) {
            this.instance = instance;
            type = instance.cls.name;
        }

        public string Describe => type + (string.IsNullOrEmpty(Message) ? "" : ": " + Message);
    }

    /// <summary>A mistake in how the program is written (found before it runs).</summary>
    public sealed class SyntaxErr : Exception {
        public readonly int line, column;
        public SyntaxErr(string message, int line, int column) : base(message) {
            this.line = line;
            this.column = column;
        }
    }
}
