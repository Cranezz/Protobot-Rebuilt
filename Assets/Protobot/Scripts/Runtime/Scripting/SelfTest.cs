using System;
using System.Collections.Generic;
using System.Text;

namespace Protobot.Scripting {
    /// <summary>V2 (robot code): small programs with the output Python gives (run from the editor: SelfTest.Run()).</summary>
    public static class SelfTest {
        private static readonly (string name, string code, string expected)[] Cases = {
            ("arith", "print(7 // 2, -7 // 2, 7 % 3, -7 % 3, 2 ** 10, 7 / 2, 1 + 2.5, 10 - 3 * 2)", "3 -4 1 2 1024 3.5 3.5 4\n"),
            ("floats", "print(0.1 + 0.2, 1.0, 3 * 1.5, 1e20, 2.5e-5, round(2.5), round(3.14159, 2))", "0.30000000000000004 1.0 4.5 1e+20 2.5e-05 2 3.14\n"),
            ("strings", "s = 'Hello'\nprint(s.upper(), s[1:3], s[-1], len(s), s * 2, 'l' in s, s.replace('l', 'L'))", "HELLO el o 5 HelloHello True HeLLo\n"),
            ("fstring", "x = 3.14159\nn = 7\nprint(f'x={x:.2f} n={n:03d} {n*2} {\"q\"!r} {x=}')", "x=3.14 n=007 14 'q' x=3.14159\n"),
            ("format", "print('{} and {:>5}|{:<4}|{:^7}|'.format(1, 'ab', 'c', 'mid'), '%d-%.1f-%s' % (4, 2.25, 'z'))", "1 and    ab|c   |  mid  | 4-2.2-z\n"),
            ("lists", "a = [3, 1, 2]\na.append(5)\na.sort()\nprint(a, a[::-1], a.pop(), sum(a), max(a), min(a), [x * x for x in a if x > 1])", "[1, 2, 3] [5, 3, 2, 1] 5 6 3 1 [4, 9]\n"),   // (pop runs before print shows the list)
            ("dicts", "d = {'a': 1, 'b': 2}\nd['c'] = 3\nfor k, v in d.items():\n    print(k, v, end=' ')\nprint(d.get('z', 0), len(d), {k: v * 2 for k, v in d.items()})", "a 1 b 2 c 3 0 3 {'a': 2, 'b': 4, 'c': 6}\n"),
            ("control", "total = 0\nfor i in range(10):\n    if i == 7:\n        break\n    if i % 2:\n        continue\n    total += i\nelse:\n    total = -1\nprint(total)\nn = 0\nwhile n < 5:\n    n += 1\nprint(n)", "12\n5\n"),
            ("functions", "def f(a, b=2, *rest, **kw):\n    return a + b + sum(rest) + len(kw)\nprint(f(1), f(1, 3), f(1, 2, 3, 4), f(1, x=1, y=2), f(b=10, a=1))", "3 4 10 5 11\n"),
            ("closures", "def counter():\n    n = 0\n    def inc():\n        nonlocal n\n        n += 1\n        return n\n    return inc\nc = counter()\nc(); c()\nprint(c())\nsq = lambda x: x * x\nprint(list(map(sq, [1, 2, 3])))", "3\n[1, 4, 9]\n"),
            ("globals", "count = 0\ndef bump():\n    global count\n    count += 1\nbump(); bump()\nprint(count)", "2\n"),
            ("classes", "class Pid:\n    def __init__(self, kp, ki=0):\n        self.kp = kp\n        self.ki = ki\n        self.total = 0\n    def step(self, error):\n        self.total += error\n        return self.kp * error + self.ki * self.total\n    def __str__(self):\n        return 'Pid(' + str(self.kp) + ')'\np = Pid(2, 0.5)\nprint(p.step(10), p.step(10), p, isinstance(p, Pid))", "25.0 30.0 Pid(2) True\n"),
            ("inherit", "class A:\n    def hi(self):\n        return 'A'\n    def who(self):\n        return self.hi()\nclass B(A):\n    def hi(self):\n        return 'B' + super().hi()\nprint(B().who(), A().who())", "BA A\n"),
            ("exceptions", "try:\n    x = 1 / 0\nexcept ZeroDivisionError as e:\n    print('caught', e)\nfinally:\n    print('done')\ntry:\n    [1][5]\nexcept (KeyError, IndexError):\n    print('index')\ndef g():\n    try:\n        return 1\n    finally:\n        print('fin')\nprint(g())", "caught division by zero\ndone\nindex\nfin\n1\n"),
            ("raise", "class MyErr(Exception):\n    pass\ntry:\n    raise MyErr('bad')\nexcept Exception as e:\n    print(type(e).__name__, e)", "MyErr bad\n"),
            ("unpack", "a, b = 1, 2\na, b = b, a\nfirst, *rest = [1, 2, 3, 4]\nprint(a, b, first, rest)\nfor i, (x, y) in enumerate([(1, 2), (3, 4)]):\n    print(i, x + y)", "2 1 1 [2, 3, 4]\n0 3\n1 7\n"),
            ("math", "import math\nfrom math import sqrt, pi\nprint(sqrt(16), round(pi, 3), math.floor(-2.5), math.degrees(math.atan2(1, 1)))", "4.0 3.142 -3 45.0\n"),
            ("chained", "x = 5\nprint(1 < x < 10, 1 < x < 3, x == 5 != 6, not x, x and 0, x or 0, None is None)", "True False True False 0 5 True\n"),
            ("ternary", "v = -3\nprint('neg' if v < 0 else 'pos', abs(v), sorted([3, 1, 2], reverse=True), sorted(['bb', 'a', 'ccc'], key=len))", "neg 3 [3, 2, 1] ['a', 'bb', 'ccc']\n"),
            ("zip", "print(list(zip([1, 2, 3], 'ab')), dict(zip(['x', 'y'], [1, 2])), any([0, 1]), all([]))", "[(1, 'a'), (2, 'b')] {'x': 1, 'y': 2} True True\n"),
            ("slices", "a = list(range(10))\nprint(a[2:5], a[:3], a[-2:], a[::3])\na[1:3] = [9]\nprint(a[:4])\ndel a[0]\nprint(len(a))", "[2, 3, 4] [0, 1, 2] [8, 9] [0, 3, 6, 9]\n[0, 9, 3, 4]\n8\n"),
            ("sets", "s = {1, 2, 2, 3}\ns.add(4)\nprint(len(s), 2 in s, sorted(s | {9}))", "4 True [1, 2, 3, 4, 9]\n"),
            ("whiletrue", "i = 0\nwhile True:\n    i += 1\n    if i >= 3:\n        break\nprint(i)", "3\n"),
            ("nested", "def outer(n):\n    def inner(k):\n        return k + n\n    return [inner(i) for i in range(n)]\nprint(outer(3))", "[3, 4, 5]\n"),
            ("strfmt", "print(str(3), str(2.0), int('42'), float('1.5'), int(3.9), int(-3.9), bool(''), repr('a'))", "3 2.0 42 1.5 3 -3 False 'a'\n"),
        };

        private static readonly (string name, string code, string error)[] Errors = {
            ("name", "x = 1\nprint(y)", "line 2: NameError"),
            ("type", "x = 'a' + 1", "line 1: TypeError"),
            ("indent", "if True:\nprint(1)", "SyntaxError line 2"),
            ("missing colon", "def f()\n    pass", "SyntaxError line 1"),
            ("unclosed", "print((1, 2)\nx = 3", "SyntaxError"),
            ("attr", "class A:\n    pass\na = A()\nprint(a.speed)", "line 4: AttributeError"),
            ("recursion", "def f(n):\n    return f(n + 1)\nf(0)", "RecursionError"),
            ("args", "def f(a):\n    pass\nf(1, 2)", "TypeError"),
            ("index", "a = [1]\nprint(a[3])", "line 2: IndexError"),
            ("assign to call", "f() = 3", "SyntaxError"),
        };

        public static string Run() {
            var report = new StringBuilder();
            var passed = 0;
            foreach (var (name, code, expected) in Cases) {
                var output = new StringBuilder();
                string got;
                try {
                    var vm = new Interpreter { Output = s => output.Append(s) };
                    vm.Load(code);
                    for (var i = 0; i < 1000 && vm.Running; i++) vm.Step();
                    got = vm.Failed != null ? output + "ERROR line " + vm.Failed.line + ": " + vm.Failed.Describe : output.ToString();
                }
                catch (SyntaxErr e) {
                    got = "SyntaxError line " + e.line + ": " + e.Message;
                }
                catch (Exception e) {
                    got = "CRASH " + e;
                }
                if (got == expected) passed++;
                else report.Append("FAIL ").Append(name).Append("\n  expected: ").Append(expected.Replace("\n", "\\n")).Append("\n  got:      ").Append(got.Replace("\n", "\\n")).Append('\n');
            }
            foreach (var (name, code, error) in Errors) {
                string got;
                try {
                    var vm = new Interpreter();
                    vm.Load(code);
                    for (var i = 0; i < 1000 && vm.Running; i++) vm.Step();
                    got = vm.Failed != null ? "line " + vm.Failed.line + ": " + vm.Failed.Describe : "no error";
                }
                catch (SyntaxErr e) {
                    got = "SyntaxError line " + e.line + ": " + e.Message;
                }
                catch (Exception e) {
                    got = "CRASH " + e;
                }
                if (got.Contains(error)) passed++;
                else report.Append("FAIL error ").Append(name).Append(": expected ").Append(error).Append(", got ").Append(got).Append('\n');
                report.Append("  [").Append(name).Append("] ").Append(got).Append('\n');
            }
            // Waiting: a thread that waits 0.5 s while another counts.
            {
                var output = new StringBuilder();
                var vm = new Interpreter { Output = s => output.Append(s) };
                vm.builtins["wait"] = new PyBuiltin("wait", a => new Suspend(a.vm.Time + a.Number(0, null, 0) / 1000));
                vm.builtins["Thread"] = new PyBuiltin("Thread", a => {
                    a.vm.Start(a.pos[0], null, "thread");
                    return PyNone.Value;
                });
                vm.Load("def side():\n    for i in range(3):\n        print('side', i)\n        wait(100)\nThread(side)\nwait(500)\nprint('main done')");
                var steps = 0;
                while (vm.Running && steps < 1000) {
                    vm.Time = steps / 240.0;
                    vm.Step();
                    steps++;
                }
                var expected = "side 0\nside 1\nside 2\nmain done\n";
                if (output.ToString() == expected && Math.Abs(steps - 121) < 3) passed++;
                else report.Append("FAIL wait: ").Append(output.ToString().Replace("\n", "\\n")).Append(" in ").Append(steps).Append(" steps\n");
            }
            // Speed: a busy loop.
            {
                var vm = new Interpreter { Budget = 100_000_000 };
                vm.Load("t = 0\nfor i in range(200000):\n    t += i * 2\n");
                var sw = System.Diagnostics.Stopwatch.StartNew();
                vm.Step();
                report.Append("speed: ").Append(vm.Executed).Append(" instructions in ").Append(sw.ElapsedMilliseconds).Append(" ms\n");
            }
            return passed + "/" + (Cases.Length + Errors.Length + 1) + " passed\n" + report;
        }
    }
}
