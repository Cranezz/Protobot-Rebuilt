using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Protobot.Scripting {
    public sealed class PySuper {
        public readonly PyInstance self;
        public readonly PyClass after;
        public PySuper(PyInstance self, PyClass after) {
            this.self = self;
            this.after = after;
        }
    }

    /// <summary>V2 (robot code): Python's built-in functions, types, exceptions, methods and the math/random/time modules.</summary>
    public static class Builtins {
        private static void Def(Interpreter vm, string name, Func<Args, object> fn) => vm.builtins[name] = new PyBuiltin(name, fn);

        public static PyClass Type(Interpreter vm, string name) => (PyClass)vm.builtins[name];

        public static void Install(Interpreter vm) {
            // ---------------------------------------------------------- exceptions
            var baseExc = new PyClass("BaseException", null) { builtinType = "BaseException" };
            vm.builtins["BaseException"] = baseExc;
            PyClass Exc(string name, PyClass parent) {
                var c = new PyClass(name, new[] { parent });
                vm.builtins[name] = c;
                return c;
            }
            var exc = Exc("Exception", baseExc);
            Exc("KeyboardInterrupt", baseExc);
            Exc("SystemExit", baseExc);
            var arith = Exc("ArithmeticError", exc);
            Exc("ZeroDivisionError", arith);
            Exc("OverflowError", arith);
            var lookup = Exc("LookupError", exc);
            Exc("IndexError", lookup);
            Exc("KeyError", lookup);
            var name = Exc("NameError", exc);
            Exc("UnboundLocalError", name);
            Exc("TypeError", exc);
            Exc("ValueError", exc);
            Exc("AttributeError", exc);
            var runtime = Exc("RuntimeError", exc);
            Exc("RecursionError", runtime);
            Exc("NotImplementedError", runtime);
            Exc("AssertionError", exc);
            Exc("StopIteration", exc);
            Exc("ImportError", exc);
            Exc("MemoryError", exc);
            Exc("OSError", exc);

            // ---------------------------------------------------------- types (callable, and for isinstance)
            PyClass BuiltinType(string typeName, Func<Args, object> make) {
                var c = new PyClass(typeName, null) { builtinType = typeName, construct = (_, a) => make(a) };
                vm.builtins[typeName] = c;
                return c;
            }
            BuiltinType("int", a => {
                if (a.Count == 0) return 0L;
                var v = a.pos[0];
                if (a.Count > 1) return ParseInt(Ops.Str(v), (int)Ops.ToLong(a.pos[1]));
                return v switch {
                    long l => l,
                    bool b => b ? 1L : 0L,
                    double d => double.IsNaN(d) || double.IsInfinity(d) ? throw new PyError("ValueError", "can't make an int from " + Ops.FloatRepr(d)) : (long)Math.Truncate(d),
                    string s => ParseInt(s, 10),
                    PyEnum e => (long)e.value,
                    _ => throw new PyError("TypeError", "int() can't turn " + Ops.Describe(v) + " into a number"),
                };
            });
            BuiltinType("float", a => {
                if (a.Count == 0) return 0.0;
                var v = a.pos[0];
                if (v is string s) {
                    var t = s.Trim().ToLowerInvariant().Replace("_", "");
                    if (t is "inf" or "+inf" or "infinity") return double.PositiveInfinity;
                    if (t is "-inf" or "-infinity") return double.NegativeInfinity;
                    if (t == "nan") return double.NaN;
                    if (double.TryParse(t, System.Globalization.NumberStyles.Float, Ops.Inv, out var d)) return d;
                    throw new PyError("ValueError", "float() can't read " + Ops.StrRepr(s) + " as a number");
                }
                return Ops.ToDouble(v, "float()");
            });
            BuiltinType("str", a => a.Count == 0 ? "" : a.vm.StrOf(a.pos[0]));
            BuiltinType("bool", a => a.Count != 0 && a.vm.Truthy(a.pos[0]));
            BuiltinType("list", a => a.Count == 0 ? new PyList() : new PyList(Ops.Iterate(a.pos[0])));
            BuiltinType("tuple", a => a.Count == 0 ? PyTuple.Empty : new PyTuple(Ops.Iterate(a.pos[0]).ToArray()));
            BuiltinType("set", a => {
                var s = new PySet();
                if (a.Count > 0)
                    foreach (var o in Ops.Iterate(a.pos[0])) s.Add(o);
                return s;
            });
            BuiltinType("dict", a => {
                var d = new PyDict();
                if (a.Count > 0) {
                    if (a.pos[0] is PyDict src)
                        foreach (var k in src.Keys) d.Set(k, src.items[k]);
                    else
                        foreach (var pair in Ops.Iterate(a.pos[0])) {
                            var kv = Ops.Iterate(pair).ToList();
                            if (kv.Count != 2) throw new PyError("ValueError", "dict() needs pairs (key, value)");
                            d.Set(kv[0], kv[1]);
                        }
                }
                if (a.kw != null)
                    foreach (var kv in a.kw) d.Set(kv.Key, kv.Value);
                return d;
            });
            BuiltinType("range", a => {
                a.Max(3);
                if (a.Count == 0) throw new PyError("TypeError", "range() needs at least one number");
                long start = 0, stop, step = 1;
                if (a.Count == 1) stop = Ops.ToLong(a.pos[0], "range()");
                else {
                    start = Ops.ToLong(a.pos[0], "range()");
                    stop = Ops.ToLong(a.pos[1], "range()");
                    if (a.Count > 2) step = Ops.ToLong(a.pos[2], "range()");
                }
                if (step == 0) throw new PyError("ValueError", "range() step can't be zero");
                return new PyRange(start, stop, step);
            });
            var objectClass = new PyClass("object", null);
            vm.builtins["object"] = objectClass;

            // ---------------------------------------------------------- functions
            Def(vm, "print", a => {
                var sep = a.kw != null && a.kw.TryGetValue("sep", out var s) && !(s is PyNone) ? Ops.Str(s) : " ";
                var end = a.kw != null && a.kw.TryGetValue("end", out var e) && !(e is PyNone) ? Ops.Str(e) : "\n";
                a.vm.Output(string.Join(sep, a.pos.Select(a.vm.StrOf)) + end);
                return PyNone.Value;
            });
            Def(vm, "len", a => {
                a.Max(1);
                var o = a.Need(0, null);
                if (o is PyInstance pi && pi.cls.Lookup("__len__", out var m)) return a.vm.CallSync(new PyBoundMethod(pi, m));
                return Ops.Len(o);
            });
            Def(vm, "abs", a => a.Need(0, null) switch {
                long l => (object)Math.Abs(l),
                double d => Math.Abs(d),
                bool b => b ? 1L : 0L,
                var o => throw new PyError("TypeError", "abs() needs a number, not " + Ops.Describe(o)),
            });
            Def(vm, "round", a => {
                var x = a.Need(0, "number");
                var nd = a.Get(1, "ndigits");
                if (nd == null || nd is PyNone) {
                    if (x is long) return x;
                    var d = Ops.ToDouble(x, "round()");
                    if (double.IsNaN(d) || double.IsInfinity(d)) throw new PyError("ValueError", "can't round " + Ops.FloatRepr(d));
                    return (long)Math.Round(d, MidpointRounding.ToEven);
                }
                var n = (int)Ops.ToLong(nd, "round()");
                if (x is long xl) return n >= 0 ? xl : (long)(Math.Round(xl / Math.Pow(10, -n), MidpointRounding.ToEven) * Math.Pow(10, -n));
                var v = Ops.ToDouble(x, "round()");
                if (n >= 0 && n <= 15) return Math.Round(v, n, MidpointRounding.ToEven);
                var p = Math.Pow(10, n);
                return Math.Round(v * p, MidpointRounding.ToEven) / p;
            });
            Def(vm, "min", a => MinMax(a, -1));
            Def(vm, "max", a => MinMax(a, 1));
            Def(vm, "sum", a => {
                object total = a.Get(1, "start", 0L);
                foreach (var o in Ops.Iterate(a.Need(0, null))) total = a.vm.BinaryOp("+", total, o);
                return total;
            });
            Def(vm, "any", a => Ops.Iterate(a.Need(0, null)).Any(a.vm.Truthy));
            Def(vm, "all", a => Ops.Iterate(a.Need(0, null)).All(a.vm.Truthy));
            Def(vm, "sorted", a => {
                var l = new PyList(Ops.Iterate(a.Need(0, null)));
                Sort(a.vm, l, a.Get(-1, "key"), a.Bool(-1, "reverse", false));
                return l;
            });
            Def(vm, "reversed", a => {
                var items = Ops.Iterate(a.Need(0, null)).ToList();
                items.Reverse();
                return new PyIterator(items.GetEnumerator());
            });
            Def(vm, "enumerate", a => {
                var start = a.Count > 1 || a.Has(-1, "start") ? Ops.ToLong(a.Get(1, "start")) : 0L;
                return new PyIterator(Enumerate(Ops.Iterate(a.Need(0, null)), start).GetEnumerator());
            });
            Def(vm, "zip", a => new PyIterator(Zip(a.pos.Select(Ops.Iterate).ToArray()).GetEnumerator()));
            Def(vm, "map", a => {
                var f = a.Need(0, null);
                var lists = a.pos.Skip(1).Select(o => Ops.Iterate(o).ToList()).ToList();
                if (lists.Count == 0) throw new PyError("TypeError", "map() needs a function and a list");
                var n = lists.Min(l => l.Count);
                var r = new List<object>();
                for (var i = 0; i < n; i++) r.Add(a.vm.CallSync(f, lists.Select(l => l[i]).ToArray()));
                return new PyIterator(r.GetEnumerator());
            });
            Def(vm, "filter", a => {
                var f = a.Need(0, null);
                var r = Ops.Iterate(a.Need(1, null)).Where(o => f is PyNone ? a.vm.Truthy(o) : a.vm.Truthy(a.vm.CallSync(f, o))).ToList();
                return new PyIterator(r.GetEnumerator());
            });
            Def(vm, "isinstance", a => IsInstance(a.Need(0, null), a.Need(1, null)));
            Def(vm, "issubclass", a => a.Need(0, null) is PyClass c && a.Need(1, null) is PyClass p && c.IsSubclassOf(p));
            Def(vm, "type", a => TypeOf(a.vm, a.Need(0, null)));
            Def(vm, "repr", a => a.vm.ReprOf(a.Need(0, null)));
            Def(vm, "format", a => Ops.Format(a.Need(0, null), a.Count > 1 ? Ops.Str(a.pos[1]) : ""));
            Def(vm, "chr", a => ((char)Ops.ToLong(a.Need(0, null))).ToString());
            Def(vm, "ord", a => a.Need(0, null) is string s && s.Length == 1 ? (long)s[0] : throw new PyError("TypeError", "ord() needs one character"));
            Def(vm, "hex", a => (Ops.ToLong(a.Need(0, null)) < 0 ? "-0x" : "0x") + Convert.ToString(Math.Abs(Ops.ToLong(a.pos[0])), 16));
            Def(vm, "bin", a => (Ops.ToLong(a.Need(0, null)) < 0 ? "-0b" : "0b") + Convert.ToString(Math.Abs(Ops.ToLong(a.pos[0])), 2));
            Def(vm, "divmod", a => {
                var x = a.Need(0, null);
                var y = a.Need(1, null);
                return new PyTuple(new[] { Ops.Binary("//", x, y), Ops.Binary("%", x, y) });
            });
            Def(vm, "pow", a => Ops.Binary("**", a.Need(0, null), a.Need(1, null)));
            Def(vm, "hasattr", a => {
                try {
                    a.vm.GetAttr(a.Need(0, null), Ops.Str(a.Need(1, null)));
                    return true;
                }
                catch (PyError e) when (e.type == "AttributeError") {
                    return false;
                }
            });
            Def(vm, "getattr", a => {
                try {
                    return a.vm.GetAttr(a.Need(0, null), Ops.Str(a.Need(1, null)));
                }
                catch (PyError e) when (e.type == "AttributeError" && a.Count > 2) {
                    return a.pos[2];
                }
            });
            Def(vm, "setattr", a => {
                a.vm.SetAttr(a.Need(0, null), Ops.Str(a.Need(1, null)), a.Need(2, null));
                return PyNone.Value;
            });
            Def(vm, "callable", a => a.Need(0, null) is PyFunction or PyBuiltin or PyBoundMethod or PyClass);
            Def(vm, "id", a => (long)System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(a.Need(0, null)));
            Def(vm, "hash", a => (long)PyEquality.Instance.GetHashCode(a.Need(0, null)));
            Def(vm, "iter", a => Ops.GetIter(a.Need(0, null)));
            Def(vm, "next", a => {
                var it = a.Need(0, null) as PyIterator ?? throw new PyError("TypeError", "next() needs an iterator (use iter(...))");
                if (it.e.MoveNext()) return it.e.Current;
                if (a.Count > 1) return a.pos[1];
                throw new PyError("StopIteration", "");
            });
            Def(vm, "super", a => {
                var frame = a.vm.CurrentFrame;
                if (frame == null || frame.code.parameters.Length == 0 || !(frame.scope.slots[0] is PyInstance self))
                    throw new PyError("RuntimeError", "super() only works inside a method");
                var code = frame.code;
                var owner = self.cls.mro.FirstOrDefault(c => c.dict.Values.Any(v => v is PyFunction f && f.code == code)) ?? self.cls;
                return new PySuper(self, owner);
            });
            Def(vm, "input", _ => throw new PyError("OSError", "a robot has no keyboard: input() can't be used (read the controller instead)"));
            Def(vm, "open", _ => throw new PyError("OSError", "robot code can't open files"));
            Def(vm, "exit", _ => {
                vm.StopAll();
                return PyNone.Value;
            });
            vm.builtins["quit"] = vm.builtins["exit"];

            // ---------------------------------------------------------- modules
            var math = new PyModule("math");
            void M(string n, Func<double, double> f) => math.globals[n] = new PyBuiltin(n, a => f(a.Number(0, null, double.NaN)));
            math.globals["pi"] = Math.PI;
            math.globals["e"] = Math.E;
            math.globals["tau"] = Math.PI * 2;
            math.globals["inf"] = double.PositiveInfinity;
            math.globals["nan"] = double.NaN;
            M("sqrt", x => x < 0 ? throw new PyError("ValueError", "math.sqrt() of a negative number") : Math.Sqrt(x));
            M("sin", Math.Sin);
            M("cos", Math.Cos);
            M("tan", Math.Tan);
            M("asin", Math.Asin);
            M("acos", Math.Acos);
            M("atan", Math.Atan);
            M("sinh", Math.Sinh);
            M("cosh", Math.Cosh);
            M("tanh", Math.Tanh);
            M("exp", Math.Exp);
            M("fabs", Math.Abs);
            M("log10", Math.Log10);
            M("log2", x => Math.Log(x, 2));
            M("degrees", x => x * 180 / Math.PI);
            M("radians", x => x * Math.PI / 180);
            math.globals["floor"] = new PyBuiltin("floor", a => a.Need(0, null) is long l ? l : (long)Math.Floor(Ops.ToDouble(a.pos[0])));
            math.globals["ceil"] = new PyBuiltin("ceil", a => a.Need(0, null) is long l ? l : (long)Math.Ceiling(Ops.ToDouble(a.pos[0])));
            math.globals["trunc"] = new PyBuiltin("trunc", a => (long)Math.Truncate(Ops.ToDouble(a.Need(0, null))));
            math.globals["atan2"] = new PyBuiltin("atan2", a => Math.Atan2(Ops.ToDouble(a.Need(0, null)), Ops.ToDouble(a.Need(1, null))));
            math.globals["hypot"] = new PyBuiltin("hypot", a => Math.Sqrt(a.pos.Sum(o => Ops.ToDouble(o) * Ops.ToDouble(o))));
            math.globals["pow"] = new PyBuiltin("pow", a => Math.Pow(Ops.ToDouble(a.Need(0, null)), Ops.ToDouble(a.Need(1, null))));
            math.globals["fmod"] = new PyBuiltin("fmod", a => Ops.ToDouble(a.Need(0, null)) % Ops.ToDouble(a.Need(1, null)));
            math.globals["copysign"] = new PyBuiltin("copysign", a => {
                var x = Math.Abs(Ops.ToDouble(a.Need(0, null)));
                return Ops.ToDouble(a.Need(1, null)) < 0 ? -x : x;
            });
            math.globals["log"] = new PyBuiltin("log", a => a.Count > 1 ? Math.Log(Ops.ToDouble(a.pos[0]), Ops.ToDouble(a.pos[1])) : Math.Log(Ops.ToDouble(a.Need(0, null))));
            math.globals["isnan"] = new PyBuiltin("isnan", a => double.IsNaN(Ops.ToDouble(a.Need(0, null))));
            math.globals["isinf"] = new PyBuiltin("isinf", a => double.IsInfinity(Ops.ToDouble(a.Need(0, null))));
            math.globals["isclose"] = new PyBuiltin("isclose", a => {
                var x = Ops.ToDouble(a.Need(0, null));
                var y = Ops.ToDouble(a.Need(1, null));
                var rel = a.Number(-1, "rel_tol", 1e-9);
                var abs = a.Number(-1, "abs_tol", 0);
                return Math.Abs(x - y) <= Math.Max(rel * Math.Max(Math.Abs(x), Math.Abs(y)), abs);
            });
            vm.modules["math"] = math;

            var random = new PyModule("random");
            var rng = new Random(1234);
            random.globals["seed"] = new PyBuiltin("seed", a => {
                rng = new Random(a.Count > 0 ? (int)Ops.ToLong(a.pos[0]) : Environment.TickCount);
                return PyNone.Value;
            });
            random.globals["random"] = new PyBuiltin("random", _ => rng.NextDouble());
            random.globals["uniform"] = new PyBuiltin("uniform", a => {
                var lo = Ops.ToDouble(a.Need(0, null));
                return lo + (Ops.ToDouble(a.Need(1, null)) - lo) * rng.NextDouble();
            });
            random.globals["randint"] = new PyBuiltin("randint", a => {
                var lo = Ops.ToLong(a.Need(0, null));
                var hi = Ops.ToLong(a.Need(1, null));
                if (hi < lo) throw new PyError("ValueError", "randint(a, b) needs a <= b");
                return lo + (long)(rng.NextDouble() * (hi - lo + 1));
            });
            random.globals["randrange"] = new PyBuiltin("randrange", a => {
                long lo = 0, hi;
                if (a.Count == 1) hi = Ops.ToLong(a.pos[0]);
                else {
                    lo = Ops.ToLong(a.Need(0, null));
                    hi = Ops.ToLong(a.Need(1, null));
                }
                if (hi <= lo) throw new PyError("ValueError", "randrange() has an empty range");
                return lo + (long)(rng.NextDouble() * (hi - lo));
            });
            random.globals["choice"] = new PyBuiltin("choice", a => {
                var items = Ops.Iterate(a.Need(0, null)).ToList();
                if (items.Count == 0) throw new PyError("IndexError", "choice() from an empty list");
                return items[rng.Next(items.Count)];
            });
            random.globals["shuffle"] = new PyBuiltin("shuffle", a => {
                var l = a.Need(0, null) as PyList ?? throw new PyError("TypeError", "shuffle() needs a list");
                for (var i = l.items.Count - 1; i > 0; i--) {
                    var j = rng.Next(i + 1);
                    (l.items[i], l.items[j]) = (l.items[j], l.items[i]);
                }
                return PyNone.Value;
            });
            vm.modules["random"] = random;

            var time = new PyModule("time");
            time.globals["time"] = new PyBuiltin("time", a => a.vm.Time);
            time.globals["monotonic"] = time.globals["time"];
            time.globals["perf_counter"] = time.globals["time"];
            time.globals["ticks_ms"] = new PyBuiltin("ticks_ms", a => (long)(a.vm.Time * 1000));
            time.globals["sleep"] = new PyBuiltin("sleep", a => new Suspend(a.vm.Time + Math.Max(0, a.Number(0, "seconds", 0))));
            time.globals["sleep_ms"] = new PyBuiltin("sleep_ms", a => new Suspend(a.vm.Time + Math.Max(0, a.Number(0, "ms", 0)) / 1000));
            vm.modules["time"] = time;
        }

        private static long ParseInt(string s, int radix) {
            var t = s.Trim().Replace("_", "");
            var negative = t.StartsWith("-");
            if (negative || t.StartsWith("+")) t = t.Substring(1);
            if (radix == 16 && t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) t = t.Substring(2);
            if (radix == 2 && t.StartsWith("0b", StringComparison.OrdinalIgnoreCase)) t = t.Substring(2);
            try {
                if (t.Length == 0) throw new FormatException();
                var v = radix == 10 ? long.Parse(t, System.Globalization.NumberStyles.None, Ops.Inv) : Convert.ToInt64(t, radix);
                return negative ? -v : v;
            }
            catch (Exception e) when (e is FormatException or OverflowException or ArgumentException) {
                throw new PyError("ValueError", "int() can't read " + Ops.StrRepr(s) + " as a whole number");
            }
        }

        private static object MinMax(Args a, int sign) {
            var items = a.Count == 1 ? Ops.Iterate(a.pos[0]).ToList() : a.pos.ToList();
            var key = a.Get(-1, "key");
            if (items.Count == 0) {
                if (a.kw != null && a.kw.TryGetValue("default", out var d)) return d;
                throw new PyError("ValueError", a.name + "() of an empty list");
            }
            var best = items[0];
            var bestKey = key == null || key is PyNone ? best : a.vm.CallSync(key, best);
            for (var i = 1; i < items.Count; i++) {
                var k = key == null || key is PyNone ? items[i] : a.vm.CallSync(key, items[i]);
                if (Ops.Truthy(a.vm.CompareOp(sign > 0 ? ">" : "<", k, bestKey))) {
                    best = items[i];
                    bestKey = k;
                }
            }
            return best;
        }

        public static void Sort(Interpreter vm, PyList l, object key, bool reverse) {
            var keyed = l.items.Select(o => (item: o, key: key == null || key is PyNone ? o : vm.CallSync(key, o))).ToList();
            // A stable sort (Python's is).
            var sorted = keyed.Select((k, i) => (k, i)).ToList();
            sorted.Sort((x, y) => {
                var c = Ops.Truthy(vm.CompareOp("<", x.k.key, y.k.key)) ? -1 : Ops.Truthy(vm.CompareOp("<", y.k.key, x.k.key)) ? 1 : 0;
                if (reverse) c = -c;
                return c != 0 ? c : x.i.CompareTo(y.i);
            });
            l.items.Clear();
            l.items.AddRange(sorted.Select(s => s.k.item));
        }

        private static IEnumerable<object> Enumerate(IEnumerable<object> items, long start) {
            foreach (var o in items) yield return new PyTuple(new object[] { start++, o });
        }

        private static IEnumerable<object> Zip(IEnumerable<object>[] lists) {
            if (lists.Length == 0) yield break;
            var es = lists.Select(l => l.GetEnumerator()).ToArray();
            while (es.All(e => e.MoveNext())) yield return new PyTuple(es.Select(e => e.Current).ToArray());
        }

        public static object TypeOf(Interpreter vm, object o) {
            if (o is PyInstance pi) return pi.cls;
            var n = Ops.TypeName(o);
            if (vm.builtins.TryGetValue(n, out var t) && t is PyClass c) return c;
            return new PyClass(n, null) { builtinType = n };
        }

        public static bool IsInstance(object o, object type) {
            switch (type) {
                case PyTuple t: return t.items.Any(x => IsInstance(o, x));
                case PyClass c:
                    if (o is PyInstance pi) return pi.cls.IsSubclassOf(c);
                    if (c.builtinType == null) return o is IPyObject p && p.TypeName == c.name;
                    var n = Ops.TypeName(o);
                    return n == c.builtinType || c.builtinType == "int" && n == "bool" || c.name == "object";
            }
            throw new PyError("TypeError", "isinstance() needs a class as its second argument, not " + Ops.Describe(type));
        }

        public static PyInstance MakeException(Interpreter vm, string type, string message) {
            var cls = vm.builtins.TryGetValue(type, out var c) && c is PyClass pc ? pc : (PyClass)vm.builtins["Exception"];
            var e = new PyInstance(cls);
            e.dict["args"] = string.IsNullOrEmpty(message) ? PyTuple.Empty : new PyTuple(new object[] { message });
            return e;
        }

        // ------------------------------------------------------------------ methods of built-in values

        private static readonly string[] StrMethods = { "upper", "lower", "strip", "lstrip", "rstrip", "split", "join", "replace", "startswith", "endswith", "find", "rfind", "index", "count", "format", "isdigit", "isnumeric", "isalpha", "isalnum", "isspace", "isupper", "islower", "title", "capitalize", "center", "ljust", "rjust", "zfill", "splitlines", "partition", "swapcase" };
        private static readonly string[] ListMethods = { "append", "extend", "insert", "pop", "remove", "index", "count", "sort", "reverse", "clear", "copy" };
        private static readonly string[] DictMethods = { "get", "keys", "values", "items", "pop", "update", "setdefault", "clear", "copy", "popitem" };
        private static readonly string[] SetMethods = { "add", "remove", "discard", "pop", "clear", "copy", "union", "intersection", "difference", "issubset", "issuperset", "update" };

        public static IEnumerable<string> MethodNames(object o) => o switch {
            string => StrMethods,
            PyList => ListMethods,
            PyDict => DictMethods,
            PySet => SetMethods,
            _ => Array.Empty<string>(),
        };

        public static PyBuiltin Method(Interpreter vm, object o, string name) {
            switch (o) {
                case string s: return StrMethod(vm, s, name);
                case PyList l: return ListMethod(vm, l, name);
                case PyDict d: return DictMethod(d, name);
                case PySet st: return SetMethod(st, name);
                case PyTuple t:
                    if (name == "index") return new PyBuiltin("index", a => {
                        var x = a.Need(0, null);
                        for (var i = 0; i < t.items.Length; i++)
                            if (Ops.Eq(t.items[i], x)) return (long)i;
                        throw new PyError("ValueError", Ops.Repr(x) + " isn't in the tuple");
                    });
                    if (name == "count") return new PyBuiltin("count", a => (long)t.items.Count(i => Ops.Eq(i, a.Need(0, null))));
                    break;
                case long l:
                    if (name == "bit_length") return new PyBuiltin(name, _ => (long)(l == 0 ? 0 : Convert.ToString(Math.Abs(l), 2).Length));
                    break;
                case double d:
                    if (name == "is_integer") return new PyBuiltin(name, _ => d == Math.Floor(d));
                    break;
            }
            return null;
        }

        private static PyBuiltin StrMethod(Interpreter vm, string s, string name) {
            Func<Args, object> f = name switch {
                "upper" => _ => s.ToUpperInvariant(),
                "lower" => _ => s.ToLowerInvariant(),
                "swapcase" => _ => new string(s.Select(c => char.IsUpper(c) ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c)).ToArray()),
                "strip" => a => a.Count > 0 && !(a.pos[0] is PyNone) ? s.Trim(Ops.Str(a.pos[0]).ToCharArray()) : s.Trim(),
                "lstrip" => a => a.Count > 0 && !(a.pos[0] is PyNone) ? s.TrimStart(Ops.Str(a.pos[0]).ToCharArray()) : s.TrimStart(),
                "rstrip" => a => a.Count > 0 && !(a.pos[0] is PyNone) ? s.TrimEnd(Ops.Str(a.pos[0]).ToCharArray()) : s.TrimEnd(),
                "split" => a => {
                    var sep = a.Get(0, "sep");
                    var max = (int)Ops.ToLong(a.Get(1, "maxsplit", -1L));
                    string[] parts;
                    if (sep == null || sep is PyNone) parts = max < 0 ? s.Split((char[])null, StringSplitOptions.RemoveEmptyEntries) : s.Split((char[])null, max + 1, StringSplitOptions.RemoveEmptyEntries);
                    else {
                        var ss = Ops.Str(sep);
                        if (ss.Length == 0) throw new PyError("ValueError", "split() can't split on an empty string");
                        parts = max < 0 ? s.Split(new[] { ss }, StringSplitOptions.None) : s.Split(new[] { ss }, max + 1, StringSplitOptions.None);
                    }
                    return new PyList(parts.Cast<object>());
                },
                "splitlines" => _ => new PyList(s.Split('\n').Select(x => (object)x.TrimEnd('\r'))),
                "join" => a => string.Join(s, Ops.Iterate(a.Need(0, null)).Select(o => o as string ?? throw new PyError("TypeError", "join() needs strings, but found " + Ops.Describe(o) + " (use str(...))"))),
                "replace" => a => {
                    var old = Ops.Str(a.Need(0, null));
                    var neu = Ops.Str(a.Need(1, null));
                    return old.Length == 0 ? s : s.Replace(old, neu);
                },
                "startswith" => a => a.Need(0, null) is PyTuple t ? t.items.Any(x => s.StartsWith(Ops.Str(x), StringComparison.Ordinal)) : s.StartsWith(Ops.Str(a.pos[0]), StringComparison.Ordinal),
                "endswith" => a => a.Need(0, null) is PyTuple t ? t.items.Any(x => s.EndsWith(Ops.Str(x), StringComparison.Ordinal)) : s.EndsWith(Ops.Str(a.pos[0]), StringComparison.Ordinal),
                "find" => a => (long)s.IndexOf(Ops.Str(a.Need(0, null)), StringComparison.Ordinal),
                "rfind" => a => (long)s.LastIndexOf(Ops.Str(a.Need(0, null)), StringComparison.Ordinal),
                "index" => a => {
                    var i = s.IndexOf(Ops.Str(a.Need(0, null)), StringComparison.Ordinal);
                    return i >= 0 ? (long)i : throw new PyError("ValueError", "substring not found");
                },
                "count" => a => {
                    var sub = Ops.Str(a.Need(0, null));
                    if (sub.Length == 0) return (long)s.Length + 1;
                    long n = 0;
                    for (var i = s.IndexOf(sub, StringComparison.Ordinal); i >= 0; i = s.IndexOf(sub, i + sub.Length, StringComparison.Ordinal)) n++;
                    return n;
                },
                "format" => a => Ops.StrFormat(s, a.pos.Select(o => o is PyInstance ? vm.StrOf(o) : o).ToArray(), a.kw),
                "isdigit" or "isnumeric" => _ => s.Length > 0 && s.All(char.IsDigit),
                "isalpha" => _ => s.Length > 0 && s.All(char.IsLetter),
                "isalnum" => _ => s.Length > 0 && s.All(char.IsLetterOrDigit),
                "isspace" => _ => s.Length > 0 && s.All(char.IsWhiteSpace),
                "isupper" => _ => s.Any(char.IsLetter) && !s.Any(char.IsLower),
                "islower" => _ => s.Any(char.IsLetter) && !s.Any(char.IsUpper),
                "title" => _ => System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLowerInvariant()),
                "capitalize" => _ => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1).ToLowerInvariant(),
                "center" => a => Ops.Format(s, "^" + Ops.ToLong(a.Need(0, null))),
                "ljust" => a => Ops.Format(s, "<" + Ops.ToLong(a.Need(0, null))),
                "rjust" => a => Ops.Format(s, ">" + Ops.ToLong(a.Need(0, null))),
                "zfill" => a => {
                    var w = (int)Ops.ToLong(a.Need(0, null));
                    var neg = s.StartsWith("-") || s.StartsWith("+");
                    var body = neg ? s.Substring(1) : s;
                    return (neg ? s.Substring(0, 1) : "") + body.PadLeft(Math.Max(0, w - (neg ? 1 : 0)), '0');
                },
                "partition" => a => {
                    var sep = Ops.Str(a.Need(0, null));
                    var i = s.IndexOf(sep, StringComparison.Ordinal);
                    return i < 0 ? new PyTuple(new object[] { s, "", "" }) : new PyTuple(new object[] { s.Substring(0, i), sep, s.Substring(i + sep.Length) });
                },
                _ => null,
            };
            return f == null ? null : new PyBuiltin(name, f);
        }

        private static PyBuiltin ListMethod(Interpreter vm, PyList l, string name) {
            Func<Args, object> f = name switch {
                "append" => a => {
                    a.Max(1);
                    l.items.Add(a.Need(0, null));
                    return PyNone.Value;
                },
                "extend" => a => {
                    l.items.AddRange(Ops.Iterate(a.Need(0, null)).ToList());
                    return PyNone.Value;
                },
                "insert" => a => {
                    var i = Ops.ToLong(a.Need(0, null));
                    if (i < 0) i += l.items.Count;
                    l.items.Insert((int)Math.Max(0, Math.Min(l.items.Count, i)), a.Need(1, null));
                    return PyNone.Value;
                },
                "pop" => a => {
                    if (l.items.Count == 0) throw new PyError("IndexError", "pop from an empty list");
                    var i = a.Count > 0 ? Ops.ToLong(a.pos[0]) : -1;
                    if (i < 0) i += l.items.Count;
                    if (i < 0 || i >= l.items.Count) throw new PyError("IndexError", "pop index out of range");
                    var v = l.items[(int)i];
                    l.items.RemoveAt((int)i);
                    return v;
                },
                "remove" => a => {
                    var x = a.Need(0, null);
                    for (var i = 0; i < l.items.Count; i++)
                        if (Ops.Eq(l.items[i], x)) {
                            l.items.RemoveAt(i);
                            return PyNone.Value;
                        }
                    throw new PyError("ValueError", Ops.Repr(x) + " isn't in the list");
                },
                "index" => a => {
                    var x = a.Need(0, null);
                    for (var i = 0; i < l.items.Count; i++)
                        if (Ops.Eq(l.items[i], x)) return (long)i;
                    throw new PyError("ValueError", Ops.Repr(x) + " isn't in the list");
                },
                "count" => a => (long)l.items.Count(i => Ops.Eq(i, a.Need(0, null))),
                "sort" => a => {
                    Sort(vm, l, a.Get(-1, "key"), a.Bool(-1, "reverse", false));
                    return PyNone.Value;
                },
                "reverse" => _ => {
                    l.items.Reverse();
                    return PyNone.Value;
                },
                "clear" => _ => {
                    l.items.Clear();
                    return PyNone.Value;
                },
                "copy" => _ => new PyList(l.items),
                _ => null,
            };
            return f == null ? null : new PyBuiltin(name, f);
        }

        private static PyBuiltin DictMethod(PyDict d, string name) {
            Func<Args, object> f = name switch {
                "get" => a => d.items.TryGetValue(a.Need(0, null), out var v) ? v : a.Get(1, "default", PyNone.Value),
                "keys" => _ => new PyList(d.Keys),
                "values" => _ => new PyList(d.Keys.Select(k => d.items[k])),
                "items" => _ => new PyList(d.Keys.Select(k => (object)new PyTuple(new[] { k, d.items[k] }))),
                "pop" => a => {
                    var k = a.Need(0, null);
                    if (d.items.TryGetValue(k, out var v)) {
                        d.Remove(k);
                        return v;
                    }
                    if (a.Count > 1) return a.pos[1];
                    throw new PyError("KeyError", Ops.Repr(k));
                },
                "popitem" => _ => {
                    if (d.order.Count == 0) throw new PyError("KeyError", "popitem(): the dictionary is empty");
                    var k = d.order[d.order.Count - 1];
                    var v = d.items[k];
                    d.Remove(k);
                    return new PyTuple(new[] { k, v });
                },
                "update" => a => {
                    if (a.Count > 0) {
                        if (a.pos[0] is PyDict src)
                            foreach (var k in src.Keys) d.Set(k, src.items[k]);
                        else
                            foreach (var pair in Ops.Iterate(a.pos[0])) {
                                var kv = Ops.Iterate(pair).ToList();
                                d.Set(kv[0], kv[1]);
                            }
                    }
                    if (a.kw != null)
                        foreach (var kv in a.kw) d.Set(kv.Key, kv.Value);
                    return PyNone.Value;
                },
                "setdefault" => a => {
                    var k = a.Need(0, null);
                    if (d.items.TryGetValue(k, out var v)) return v;
                    var def = a.Get(1, "default", PyNone.Value);
                    d.Set(k, def);
                    return def;
                },
                "clear" => _ => {
                    d.items.Clear();
                    d.order.Clear();
                    return PyNone.Value;
                },
                "copy" => _ => {
                    var c = new PyDict();
                    foreach (var k in d.Keys) c.Set(k, d.items[k]);
                    return c;
                },
                _ => null,
            };
            return f == null ? null : new PyBuiltin(name, f);
        }

        private static PyBuiltin SetMethod(PySet s, string name) {
            Func<Args, object> f = name switch {
                "add" => a => {
                    s.Add(a.Need(0, null));
                    return PyNone.Value;
                },
                "update" => a => {
                    foreach (var o in Ops.Iterate(a.Need(0, null))) s.Add(o);
                    return PyNone.Value;
                },
                "remove" => a => s.Remove(a.Need(0, null)) ? PyNone.Value : throw new PyError("KeyError", Ops.Repr(a.pos[0])),
                "discard" => a => {
                    s.Remove(a.Need(0, null));
                    return PyNone.Value;
                },
                "pop" => _ => {
                    if (s.order.Count == 0) throw new PyError("KeyError", "pop from an empty set");
                    var v = s.order[0];
                    s.Remove(v);
                    return v;
                },
                "clear" => _ => {
                    s.items.Clear();
                    s.order.Clear();
                    return PyNone.Value;
                },
                "copy" => _ => {
                    var c = new PySet();
                    foreach (var o in s.order) c.Add(o);
                    return c;
                },
                "union" => a => Ops.Binary("|", s, ToSet(a.Need(0, null))),
                "intersection" => a => Ops.Binary("&", s, ToSet(a.Need(0, null))),
                "difference" => a => Ops.Binary("-", s, ToSet(a.Need(0, null))),
                "issubset" => a => s.items.IsSubsetOf(ToSet(a.Need(0, null)).items),
                "issuperset" => a => s.items.IsSupersetOf(ToSet(a.Need(0, null)).items),
                _ => null,
            };
            return f == null ? null : new PyBuiltin(name, f);
        }

        private static PySet ToSet(object o) {
            if (o is PySet s) return s;
            var r = new PySet();
            foreach (var x in Ops.Iterate(o)) r.Add(x);
            return r;
        }
    }
}
