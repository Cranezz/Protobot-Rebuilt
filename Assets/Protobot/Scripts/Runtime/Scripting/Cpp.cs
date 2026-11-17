using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Protobot.Scripting {
    // V2 (robot code): C++ as VEX teams write it (PROS + LemLib / EZ-Template, VEXcode C++), translated into the same
    // syntax tree the Python front end makes, so it runs on the same interpreter with the same robot APIs (and errors
    // and traces point at the C++ lines). What robot code uses: functions, structs/classes (fields, constructors with
    // init lists, methods, inheritance), lambdas (captures), for/while/do/switch, range-for, auto, references and
    // pointers (& -> * nullptr as plain references), namespaces, using namespace, enums, #define constants,
    // std::vector/std::string basics, printf/std::cout, C++ integer division and modulo, designated initializers
    // ({.forwards = false} -> keyword arguments). Not there: templates of your own, operator overloading, pointer
    // arithmetic, references to numbers (a function can't change the caller's int), function-like macros.

    public sealed class CppToken {
        public string kind, text;   // kind: id, num, str, chr, op, eof
        public object value;
        public int line, col;
        public override string ToString() => text;
    }

    public static class CppLexer {
        private static readonly string[] Ops3 = { ">>=", "<<=", "...", "->*" };
        private static readonly string[] Ops2 = { "::", "->", "++", "--", "<<", ">>", "<=", ">=", "==", "!=", "&&", "||", "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=" };

        public static List<CppToken> Tokenize(string src, Dictionary<string, List<CppToken>> defines, List<string> warnings, bool addEof = true) {
            var list = new List<CppToken>();
            int i = 0, line = 1, lineStart = 0;
            var atLineStart = true;
            while (i < src.Length) {
                var c = src[i];
                if (c == '\n') { line++; i++; lineStart = i; atLineStart = true; continue; }
                if (c == '\r' || c == ' ' || c == '\t' || c == '\f' || c == '\v') { i++; continue; }
                if (c == '#' && atLineStart) {
                    var sb = new StringBuilder();
                    var startLine = line;
                    while (i < src.Length && src[i] != '\n') {
                        if (src[i] == '\\' && i + 1 < src.Length && (src[i + 1] == '\n' || src[i + 1] == '\r')) {
                            i++;
                            if (src[i] == '\r') i++;
                            if (i < src.Length && src[i] == '\n') { i++; line++; }
                            continue;
                        }
                        if (src[i] == '/' && i + 1 < src.Length && src[i + 1] == '/') {
                            while (i < src.Length && src[i] != '\n') i++;
                            break;
                        }
                        sb.Append(src[i]);
                        i++;
                    }
                    Directive(sb.ToString(), startLine, defines, warnings);
                    continue;
                }
                atLineStart = false;
                var col = i - lineStart + 1;
                if (c == '/' && i + 1 < src.Length && src[i + 1] == '/') {
                    while (i < src.Length && src[i] != '\n') i++;
                    continue;
                }
                if (c == '/' && i + 1 < src.Length && src[i + 1] == '*') {
                    i += 2;
                    while (i < src.Length && !(src[i] == '*' && i + 1 < src.Length && src[i + 1] == '/')) {
                        if (src[i] == '\n') { line++; lineStart = i + 1; }
                        i++;
                    }
                    i += 2;
                    continue;
                }
                if (char.IsLetter(c) || c == '_') {
                    var start = i;
                    while (i < src.Length && (char.IsLetterOrDigit(src[i]) || src[i] == '_')) i++;
                    var word = src.Substring(start, i - start);
                    // String prefixes (u8"", L"", R"(...)").
                    if (i < src.Length && src[i] == '"' && (word == "R" || word == "u8R" || word == "LR")) {
                        var open = src.IndexOf('(', i);
                        var delim = src.Substring(i + 1, open - i - 1);
                        var close = src.IndexOf(")" + delim + "\"", open, StringComparison.Ordinal);
                        if (close < 0) throw new SyntaxErr("this raw string is never closed", line, col);
                        var raw = src.Substring(open + 1, close - open - 1);
                        foreach (var ch in raw) if (ch == '\n') line++;
                        list.Add(new CppToken { kind = "str", text = raw, value = raw, line = line, col = col });
                        i = close + delim.Length + 2;
                        continue;
                    }
                    if (i < src.Length && (src[i] == '"' || src[i] == '\'') && (word == "u8" || word == "L" || word == "u" || word == "U")) continue;
                    if (defines.TryGetValue(word, out var body)) {
                        foreach (var d in body) list.Add(new CppToken { kind = d.kind, text = d.text, value = d.value, line = line, col = col });
                        continue;
                    }
                    list.Add(new CppToken { kind = "id", text = word, line = line, col = col });
                    continue;
                }
                if (char.IsDigit(c) || (c == '.' && i + 1 < src.Length && char.IsDigit(src[i + 1]))) {
                    var start = i;
                    var isFloat = false;
                    if (c == '0' && i + 1 < src.Length && (src[i + 1] == 'x' || src[i + 1] == 'X')) {
                        i += 2;
                        while (i < src.Length && (Uri.IsHexDigit(src[i]) || src[i] == '\'')) i++;
                    }
                    else if (c == '0' && i + 1 < src.Length && (src[i + 1] == 'b' || src[i + 1] == 'B')) {
                        i += 2;
                        while (i < src.Length && (src[i] == '0' || src[i] == '1' || src[i] == '\'')) i++;
                    }
                    else {
                        while (i < src.Length && (char.IsDigit(src[i]) || src[i] == '\'')) i++;
                        if (i < src.Length && src[i] == '.') { isFloat = true; i++; while (i < src.Length && char.IsDigit(src[i])) i++; }
                        if (i < src.Length && (src[i] == 'e' || src[i] == 'E') && i + 1 < src.Length && (char.IsDigit(src[i + 1]) || ((src[i + 1] == '-' || src[i + 1] == '+') && i + 2 < src.Length && char.IsDigit(src[i + 2])))) {
                            isFloat = true;
                            i += 2;
                            while (i < src.Length && char.IsDigit(src[i])) i++;
                        }
                    }
                    var text = src.Substring(start, i - start).Replace("'", "");
                    var suffixStart = i;
                    while (i < src.Length && "fFlLuU".IndexOf(src[i]) >= 0) i++;
                    var suffix = src.Substring(suffixStart, i - suffixStart);
                    if (suffix.IndexOfAny(new[] { 'f', 'F' }) >= 0 && !text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) isFloat = true;
                    object value;
                    if (isFloat) value = double.Parse(text, CultureInfo.InvariantCulture);
                    else if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) value = Convert.ToInt64(text.Substring(2), 16);
                    else if (text.StartsWith("0b", StringComparison.OrdinalIgnoreCase)) value = Convert.ToInt64(text.Substring(2), 2);
                    else if (text.Length > 1 && text[0] == '0') value = Convert.ToInt64(text, 8);
                    else value = long.Parse(text, CultureInfo.InvariantCulture);
                    // V2: unit literals (EZ-Template / okapi: 24_in, 90_deg, 500_ms): lengths in inches, angles in
                    // degrees, times in milliseconds, which is what the robot libraries take.
                    if (i + 1 < src.Length && src[i] == '_' && char.IsLetter(src[i + 1])) {
                        var unitStart = ++i;
                        while (i < src.Length && (char.IsLetterOrDigit(src[i]) || src[i] == '_')) i++;
                        var unit = src.Substring(unitStart, i - unitStart);
                        double scale;
                        switch (unit) {
                            case "in": case "inch": scale = 1; break;
                            case "cm": scale = 1 / 2.54; break;
                            case "mm": scale = 1 / 25.4; break;
                            case "m": scale = 1 / 0.0254; break;
                            case "ft": scale = 12; break;
                            case "tile": case "tiles": scale = 24; break;
                            case "deg": scale = 1; break;
                            case "rad": scale = 180 / Math.PI; break;
                            case "rot": scale = 360; break;
                            case "ms": scale = 1; break;
                            case "s": case "sec": scale = 1000; break;
                            case "min": scale = 60000; break;
                            default:
                                scale = 1;
                                warnings?.Add("line " + line + ": the unit _" + unit + " is read as a plain number");
                                break;
                        }
                        value = Convert.ToDouble(value, CultureInfo.InvariantCulture) * scale;
                        text += "_" + unit;
                    }
                    list.Add(new CppToken { kind = "num", text = text, value = value, line = line, col = col });
                    continue;
                }
                if (c == '"' || c == '\'') {
                    var quote = c;
                    i++;
                    var sb = new StringBuilder();
                    while (i < src.Length && src[i] != quote) {
                        if (src[i] == '\n') throw new SyntaxErr("this " + (quote == '"' ? "string" : "character") + " isn't closed on its line", line, col);
                        if (src[i] == '\\' && i + 1 < src.Length) {
                            i++;
                            var e = src[i];
                            switch (e) {
                                case 'n': sb.Append('\n'); break;
                                case 't': sb.Append('\t'); break;
                                case 'r': sb.Append('\r'); break;
                                case >= '0' and <= '7': {   // V2: octal (\033 = escape)
                                    var v = e - '0';
                                    for (var k = 1; k < 3 && i + 1 < src.Length && src[i + 1] >= '0' && src[i + 1] <= '7'; k++) v = v * 8 + (src[++i] - '0');
                                    sb.Append((char)v);
                                    break;
                                }
                                case 'a': sb.Append('\a'); break;
                                case 'b': sb.Append('\b'); break;
                                case 'x': {
                                    var h = 0;
                                    var n = 0;
                                    while (i + 1 < src.Length && Uri.IsHexDigit(src[i + 1]) && n < 2) { h = h * 16 + Convert.ToInt32(src[i + 1].ToString(), 16); i++; n++; }
                                    sb.Append((char)h);
                                    break;
                                }
                                default: sb.Append(e); break;
                            }
                            i++;
                            continue;
                        }
                        sb.Append(src[i]);
                        i++;
                    }
                    if (i >= src.Length) throw new SyntaxErr("this " + (quote == '"' ? "string" : "character") + " is never closed", line, col);
                    i++;
                    list.Add(new CppToken { kind = quote == '"' ? "str" : "chr", text = sb.ToString(), value = sb.ToString(), line = line, col = col });
                    continue;
                }
                string op = null;
                foreach (var o in Ops3) if (string.CompareOrdinal(src, i, o, 0, 3) == 0) { op = o; break; }
                if (op == null) foreach (var o in Ops2) if (string.CompareOrdinal(src, i, o, 0, 2) == 0) { op = o; break; }
                op ??= c.ToString();
                i += op.Length;
                list.Add(new CppToken { kind = "op", text = op, line = line, col = col });
            }
            if (addEof) list.Add(new CppToken { kind = "eof", text = "the end of the program", line = line, col = 1 });
            return list;
        }

        private static void Directive(string text, int line, Dictionary<string, List<CppToken>> defines, List<string> warnings) {
            var t = text.Trim().TrimStart('#').Trim();
            if (!t.StartsWith("define", StringComparison.Ordinal)) return;   // #include, #pragma, #if... (everything is in)
            var rest = t.Substring(6).Trim();
            var n = 0;
            while (n < rest.Length && (char.IsLetterOrDigit(rest[n]) || rest[n] == '_')) n++;
            if (n == 0) return;
            var name = rest.Substring(0, n);
            if (n < rest.Length && rest[n] == '(') {
                warnings.Add("line " + line + ": the macro " + name + "(...) isn't supported (write it as a function)");
                return;
            }
            try { defines[name] = Tokenize(rest.Substring(n), defines, warnings, false); }
            catch (SyntaxErr) { warnings.Add("line " + line + ": #define " + name + " couldn't be read"); }
        }
    }

    /// <summary>C++ to the interpreter's syntax tree.</summary>
    public sealed class CppTranslator {
        // Statement-only forms (an assignment or ++ inside a bigger expression isn't supported).
        private sealed class CAssign : Expr { public Expr target; public string op; public Expr value; }
        private sealed class CIncr : Expr { public Expr target; public int delta; }

        private sealed class TypeInfo {
            public string name = "";      // as written ("pros::Motor", "int", "std::vector")
            public string kind = "class"; // int, float, bool, str, char, void, auto, vector, class, pointer
            public bool pointer, reference;
        }

        private sealed class Ctx {
            public Ctx parent;
            public FuncDef def;
            public readonly HashSet<string> declared = new();
            public readonly HashSet<string> assigned = new();
            public readonly Dictionary<string, string> types = new();
            public string cls;            // a method of this class
            public bool isModule;
        }

        private sealed class ClassInfo {
            public string name;
            public readonly HashSet<string> members = new();
            public readonly Dictionary<string, string> types = new();
            public string baseName;
        }

        private readonly List<CppToken> t;
        private int p;
        private readonly HashSet<string> namespaces = new();
        private readonly Dictionary<string, ClassInfo> classes = new();
        private readonly HashSet<string> functions = new();
        private readonly HashSet<string> usings = new();
        private readonly List<Stmt> usingStmts = new();
        private readonly Ctx module = new() { isModule = true };
        private Ctx ctx;
        private List<Stmt> hoist = new();
        private int lambdas, temps;
        public readonly List<string> warnings = new();

        private static readonly HashSet<string> PyKeywords = new() {
            "and", "as", "assert", "async", "await", "def", "del", "elif", "except", "from", "global", "import", "in", "is",
            "lambda", "nonlocal", "not", "or", "pass", "raise", "with", "yield", "None", "True", "False", "finally", "self",
        };

        private static readonly HashSet<string> Qualifiers = new() {
            "const", "constexpr", "static", "inline", "extern", "volatile", "mutable", "register", "virtual", "explicit",
            "thread_local", "typename", "struct", "class", "enum", "consteval", "constinit",
        };

        // Words that never start a type (so "delete p;" or "return x;" aren't read as declarations).
        private static readonly HashSet<string> NotTypes = new() {
            "delete", "new", "return", "throw", "case", "default", "else", "goto", "sizeof", "if", "while", "for", "do",
            "switch", "break", "continue", "try", "catch", "true", "false", "nullptr", "NULL", "this", "operator",
        };

        private static readonly HashSet<string> IntTypes = new() {
            "int", "long", "short", "unsigned", "signed", "size_t", "int8_t", "int16_t", "int32_t", "int64_t", "uint8_t",
            "uint16_t", "uint32_t", "uint64_t", "std::size_t", "std::int32_t", "std::uint32_t", "std::uint8_t", "std::int8_t",
        };

        private CppTranslator(List<CppToken> tokens) {
            t = tokens;
            ctx = module;
        }

        /// <summary>The program as the interpreter's statements, with the call that starts it at the end (main(), or
        /// PROS's initialize() and its competition functions).</summary>
        public static List<Stmt> Translate(string source, out List<string> warnings) {
            var defines = new Dictionary<string, List<CppToken>>();
            var w = new List<string>();
            var tokens = CppLexer.Tokenize(source ?? "", defines, w);
            var tr = new CppTranslator(tokens);
            tr.warnings.AddRange(w);
            tr.PreScan();
            var body = tr.Module();
            warnings = tr.warnings;
            return body;
        }

        /// <summary>Whether a program reads as C++ (else it's Python).</summary>
        public static bool LooksLikeCpp(string source) {
            if (string.IsNullOrEmpty(source)) return false;
            if (source.Contains("#include") || source.Contains("pros::") || source.Contains("vex::") || source.Contains("lemlib::")) return true;
            return System.Text.RegularExpressions.Regex.IsMatch(source, @"(^|\n)\s*(void|int|double|bool|auto)\s+\w+\s*\([^)]*\)\s*\{");
        }

        // ------------------------------------------------------------------ tokens

        private CppToken Peek(int ahead = 0) => t[Math.Min(p + ahead, t.Count - 1)];
        private bool Is(string text, int ahead = 0) { var k = Peek(ahead); return (k.kind == "op" || k.kind == "id") && k.text == text; }
        private bool IsId(int ahead = 0) => Peek(ahead).kind == "id";
        private CppToken Next() => t[Math.Min(p++, t.Count - 1)];

        private bool Accept(string text) {
            if (!Is(text)) return false;
            p++;
            return true;
        }

        private CppToken Expect(string text, string what = null) {
            if (Is(text)) return Next();
            var k = Peek();
            throw new SyntaxErr("expected '" + text + "'" + (what != null ? " " + what : "") + " but found " + (k.kind == "eof" ? k.text : "'" + k.text + "'"), k.line, k.col);
        }

        private string ExpectId(string what) {
            var k = Peek();
            if (k.kind != "id") throw new SyntaxErr("expected " + what + " but found " + (k.kind == "eof" ? k.text : "'" + k.text + "'"), k.line, k.col);
            p++;
            return k.text;
        }

        private SyntaxErr Error(string message, CppToken at = null) {
            at ??= Peek();
            return new SyntaxErr(message, at.line, at.col);
        }

        private T At<T>(T node, CppToken tok) where T : Node {
            node.line = tok.line;
            node.column = tok.col;
            return node;
        }

        private void SkipBalanced(string open, string close) {
            var depth = 0;
            do {
                if (Peek().kind == "eof") throw Error("'" + open + "' is never closed");
                if (Is(open)) depth++;
                else if (Is(close)) depth--;
                p++;
            } while (depth > 0);
        }

        // Template arguments <...> (">>" closes two).
        private void SkipTemplate() {
            var depth = 0;
            do {
                if (Peek().kind == "eof") throw Error("'<' is never closed");
                if (Is("<")) depth++;
                else if (Is(">")) depth--;
                else if (Is(">>")) depth -= 2;
                else if (Is("(")) { SkipBalanced("(", ")"); continue; }
                p++;
            } while (depth > 0);
        }

        // ------------------------------------------------------------------ pre-scan: class members, namespaces

        private void PreScan() {
            for (var i = 0; i < t.Count - 2; i++) {
                if (t[i].kind == "id" && t[i].text == "namespace" && t[i + 1].kind == "id" && !(i > 0 && t[i - 1].text == "using")) namespaces.Add(t[i + 1].text);
                if (t[i].kind != "id" || (t[i].text != "struct" && t[i].text != "class") || t[i + 1].kind != "id") continue;
                if (i > 0 && t[i - 1].text == "enum") continue;
                var name = t[i + 1].text;
                var j = i + 2;
                string baseName = null;
                if (t[j].text == ":") {
                    while (j < t.Count && t[j].text != "{" && t[j].text != ";") {
                        if (t[j].kind == "id" && t[j].text != "public" && t[j].text != "private" && t[j].text != "protected" && t[j].text != "virtual") baseName = t[j].text;
                        j++;
                    }
                }
                if (j >= t.Count || t[j].text != "{") continue;
                var info = new ClassInfo { name = name, baseName = baseName };
                classes[name] = info;
                var depth = 0;
                for (var k = j; k < t.Count; k++) {
                    if (t[k].text == "{" && t[k].kind == "op") depth++;
                    else if (t[k].text == "}" && t[k].kind == "op") { depth--; if (depth == 0) break; }
                    else if (depth == 1 && t[k].kind == "id" && k > j + 1 && (t[k + 1].text == "(" || t[k + 1].text == "=" || t[k + 1].text == ";" || t[k + 1].text == "{" || t[k + 1].text == "[" || t[k + 1].text == ","))
                        if (t[k].text != name && !Qualifiers.Contains(t[k].text) && t[k].text != "operator" && t[k - 1].text != "~"
                            && (t[k - 1].kind == "id" || t[k - 1].text == ">" || t[k - 1].text == "*" || t[k - 1].text == "&" || t[k - 1].text == ">>" || t[k - 1].text == ","))
                            info.members.Add(t[k].text);
                }
            }
        }

        // ------------------------------------------------------------------ the program

        private List<Stmt> Module() {
            var body = new List<Stmt>();
            while (Peek().kind != "eof") body.AddRange(WithHoist(TopLevel));
            body.InsertRange(0, usingStmts);
            // How it starts: main() (VEXcode C++), else PROS's initialize() then its competition functions.
            var end = Peek();
            Stmt Call(string fn, params Expr[] args) {
                var call = At(new CallExpr { func = At(new NameExpr { name = fn }, end) }, end);
                call.args.AddRange(args);
                return At(new ExprStmt { expr = call }, end);
            }
            Expr FnOrNone(string fn) => functions.Contains(fn) ? At(new NameExpr { name = fn }, end) : At(new ConstExpr { value = PyNone.Value }, end);
            if (functions.Contains("main")) body.Add(Call("main"));
            else {
                if (functions.Contains("initialize")) body.Add(Call("initialize"));
                if (functions.Contains("competition_initialize")) body.Add(Call("competition_initialize"));
                if (functions.Contains("opcontrol") || functions.Contains("autonomous")) body.Add(Call("__competition", FnOrNone("opcontrol"), FnOrNone("autonomous")));
            }
            return body;
        }

        // Runs a parse step, putting the lambdas it made (as defs) before what it made.
        private List<Stmt> WithHoist(Func<List<Stmt>> step) {
            var saved = hoist;
            hoist = new List<Stmt>();
            var made = step();
            var result = hoist;
            hoist = saved;
            result.AddRange(made);
            return result;
        }

        private List<Stmt> TopLevel() {
            var tok = Peek();
            if (Accept(";")) return new List<Stmt>();
            if (Is("using")) return Using();
            if (Is("namespace")) {
                p++;
                if (IsId()) namespaces.Add(Next().text);
                while (Accept("::")) namespaces.Add(ExpectId("a namespace name"));
                if (Accept("=")) {   // namespace alias
                    while (!Is(";")) p++;
                    p++;
                    return new List<Stmt>();
                }
                Expect("{", "to open the namespace");
                var inner = new List<Stmt>();
                while (!Is("}")) {
                    if (Peek().kind == "eof") throw Error("this namespace is never closed", tok);
                    inner.AddRange(WithHoist(TopLevel));
                }
                p++;
                return inner;
            }
            if (Is("template")) {
                p++;
                SkipTemplate();
                return TopLevel();
            }
            if (Is("typedef")) {
                while (!Is(";")) p++;
                p++;
                return new List<Stmt>();
            }
            if (Is("extern") && Peek(1).kind == "str") {   // extern "C" { ... }
                p += 2;
                if (Accept("{")) {
                    var inner = new List<Stmt>();
                    while (!Accept("}")) inner.AddRange(WithHoist(TopLevel));
                    return inner;
                }
                return TopLevel();
            }
            if (Is("enum")) return Enum();
            if ((Is("struct") || Is("class")) && IsId(1) && (Is("{", 2) || Is(":", 2) || Is("final", 2))) return Class();
            if (Is("ASSET") && Is("(", 1)) {   // LemLib's path file
                p += 2;
                var name = ExpectId("the path's name");
                Expect(")");
                Accept(";");
                warnings.Add("line " + tok.line + ": ASSET(" + name + ") path files aren't loaded yet (follow() needs a list of points here)");
                module.declared.Add(name);
                return new List<Stmt> { At(new AssignStmt { targets = { At(new NameExpr { name = name }, tok) }, value = At(new ConstExpr { value = PyNone.Value }, tok) }, tok) };
            }
            var decl = Declaration(true);
            if (decl != null) return decl;
            throw Error("this isn't something a C++ program can have here (a function, a variable, a struct...)");
        }

        private List<Stmt> Using() {
            var tok = Next();
            if (Accept("namespace")) {
                var names = new List<string> { ExpectId("a namespace") };
                while (Accept("::")) names.Add(ExpectId("a namespace"));
                Expect(";");
                // pros, vex, lemlib, std, ez: their names become usable without the prefix.
                if (namespaces.Contains(names[0])) return new List<Stmt>();
                var module = string.Join("::", names) switch {
                    "pros::v5" or "pros::literals" or "pros::adi" or "std::chrono" or "std::literals" => null,
                    var m => m,
                };
                if (module == null) return new List<Stmt>();
                // (Gathered at the top of the program: a star import can't be inside a function.)
                var key = "cpp:" + names[0];
                if (!usings.Contains(key)) {
                    usings.Add(key);
                    usingStmts.Add(At(new ImportStmt { module = key, star = true, names = null }, tok));
                }
                return new List<Stmt>();
            }
            while (!Is(";")) p++;   // using X = Y; using std::cout;
            p++;
            return new List<Stmt>();
        }

        private List<Stmt> Enum() {
            var tok = Next();
            var scoped = Accept("class") || Accept("struct");
            var name = IsId() ? Next().text : null;
            if (Accept(":")) while (!Is("{")) p++;
            if (Accept(";")) return new List<Stmt>();
            Expect("{", "to list the enum's values");
            var result = new List<Stmt>();
            var dict = At(new DictExpr(), tok);
            long next = 0;
            while (!Accept("}")) {
                var vt = Peek();
                var item = ExpectId("an enum value");
                Expr value;
                if (Accept("=")) {
                    value = Ternary();
                    if (value is ConstExpr c && c.value is long l) next = l;
                    else if (value is UnaryExpr u && u.op == "-" && u.operand is ConstExpr c2 && c2.value is long l2) next = -l2;
                }
                else value = At(new ConstExpr { value = next }, vt);
                dict.keys.Add(At(new ConstExpr { value = item }, vt));
                dict.values.Add(value);
                next++;
                if (!scoped) {
                    module.declared.Add(item);
                    result.Add(At(new AssignStmt { targets = { At(new NameExpr { name = item }, vt) }, value = At(new ConstExpr { value = next - 1 }, vt) }, vt));
                }
                Accept(",");
            }
            Accept(";");
            if (name != null) {
                module.declared.Add(name);
                result.Add(At(new AssignStmt { targets = { At(new NameExpr { name = name }, tok) }, value = Call("__enum", tok, At(new ConstExpr { value = name }, tok), dict) }, tok));
            }
            return result;
        }

        private Expr Call(string fn, CppToken at, params Expr[] args) {
            var c = At(new CallExpr { func = At(new NameExpr { name = fn }, at) }, at);
            c.args.AddRange(args);
            return c;
        }

        // ------------------------------------------------------------------ types

        private bool TryType(out TypeInfo type) {
            type = new TypeInfo();
            var start = p;
            var sawWord = false;
            while (IsId() && Qualifiers.Contains(Peek().text) && !(Peek().text == "enum")) p++;
            var name = new StringBuilder();
            if (IsId() && (Peek().text == "unsigned" || Peek().text == "signed" || Peek().text == "long" || Peek().text == "short")) {
                while (IsId() && (Peek().text == "unsigned" || Peek().text == "signed" || Peek().text == "long" || Peek().text == "short")) { name.Append(Next().text).Append(' '); sawWord = true; }
                if (IsId() && (Peek().text == "int" || Peek().text == "double" || Peek().text == "char")) name.Append(Next().text);
                type.name = name.ToString().Trim();
                type.kind = type.name.Contains("double") ? "float" : type.name.Contains("char") ? "char" : "int";
            }
            else {
                if (Is("::")) p++;
                if (!IsId() || NotTypes.Contains(Peek().text)) { p = start; return false; }
                name.Append(Next().text);
                sawWord = true;
                if (Is("<") && TemplateNamed(name.ToString())) SkipTemplate();
                while (Is("::") && IsId(1)) {
                    p++;
                    name.Append("::").Append(Next().text);
                    if (Is("<") && TemplateNamed(name.ToString())) SkipTemplate();
                }
                type.name = name.ToString();
                type.kind = KindOf(type.name);
            }
            if (!sawWord) { p = start; return false; }
            while (true) {
                if (Accept("const") || Accept("volatile")) continue;
                if (Accept("*")) { type.pointer = true; continue; }
                if (Accept("&") || Accept("&&")) { type.reference = true; continue; }
                break;
            }
            return true;
        }

        // Names written with <...> (anything known as a class template, or std::...).
        private bool TemplateNamed(string name) =>
            name.StartsWith("std::") || name is "vector" or "array" or "pair" or "map" or "unordered_map" or "function" or "optional" or "tuple"
            or "shared_ptr" or "unique_ptr" or "static_cast" or "dynamic_cast" or "reinterpret_cast" or "const_cast" || name.EndsWith("::Task") || classes.ContainsKey(name);

        private static string KindOf(string name) {
            if (IntTypes.Contains(name)) return "int";
            switch (name) {
                case "double": case "float": case "long double": return "float";
                case "bool": return "bool";
                case "char": return "char";
                case "void": return "void";
                case "auto": return "auto";
                case "std::string": case "string": return "str";
                case "std::vector": case "vector": case "std::array": case "array": case "std::deque": return "vector";
                case "std::map": case "map": case "std::unordered_map": case "unordered_map": return "map";
                case "std::function": case "function": return "auto";
            }
            return "class";
        }

        private Expr DefaultValue(TypeInfo type, CppToken at) {
            if (type.pointer) return At(new ConstExpr { value = PyNone.Value }, at);
            return type.kind switch {
                "int" => At(new ConstExpr { value = 0L }, at),
                "float" => At(new ConstExpr { value = 0.0 }, at),
                "bool" => At(new ConstExpr { value = false }, at),
                "char" => At(new ConstExpr { value = "\0" }, at),
                "str" => At(new ConstExpr { value = "" }, at),
                "vector" => At(new ListExpr(), at),
                "map" => At(new DictExpr(), at),
                "auto" => At(new ConstExpr { value = PyNone.Value }, at),
                _ => At(new CallExpr { func = TypeExpr(type, at) }, at),
            };
        }

        private Expr TypeExpr(TypeInfo type, CppToken at) {
            if (type.kind == "str") return At(new NameExpr { name = "str" }, at);
            if (type.kind == "vector") return At(new NameExpr { name = "__vector" }, at);
            if (type.kind == "map") return At(new NameExpr { name = "dict" }, at);
            if (type.kind == "int") return At(new NameExpr { name = "__cint" }, at);
            if (type.kind == "float") return At(new NameExpr { name = "float" }, at);
            if (type.kind == "bool") return At(new NameExpr { name = "bool" }, at);
            return Qualified(type.name.Split(new[] { "::" }, StringSplitOptions.RemoveEmptyEntries).ToList(), at);
        }

        // ------------------------------------------------------------------ declarations

        // A variable or function declaration (at the top level, in a block or in a class). Null: not one.
        private List<Stmt> Declaration(bool topLevel, ClassInfo cls = null) {
            var start = p;
            var tok = Peek();
            var isStatic = false;
            for (var k = p; k < t.Count && t[k].kind == "id" && Qualifiers.Contains(t[k].text); k++) if (t[k].text == "static") isStatic = true;
            if (!TryType(out var type)) return null;
            // A function defined outside its class: void Robot::drive(...) { }
            if (!IsId()) {
                p = start;
                return null;
            }
            var nameTok = Peek();
            var name = Next().text;
            string ownerClass = null;
            while (Accept("::")) {
                ownerClass = name;
                name = ExpectId("a name");
            }
            if (name == "operator") throw Error("operator overloading isn't supported", nameTok);
            if (Is("(") && (IsParameterList() || ownerClass != null)) return Function(type, name, nameTok, cls, ownerClass);
            if (!(Is("=") || Is(";") || Is("{") || Is("[") || Is(",") || Is("(") || Is(":"))) {
                p = start;
                return null;
            }
            var result = new List<Stmt>();
            while (true) {
                result.Add(Variable(type, name, nameTok, cls));
                if (!Accept(",")) break;
                while (Accept("*") || Accept("&")) { }
                nameTok = Peek();
                name = ExpectId("a variable name");
            }
            Expect(";", "after the declaration");
            _ = isStatic;
            return result;
        }

        // Is "(" ... ")" a parameter list (then it's a function), not constructor arguments?
        private bool IsKnownValue(string name) {
            if (functions.Contains(PyName(name))) return true;
            for (var c = ctx; c != null; c = c.parent)
                if (c.declared.Contains(name) || c.declared.Contains(PyName(name))) return true;
            return false;
        }

        private bool IsParameterList() {
            var save = p;
            try {
                p++;   // (
                if (Accept(")")) return Is("{") || Is(";") || Is("const") || Is("override") || Is("noexcept") || Is("->") || Is("final") || Is("=");
                if (Accept("void") && Accept(")")) return true;
                while (true) {
                    if (Accept("...")) { }
                    else {
                        var typeStart = p;
                        if (!TryType(out _)) return false;
                        if (IsId()) p++;
                        // V2: a lone name that's a function or a variable isn't a type: pros::Task counter(count_task);
                        // makes a task (it was taken for a function's declaration and the task never started).
                        else if (p == typeStart + 1 && t[typeStart].kind == "id" && IsKnownValue(t[typeStart].text)) return false;
                        else if (!Is(",") && !Is(")") && !Is("=") && !Is("[")) return false;
                        if (Accept("[")) { while (!Is("]")) p++; p++; }
                        if (Accept("=")) {
                            var depth = 0;
                            while (!(depth == 0 && (Is(",") || Is(")")))) {
                                if (Peek().kind == "eof") return false;
                                if (Is("(") || Is("{") || Is("[")) depth++;
                                if (Is(")") || Is("}") || Is("]")) depth--;
                                p++;
                            }
                        }
                    }
                    if (Accept(")")) return true;
                    if (!Accept(",")) return false;
                }
            }
            finally { p = save; }
        }

        private Stmt Variable(TypeInfo type, string rawName, CppToken nameTok, ClassInfo cls) {
            var name = PyName(rawName);
            var target = cls != null ? (Expr)At(new AttrExpr { target = At(new NameExpr { name = "self" }, nameTok), name = name }, nameTok) : At(new NameExpr { name = name }, nameTok);
            Expr value;
            if (Accept("[")) {   // an array: double speeds[4]; int a[] = {1, 2};
                Expr size = null;
                if (!Is("]")) size = Ternary();
                Expect("]");
                while (Accept("[")) { while (!Is("]")) p++; p++; }
                if (Accept("=")) value = Initializer(type, nameTok);
                else if (Is("{")) value = Initializer(type, nameTok);
                else value = size == null ? At(new ListExpr(), nameTok) : At(new BinExpr { op = "*", left = At(new ListExpr { items = { DefaultValue(new TypeInfo { kind = type.kind, name = type.name }, nameTok) } }, nameTok), right = size }, nameTok);
                type = new TypeInfo { kind = "vector", name = "array" };
            }
            else if (Accept("=")) value = Is("{") ? Construct(type, BraceItems(), nameTok) : Assignment();
            else if (Is("{")) value = Construct(type, BraceItems(), nameTok);
            else if (Is("(")) {
                var args = Arguments(out var kwNames, out var kwValues);
                value = Construct(type, (args, kwNames, kwValues), nameTok);
            }
            else if (Accept(":")) {   // a bit field
                Ternary();
                value = DefaultValue(type, nameTok);
            }
            else value = DefaultValue(type, nameTok);
            if (value is CAssign || value is CIncr) throw Error("an assignment can't be a variable's starting value here", nameTok);
            if (type.kind == "int" && !type.pointer && TypeOfExpr(value) != "int" && !(value is ConstExpr c && c.value is long)) value = Call("__cint", nameTok, value);
            Declare(name, type.pointer ? "class" : type.kind, cls);
            return At(new AssignStmt { targets = { target }, value = value }, nameTok);
        }

        private void Declare(string name, string kind, ClassInfo cls) {
            if (cls != null) {
                cls.members.Add(name);
                cls.types[name] = kind;
                return;
            }
            ctx.declared.Add(name);
            ctx.types[name] = kind;
        }

        // What a brace or = initializer makes for a type.
        private Expr Initializer(TypeInfo type, CppToken at) {
            if (Is("{")) {
                var (items, kwNames, kwValues) = BraceItems();
                return Construct(type, (items, kwNames, kwValues), at);
            }
            return Assignment();
        }

        private Expr Construct(TypeInfo type, (List<Expr> items, List<string> kwNames, List<Expr> kwValues) args, CppToken at) {
            if (type.kind == "vector" || type.kind == "auto" && args.kwNames.Count == 0 && args.items.Count != 1) {
                // std::vector<double> v(4, 0.0) means four zeros; {1, 2, 3} the items.
                var list = At(new ListExpr(), at);
                list.items.AddRange(args.items);
                return list;
            }
            if (type.kind is "int" or "float" or "bool" or "str" or "char" or "auto") {
                if (args.items.Count == 0) return DefaultValue(type, at);
                var v = args.items[0];
                return type.kind == "int" && TypeOfExpr(v) != "int" ? Call("__cint", at, v) : v;
            }
            if (type.kind == "map") return At(new DictExpr(), at);
            var call = At(new CallExpr { func = TypeExpr(type, at) }, at);
            call.args.AddRange(args.items);
            call.kwNames.AddRange(args.kwNames);
            call.kwValues.AddRange(args.kwValues);
            return call;
        }

        // { a, b } or { .x = 1, .y = 2 }.
        private (List<Expr>, List<string>, List<Expr>) BraceItems() {
            Expect("{");
            var items = new List<Expr>();
            var kwNames = new List<string>();
            var kwValues = new List<Expr>();
            while (!Accept("}")) {
                if (Is(".") && IsId(1) && Is("=", 2)) {
                    p++;
                    kwNames.Add(Next().text);   // as written ({.kP = 1} fills a struct's kP; the robot libraries take both spellings)
                    p++;
                    kwValues.Add(Is("{") ? BraceList() : Assignment());
                }
                else items.Add(Is("{") ? BraceList() : Assignment());
                if (!Accept(",")) {
                    Expect("}", "to close the braces");
                    break;
                }
            }
            return (items, kwNames, kwValues);
        }

        private Expr BraceList() {
            var at = Peek();
            var (items, kwNames, kwValues) = BraceItems();
            if (kwNames.Count > 0) {
                var d = At(new DictExpr(), at);
                for (var i = 0; i < kwNames.Count; i++) {
                    d.keys.Add(At(new ConstExpr { value = kwNames[i] }, at));
                    d.values.Add(kwValues[i]);
                }
                return d;
            }
            var list = At(new ListExpr(), at);
            list.items.AddRange(items);
            return list;
        }

        // ------------------------------------------------------------------ functions

        private List<Stmt> Function(TypeInfo returns, string rawName, CppToken nameTok, ClassInfo cls, string ownerClass) {
            var name = PyName(rawName);
            var owner = cls ?? (ownerClass != null && classes.TryGetValue(ownerClass, out var oc) ? oc : null);
            var def = At(new FuncDef { name = owner != null && cls == null ? "__" + ownerClass + "_" + name : name }, nameTok);
            var fctx = new Ctx { parent = ctx, def = def, cls = owner?.name };
            if (owner != null) def.parameters.Add("self");
            Parameters(def, fctx);
            while (IsId() && (Is("const") || Is("override") || Is("final") || Is("noexcept"))) p++;
            if (Accept("->")) TryType(out _);
            if (Accept("=")) {   // = 0, = default, = delete
                Next();
                Expect(";");
                return new List<Stmt>();
            }
            if (Accept(";")) {   // a prototype: the definition comes later
                if (cls == null && ownerClass == null) functions.Add(name);
                return new List<Stmt>();
            }
            if (cls == null && ownerClass == null) {
                functions.Add(name);
                module.declared.Add(name);
            }
            def.body = Body(fctx);
            var result = new List<Stmt> { def };
            if (owner != null && cls == null) {
                // Attached to its class: Robot.drive = __Robot_drive
                result.Add(At(new AssignStmt {
                    targets = { At(new AttrExpr { target = At(new NameExpr { name = ownerClass }, nameTok), name = name }, nameTok) },
                    value = At(new NameExpr { name = def.name }, nameTok),
                }, nameTok));
            }
            _ = returns;
            return result;
        }

        private void Parameters(FuncDef def, Ctx fctx) {
            Expect("(");
            if (Accept(")")) return;
            if (Is("void") && Is(")", 1)) { p += 2; return; }
            while (true) {
                if (Accept("...")) {
                    def.star = "args";
                    fctx.declared.Add("args");
                }
                else {
                    var ptok = Peek();
                    if (!TryType(out var ptype)) throw Error("expected a parameter's type");
                    var pname = IsId() ? PyName(Next().text) : "_p" + def.parameters.Count;
                    if (Accept("[")) { while (!Is("]")) p++; p++; ptype = new TypeInfo { kind = "vector" }; }
                    def.parameters.Add(pname);
                    fctx.declared.Add(pname);
                    fctx.types[pname] = ptype.pointer ? "class" : ptype.kind;
                    if (Accept("=")) {
                        var saved = ctx;
                        ctx = fctx;
                        def.defaults.Add(Is("{") ? BraceList() : Ternary());
                        ctx = saved;
                    }
                    else if (def.defaults.Count > 0) throw Error("a parameter without a default can't come after one with a default", ptok);
                }
                if (Accept(")")) return;
                Expect(",", "between parameters");
            }
        }

        // A function's body with its global / nonlocal names worked out.
        private List<Stmt> Body(Ctx fctx) {
            var saved = ctx;
            ctx = fctx;
            var open = Expect("{", "to start the function's body");
            var body = new List<Stmt>();
            while (!Is("}")) {
                if (Peek().kind == "eof") throw Error("this function's '{' is never closed", open);
                body.AddRange(Statement());
            }
            p++;
            ctx = saved;
            // C++ writes to a global (or a captured variable) from inside: Python needs to be told.
            var globals = new GlobalStmt();
            var nonlocals = new GlobalStmt { nonlocal = true };
            foreach (var n in fctx.assigned) {
                if (fctx.declared.Contains(n)) continue;
                var outer = fctx.parent;
                var found = false;
                while (outer != null && !outer.isModule) {
                    if (outer.declared.Contains(n)) { found = true; break; }
                    outer = outer.parent;
                }
                if (found) nonlocals.names.Add(n);
                else globals.names.Add(n);
            }
            if (nonlocals.names.Count > 0) body.Insert(0, At(nonlocals, open));
            if (globals.names.Count > 0) body.Insert(0, At(globals, open));
            if (body.Count == 0) body.Add(At(new PassStmt(), open));
            return body;
        }

        // ------------------------------------------------------------------ classes

        private List<Stmt> Class() {
            var tok = Next();
            var name = ExpectId("the class's name");
            var info = classes.TryGetValue(name, out var known) ? known : classes[name] = new ClassInfo { name = name };
            var def = At(new ClassDef { name = name, body = new List<Stmt>() }, tok);
            Accept("final");
            if (Accept(":")) {
                do {
                    while (Is("public") || Is("private") || Is("protected") || Is("virtual")) p++;
                    var bt = Peek();
                    TryType(out var btype);
                    def.bases.Add(TypeExpr(btype, bt));
                    info.baseName = btype.name;
                } while (Accept(","));
            }
            Expect("{", "to start the class");
            var fields = new List<(string name, Expr value, CppToken at)>();
            var ctors = new List<(FuncDef def, List<Stmt> inits, CppToken at)>();
            module.declared.Add(name);
            while (!Accept("}")) {
                if (Peek().kind == "eof") throw Error("this class is never closed", tok);
                if ((Is("public") || Is("private") || Is("protected")) && Is(":", 1)) { p += 2; continue; }
                if (Accept(";")) continue;
                if (Is("friend") || Is("using") || Is("typedef") || Is("static_assert")) {
                    while (!Is(";")) p++;
                    p++;
                    continue;
                }
                if (Is("template")) { p++; SkipTemplate(); continue; }
                if (Is("enum")) {
                    def.body.AddRange(Enum());
                    continue;
                }
                // Destructor: ignored.
                if (Is("~") || (Is("virtual") && Is("~", 1))) {
                    while (!Is("{") && !Is(";")) p++;
                    if (Is("{")) SkipBalanced("{", "}");
                    else p++;
                    continue;
                }
                // Constructor: Name(params) : inits { body }
                var save = p;
                while (Is("explicit") || Is("inline") || Is("constexpr")) p++;
                if (Is(name) && Is("(", 1)) {
                    var ctok = Next();
                    var cdef = At(new FuncDef { name = "__init__" }, ctok);
                    var fctx = new Ctx { parent = ctx, def = cdef, cls = name };
                    cdef.parameters.Add("self");
                    Parameters(cdef, fctx);
                    while (Is("noexcept")) p++;
                    var inits = new List<Stmt>();
                    if (Accept(":")) {
                        var saved = ctx;
                        ctx = fctx;
                        do {
                            var itok = Peek();
                            var member = ExpectId("a member to start");
                            (List<Expr> items, List<string> kn, List<Expr> kv) args;
                            if (Is("{")) args = BraceItems();
                            else {
                                var a = Arguments(out var kn, out var kv);
                                args = (a, kn, kv);
                            }
                            if (member == info.baseName || def.bases.Count > 0 && member == (def.bases[0] as NameExpr)?.name) {
                                // Base(args): Base.__init__(self, args)
                                var call = At(new CallExpr { func = At(new AttrExpr { target = def.bases[0], name = "__init__" }, itok) }, itok);
                                call.args.Add(At(new NameExpr { name = "self" }, itok));
                                call.args.AddRange(args.items);
                                inits.Add(At(new ExprStmt { expr = call }, itok));
                            }
                            else {
                                var kind = info.types.TryGetValue(PyName(member), out var k) ? k : "class";
                                Expr v = args.items.Count == 1 && kind != "class" ? args.items[0] : kind == "vector" ? (Expr)new ListExpr { items = args.items } : args.items.Count == 1 ? args.items[0] : args.items.Count == 0 ? (Expr)At(new ConstExpr { value = PyNone.Value }, itok) : args.items[0];
                                inits.Add(At(new AssignStmt { targets = { At(new AttrExpr { target = At(new NameExpr { name = "self" }, itok), name = PyName(member) }, itok) }, value = v }, itok));
                            }
                        } while (Accept(","));
                        ctx = saved;
                    }
                    if (Accept(";")) continue;
                    if (Accept("=")) { Next(); Expect(";"); continue; }
                    cdef.body = Body(fctx);
                    ctors.Add((cdef, inits, ctok));
                    continue;
                }
                p = save;
                // Field(s) or method.
                var saveCtx = ctx;
                ctx = new Ctx { parent = saveCtx, cls = name };
                var member0 = Peek();
                var isStatic = Is("static") || Is("static", 1) || Is("static", 2);
                if (!TryType(out var mtype) || !IsId()) throw Error("this isn't a field or a method", member0);
                var mtok = Peek();
                var mname = Next().text;
                if (Is("(")) {
                    var mdef = At(new FuncDef { name = PyName(mname) }, mtok);
                    var fctx = new Ctx { parent = saveCtx, def = mdef, cls = name };
                    if (!isStatic) mdef.parameters.Add("self");
                    Parameters(mdef, fctx);
                    while (Is("const") || Is("override") || Is("final") || Is("noexcept")) p++;
                    if (Accept("->")) TryType(out _);
                    ctx = saveCtx;
                    if (Accept("=")) { Next(); Expect(";"); continue; }
                    if (Accept(";")) continue;
                    mdef.body = Body(fctx);
                    if (isStatic) mdef.decorators.Add(At(new NameExpr { name = "staticmethod" }, mtok));
                    def.body.Add(mdef);
                    info.members.Add(mdef.name);
                    continue;
                }
                ctx = saveCtx;
                while (true) {
                    var fname = PyName(mname);
                    Expr value;
                    var fieldType = mtype;
                    if (Accept("[")) {
                        Expr size = Is("]") ? null : Ternary();
                        Expect("]");
                        value = Is("=") || Is("{") ? (Accept("=") || true ? Initializer(mtype, mtok) : null) : size == null ? At(new ListExpr(), mtok) : At(new BinExpr { op = "*", left = At(new ListExpr { items = { DefaultValue(mtype, mtok) } }, mtok), right = size }, mtok);
                        fieldType = new TypeInfo { kind = "vector" };
                    }
                    else if (Accept("=")) value = Is("{") ? Construct(mtype, BraceItems(), mtok) : Assignment();
                    else if (Is("{")) value = Construct(mtype, BraceItems(), mtok);
                    else value = DefaultValue(mtype, mtok);
                    info.members.Add(fname);
                    info.types[fname] = fieldType.pointer ? "class" : fieldType.kind;
                    if (isStatic) def.body.Add(At(new AssignStmt { targets = { At(new NameExpr { name = fname }, mtok) }, value = value }, mtok));
                    else fields.Add((fname, value, mtok));
                    if (!Accept(",")) break;
                    mtok = Peek();
                    mname = ExpectId("a field name");
                }
                Expect(";", "after the field");
            }
            // Declarators after the class: struct Pid { ... } pid;
            var after = new List<Stmt>();
            while (IsId()) {
                var vtok = Next();
                after.Add(At(new AssignStmt { targets = { At(new NameExpr { name = PyName(vtok.text) }, vtok) }, value = At(new CallExpr { func = At(new NameExpr { name = name }, vtok) }, vtok) }, vtok));
                ctx.declared.Add(PyName(vtok.text));
                Accept(",");
            }
            Expect(";", "after the class's '}'");

            // __init__: fields' starting values, then the constructor (by its number of arguments when there are
            // several); without a constructor the fields are its (optional) arguments, so Pid{1, 0, 2} works.
            var initTok = tok;
            List<Stmt> FieldInits() => fields.Select(f => (Stmt)At(new AssignStmt { targets = { At(new AttrExpr { target = At(new NameExpr { name = "self" }, f.at), name = f.name }, f.at) }, value = f.value }, f.at)).ToList();
            if (ctors.Count == 0) {
                var init = At(new FuncDef { name = "__init__", body = new List<Stmt>() }, initTok);
                init.parameters.Add("self");
                init.star = "__args";
                init.starStar = "__kw";
                init.body.AddRange(FieldInits());
                // Aggregate init: positional values fill the fields in order, keywords by name.
                init.body.Add(At(new ExprStmt { expr = Call("__aggregate", initTok, At(new NameExpr { name = "self" }, initTok),
                    At(new ListExpr { items = fields.Select(f => (Expr)At(new ConstExpr { value = f.name }, f.at)).ToList() }, initTok),
                    At(new NameExpr { name = "__args" }, initTok), At(new NameExpr { name = "__kw" }, initTok)) }, initTok));
                def.body.Insert(0, init);
            }
            else if (ctors.Count == 1) {
                var (cdef, inits, _) = ctors[0];
                var body = FieldInits();
                body.AddRange(inits);
                body.AddRange(cdef.body);
                cdef.body = body;
                def.body.Insert(0, cdef);
            }
            else {
                // Overloads by argument count.
                var init = At(new FuncDef { name = "__init__", body = new List<Stmt>() }, initTok);
                init.parameters.Add("self");
                init.star = "__args";
                init.body.AddRange(FieldInits());
                IfStmt chain = null, last = null;
                for (var i = 0; i < ctors.Count; i++) {
                    var (cdef, inits, ctok) = ctors[i];
                    cdef.name = "__init" + i;
                    var body = new List<Stmt>(inits);
                    body.AddRange(cdef.body);
                    cdef.body = body;
                    def.body.Add(cdef);
                    var count = cdef.parameters.Count - 1;
                    var test = At(new CompareExpr { first = Call("len", ctok, At(new NameExpr { name = "__args" }, ctok)), ops = { "==" }, rest = { At(new ConstExpr { value = (long)count }, ctok) } }, ctok);
                    var call = At(new CallExpr { func = At(new AttrExpr { target = At(new NameExpr { name = "self" }, ctok), name = cdef.name }, ctok), starArgs = At(new NameExpr { name = "__args" }, ctok) }, ctok);
                    var branch = At(new IfStmt { test = test, body = new List<Stmt> { At(new ExprStmt { expr = call }, ctok) }, orElse = new List<Stmt>() }, ctok);
                    if (chain == null) chain = branch;
                    else last.orElse.Add(branch);
                    last = branch;
                }
                init.body.Add(chain);
                def.body.Insert(0, init);
            }
            var result = new List<Stmt> { def };
            result.AddRange(after);
            return result;
        }

        // ------------------------------------------------------------------ statements

        private List<Stmt> Statement() => WithHoist(StatementInner);

        private List<Stmt> StatementInner() {
            var tok = Peek();
            if (Accept(";")) return new List<Stmt>();
            if (Is("{")) return Block();
            if (IsId()) {
                switch (tok.text) {
                    case "if": return new List<Stmt> { If() };
                    case "while": {
                        p++;
                        Expect("(");
                        var test = Condition();
                        Expect(")");
                        return new List<Stmt> { At(new WhileStmt { test = test, body = Loop(Statement), orElse = new List<Stmt>() }, tok) };
                    }
                    case "do": return DoWhile();
                    case "for": return For();
                    case "switch": return Switch();
                    case "return": {
                        p++;
                        Expr value = Is(";") ? null : Is("{") ? BraceList() : Assignment();
                        Expect(";", "after return");
                        return new List<Stmt> { At(new ReturnStmt { value = value }, tok) };
                    }
                    case "break": p++; Expect(";"); return new List<Stmt> { At(new BreakStmt(), tok) };
                    case "continue": p++; Expect(";"); return new List<Stmt> { At(new ContinueStmt(), tok) };
                    case "goto": throw Error("goto isn't supported");
                    case "try": return Try();
                    case "throw": {
                        p++;
                        var e = Is(";") ? null : Assignment();
                        Expect(";");
                        return new List<Stmt> { At(new RaiseStmt { exc = e == null ? null : Call("RuntimeError", tok, Call("str", tok, e)) }, tok) };
                    }
                    case "using": return Using();
                    case "typedef": while (!Is(";")) p++; p++; return new List<Stmt>();
                    case "static_assert": while (!Is(";")) p++; p++; return new List<Stmt>();
                    case "enum": return Enum();
                    case "struct":
                    case "class":
                        if (IsId(1) && (Is("{", 2) || Is(":", 2))) return Class();
                        break;
                }
            }
            var decl = Declaration(false);
            if (decl != null) return decl;
            var stmt = ExpressionStatement();
            Expect(";", "at the end of the statement");
            return stmt;
        }

        private List<Stmt> Block() {
            var open = Expect("{");
            var body = new List<Stmt>();
            while (!Accept("}")) {
                if (Peek().kind == "eof") throw Error("this '{' is never closed", open);
                body.AddRange(Statement());
            }
            return body;
        }

        // A loop's body (a block or one statement).
        private List<Stmt> Loop(Func<List<Stmt>> body) {
            var b = body();
            if (b.Count == 0) b.Add(new PassStmt());
            return b;
        }

        private Expr Condition() {
            // if (auto x = f()) / while (int n = ...) aren't supported; plain expressions are.
            var e = Assignment();
            if (e is CAssign || e is CIncr) throw Error("an assignment inside a condition isn't supported: do it on the line before");
            return e;
        }

        private Stmt If() {
            var tok = Next();
            Accept("constexpr");
            Expect("(", "after if");
            var test = Condition();
            Expect(")", "to close the if's condition");
            var body = Loop(Statement);
            var orElse = new List<Stmt>();
            if (Accept("else")) orElse = Statement();
            return At(new IfStmt { test = test, body = body, orElse = orElse }, tok);
        }

        private List<Stmt> DoWhile() {
            var tok = Next();
            var body = Statement();
            Expect("while", "after do's body");
            Expect("(");
            var test = Condition();
            Expect(")");
            Expect(";");
            // while True: body; if not test: break   (continue checks the test too)
            Stmt Exit() => At(new IfStmt { test = At(new UnaryExpr { op = "not", operand = test }, tok), body = new List<Stmt> { At(new BreakStmt(), tok) }, orElse = new List<Stmt>() }, tok);
            body = ReplaceContinue(body, () => new List<Stmt> { Exit(), At(new ContinueStmt(), tok) });
            body.Add(Exit());
            return new List<Stmt> { At(new WhileStmt { test = At(new ConstExpr { value = true }, tok), body = body, orElse = new List<Stmt>() }, tok) };
        }

        private List<Stmt> For() {
            var tok = Next();
            Expect("(", "after for");
            // Range for: for (auto x : items)
            var save = p;
            if (TryType(out var rtype) && IsId()) {
                var vtok = Peek();
                var name = PyName(Next().text);
                if (Accept(":")) {
                    var iter = Assignment();
                    Expect(")");
                    ctx.declared.Add(name);
                    ctx.types[name] = rtype.kind;
                    var body = Loop(Statement);
                    return new List<Stmt> { At(new ForStmt { target = At(new NameExpr { name = name }, vtok), iter = iter, body = body, orElse = new List<Stmt>() }, tok) };
                }
            }
            p = save;
            var result = new List<Stmt>();
            if (!Accept(";")) {
                var decl = Declaration(false);
                if (decl != null) result.AddRange(decl);
                else {
                    result.AddRange(ExpressionStatements());
                    Expect(";", "after the for loop's start");
                }
            }
            Expr test = Is(";") ? At(new ConstExpr { value = true }, tok) : Condition();
            Expect(";", "after the for loop's condition");
            var step = new List<Stmt>();
            if (!Is(")")) step.AddRange(ExpressionStatements());
            Expect(")", "to close the for loop's parentheses");
            var loopBody = Loop(Statement);
            // The step runs after the body and before every continue.
            loopBody = ReplaceContinue(loopBody, () => { var s = new List<Stmt>(step); s.Add(At(new ContinueStmt(), tok)); return s; });
            loopBody.AddRange(step);
            result.Add(At(new WhileStmt { test = test, body = loopBody, orElse = new List<Stmt>() }, tok));
            return result;
        }

        // continue in this loop (not in loops inside it) becomes what's given.
        private static List<Stmt> ReplaceContinue(List<Stmt> body, Func<List<Stmt>> with) {
            var result = new List<Stmt>();
            foreach (var s in body) {
                switch (s) {
                    case ContinueStmt:
                        result.AddRange(with());
                        break;
                    case IfStmt i:
                        i.body = ReplaceContinue(i.body, with);
                        i.orElse = ReplaceContinue(i.orElse, with);
                        result.Add(i);
                        break;
                    case TryStmt tr:
                        tr.body = ReplaceContinue(tr.body, with);
                        for (var k = 0; k < tr.handlers.Count; k++) {
                            var h = tr.handlers[k];
                            tr.handlers[k] = (h.type, h.name, ReplaceContinue(h.body, with), h.line);
                        }
                        result.Add(tr);
                        break;
                    default:
                        result.Add(s);
                        break;
                }
            }
            return result;
        }

        private List<Stmt> Switch() {
            var tok = Next();
            Expect("(");
            var value = Assignment();
            Expect(")");
            Expect("{", "to start the switch");
            var groups = new List<(List<Expr> labels, bool isDefault, List<Stmt> body, CppToken at)>();
            List<Expr> labels = null;
            var isDefault = false;
            List<Stmt> current = null;
            CppToken at = tok;
            while (!Accept("}")) {
                if (Peek().kind == "eof") throw Error("this switch is never closed", tok);
                if (Is("case") || Is("default")) {
                    if (current != null && current.Count > 0) {
                        groups.Add((labels, isDefault, current, at));
                        labels = null;
                        isDefault = false;
                        current = null;
                    }
                    labels ??= new List<Expr>();
                    current ??= new List<Stmt>();
                    at = Peek();
                    if (Accept("default")) isDefault = true;
                    else {
                        p++;
                        labels.Add(Ternary());
                    }
                    Expect(":", "after the case");
                    continue;
                }
                if (current == null) throw Error("a switch's statements go after a case");
                current.AddRange(Statement());
            }
            if (current != null) groups.Add((labels, isDefault, current, at));
            // A temporary holds the value; the cases are an if / elif chain. Each case runs into the next one until a
            // break, as in C++.
            var temp = "__switch" + temps++;
            var result = new List<Stmt> { At(new AssignStmt { targets = { At(new NameExpr { name = temp }, tok) }, value = value }, tok) };
            ctx.declared.Add(temp);
            IfStmt chain = null, last = null;
            List<Stmt> defaultBody = null;
            for (var g = 0; g < groups.Count; g++) {
                var body = new List<Stmt>();
                for (var k = g; k < groups.Count; k++) {
                    var (stopped, part) = UntilBreak(groups[k].body);
                    body.AddRange(part);
                    if (stopped) break;
                }
                if (groups[g].isDefault && groups[g].labels.Count == 0) {
                    defaultBody = body;
                    continue;
                }
                Expr test = null;
                foreach (var label in groups[g].labels) {
                    var cmp = At(new CompareExpr { first = At(new NameExpr { name = temp }, groups[g].at), ops = { "==" }, rest = { label } }, groups[g].at);
                    test = test == null ? (Expr)cmp : At(new BoolExpr { isAnd = false, left = test, right = cmp }, groups[g].at);
                }
                if (body.Count == 0) body.Add(new PassStmt());
                var branch = At(new IfStmt { test = test, body = body, orElse = new List<Stmt>() }, groups[g].at);
                if (chain == null) chain = branch;
                else last.orElse.Add(branch);
                last = branch;
                if (groups[g].isDefault) defaultBody = body;
            }
            if (chain == null) {
                if (defaultBody != null) result.AddRange(defaultBody);
            }
            else {
                if (defaultBody != null) last.orElse.AddRange(defaultBody);
                result.Add(chain);
            }
            return result;
        }

        // A case's statements up to its break (true: it stops there). A break inside an if in a case isn't supported.
        private (bool, List<Stmt>) UntilBreak(List<Stmt> body) {
            var part = new List<Stmt>();
            foreach (var s in body) {
                if (s is BreakStmt) return (true, part);
                if (ContainsBreak(s)) throw new SyntaxErr("a break inside an if in a switch case isn't supported: end the case with break", s.line, s.column);
                part.Add(s);
                if (s is ReturnStmt || s is ContinueStmt || s is RaiseStmt) return (true, part);
            }
            return (false, part);
        }

        private static bool ContainsBreak(Stmt s) => s switch {
            BreakStmt => true,
            IfStmt i => i.body.Any(ContainsBreak) || i.orElse.Any(ContainsBreak),
            _ => false,
        };

        private List<Stmt> Try() {
            var tok = Next();
            var body = Block();
            var tr = At(new TryStmt { body = body, orElse = new List<Stmt>(), final = null }, tok);
            while (Accept("catch")) {
                Expect("(");
                string name = null;
                if (Accept("...")) { }
                else {
                    TryType(out _);
                    if (IsId()) name = PyName(Next().text);
                }
                Expect(")");
                var hbody = Block();
                if (hbody.Count == 0) hbody.Add(new PassStmt());
                tr.handlers.Add((null, name, hbody, tok.line));
            }
            if (tr.handlers.Count == 0) throw Error("try needs a catch");
            return new List<Stmt> { tr };
        }

        private List<Stmt> ExpressionStatements() {
            var result = new List<Stmt>();
            do result.AddRange(ExpressionStatement()); while (Accept(","));
            return result;
        }

        private List<Stmt> ExpressionStatement() {
            var tok = Peek();
            var e = Assignment();
            switch (e) {
                case CAssign a: return new List<Stmt> { AssignStatement(a, tok) };
                case CIncr i: return new List<Stmt> { AssignStatement(new CAssign { target = i.target, op = i.delta > 0 ? "+=" : "-=", value = At(new ConstExpr { value = 1L }, tok) }, tok) };
            }
            // std::cout << a << b << std::endl;
            var chain = new List<Expr>();
            var cur = e;
            while (cur is BinExpr b && b.op == "<<") {
                chain.Insert(0, b.right);
                cur = b.left;
            }
            if (chain.Count > 0 && IsCout(cur)) {
                var call = At(new CallExpr { func = At(new NameExpr { name = "__cout" }, tok) }, tok);
                call.args.AddRange(chain);
                return new List<Stmt> { At(new ExprStmt { expr = call }, tok) };
            }
            return new List<Stmt> { At(new ExprStmt { expr = e }, tok) };
        }

        private static bool IsCout(Expr e) =>
            e is NameExpr n && (n.name == "cout" || n.name == "cerr") || e is AttrExpr a && a.target is NameExpr s && s.name == "std" && (a.name == "cout" || a.name == "cerr");

        private Stmt AssignStatement(CAssign a, CppToken tok) {
            if (a.target is NameExpr n) ctx.assigned.Add(n.name);
            var targetKind = TypeOfExpr(a.target);
            if (a.op == "=") {
                var v = a.value;
                if (targetKind == "int" && TypeOfExpr(v) != "int") v = Call("__cint", tok, v);
                return At(new AssignStmt { targets = { a.target }, value = v }, tok);
            }
            var op = a.op.Substring(0, a.op.Length - 1);
            // x /= y on numbers keeps C++'s rules (int / int truncates); an int target stays an int.
            var combined = Binary(op, a.target, a.value, tok);
            if (op == "/" || op == "%" || (targetKind == "int" && TypeOfExpr(a.value) != "int" && (op == "+" || op == "-" || op == "*"))) {
                if (targetKind == "int") combined = Call("__cint", tok, combined);
                return At(new AssignStmt { targets = { a.target }, value = combined }, tok);
            }
            return At(new AugAssignStmt { target = a.target, op = op, value = a.value }, tok);
        }

        // ------------------------------------------------------------------ expressions

        private Expr Assignment() {
            var tok = Peek();
            var left = Ternary();
            if (Peek().kind == "op" && Peek().text is "=" or "+=" or "-=" or "*=" or "/=" or "%=" or "&=" or "|=" or "^=" or "<<=" or ">>=") {
                var op = Next().text;
                var right = Is("{") ? BraceList() : Assignment();
                if (right is CAssign) throw Error("chained assignments (a = b = c) aren't supported", tok);
                if (!(left is NameExpr || left is AttrExpr || left is IndexExpr)) throw Error("this can't be assigned to", tok);
                return At(new CAssign { target = left, op = op, value = right }, tok);
            }
            return left;
        }

        private Expr Ternary() {
            var tok = Peek();
            var cond = Binary(0);
            if (!Accept("?")) return cond;
            var a = Assignment();
            Expect(":", "in the ? : expression");
            var b = Assignment();
            return At(new IfExpr { test = cond, body = a, orElse = b }, tok);
        }

        private static readonly string[][] Levels = {
            new[] { "||" }, new[] { "&&" }, new[] { "|" }, new[] { "^" }, new[] { "&" }, new[] { "==", "!=" },
            new[] { "<", "<=", ">", ">=" }, new[] { "<<", ">>" }, new[] { "+", "-" }, new[] { "*", "/", "%" },
        };

        private Expr Binary(int level) {
            if (level >= Levels.Length) return Unary();
            var left = Binary(level + 1);
            while (Peek().kind == "op" && Array.IndexOf(Levels[level], Peek().text) >= 0) {
                var tok = Next();
                var right = Binary(level + 1);
                left = Binary(tok.text, left, right, tok);
            }
            return left;
        }

        private Expr Binary(string op, Expr left, Expr right, CppToken tok) {
            Check(left, tok);
            Check(right, tok);
            switch (op) {
                case "||": return At(new BoolExpr { isAnd = false, left = left, right = right }, tok);
                case "&&": return At(new BoolExpr { isAnd = true, left = left, right = right }, tok);
                case "==": case "!=": case "<": case "<=": case ">": case ">=":
                    return At(new CompareExpr { first = left, ops = { op }, rest = { right } }, tok);
                case "/":
                    // C++: int / int truncates (7 / 2 is 3). Known doubles divide as usual.
                    if (TypeOfExpr(left) == "float" || TypeOfExpr(right) == "float") return At(new BinExpr { op = "/", left = left, right = right }, tok);
                    return Call("__cdiv", tok, left, right);
                case "%":
                    return Call("__cmod", tok, left, right);
                default:
                    return At(new BinExpr { op = op, left = left, right = right }, tok);
            }
        }

        private void Check(Expr e, CppToken tok) {
            if (e is CAssign) throw Error("an assignment inside an expression isn't supported: put it on its own line", tok);
            if (e is CIncr) throw Error("++ or -- inside an expression isn't supported: put it on its own line", tok);
        }

        private Expr Unary() {
            var tok = Peek();
            if (tok.kind == "op") {
                switch (tok.text) {
                    case "!": p++; return At(new UnaryExpr { op = "not", operand = Checked(Unary(), tok) }, tok);
                    case "-": p++; {
                        var o = Checked(Unary(), tok);
                        if (o is ConstExpr c && c.value is long l) return At(new ConstExpr { value = -l }, tok);
                        if (o is ConstExpr d && d.value is double dv) return At(new ConstExpr { value = -dv }, tok);
                        return At(new UnaryExpr { op = "-", operand = o }, tok);
                    }
                    case "+": p++; return Checked(Unary(), tok);
                    case "~": p++; return At(new UnaryExpr { op = "~", operand = Checked(Unary(), tok) }, tok);
                    case "++": case "--": p++; return At(new CIncr { target = Unary(), delta = tok.text == "++" ? 1 : -1 }, tok);
                    case "*": p++; return Checked(Unary(), tok);   // a pointer's object is the object
                    case "&": p++; return Checked(Unary(), tok);   // its address: the object itself
                    case "(":
                        // A cast: (int)x, (double)y
                        if (IsId(1) && (KindOf(Peek(1).text) is "int" or "float" or "bool" or "char" || Peek(1).text is "unsigned" or "long" or "short" or "signed")) {
                            var save = p;
                            p++;
                            if (TryType(out var ct) && Accept(")") && !Is(")") && !Is(";") && !Is(",")) return Cast(ct, Unary(), tok);
                            p = save;
                        }
                        break;
                }
            }
            if (tok.kind == "id") {
                switch (tok.text) {
                    case "sizeof": throw Error("sizeof isn't supported (use .size() for a vector)");
                    case "new": {
                        p++;
                        var nt = Peek();
                        TryType(out var type);
                        if (Is("(")) {
                            var args = Arguments(out var kn, out var kv);
                            return Construct(type, (args, kn, kv), nt);
                        }
                        if (Is("{")) return Construct(type, BraceItems(), nt);
                        if (Accept("[")) { var n = Ternary(); Expect("]"); return At(new BinExpr { op = "*", left = At(new ListExpr { items = { DefaultValue(type, nt) } }, nt), right = n }, nt); }
                        return DefaultValue(type, nt);
                    }
                    case "delete":
                        p++;
                        if (Accept("[")) Expect("]");
                        Unary();
                        return At(new ConstExpr { value = PyNone.Value }, tok);
                }
            }
            return Postfix(Primary());
        }

        private Expr Checked(Expr e, CppToken tok) {
            Check(e, tok);
            return e;
        }

        private Expr Cast(TypeInfo type, Expr value, CppToken tok) => type.kind switch {
            "int" => Call("__cint", tok, value),
            "float" => Call("float", tok, value),
            "bool" => Call("bool", tok, value),
            "str" => Call("str", tok, value),
            "char" => Call("__cchar", tok, value),
            _ => value,
        };

        private Expr Postfix(Expr e) {
            while (true) {
                var tok = Peek();
                if (Is("(")) {
                    var args = Arguments(out var kwNames, out var kwValues);
                    e = MakeCall(e, args, kwNames, kwValues, tok);
                }
                else if (Accept("[")) {
                    var index = Assignment();
                    Expect("]");
                    e = At(new IndexExpr { target = e, index = index }, tok);
                }
                else if (Accept(".") || Accept("->")) {
                    if (Accept("template")) { }
                    var name = ExpectId("a member's name");
                    e = At(new AttrExpr { target = e, name = PyName(name) }, tok);
                    if (Is("<") && name is "get" or "as") SkipTemplate();
                }
                else if (Is("++") || Is("--")) {
                    p++;
                    e = At(new CIncr { target = e, delta = tok.text == "++" ? 1 : -1 }, tok);
                }
                else return e;
            }
        }

        private List<Expr> Arguments(out List<string> kwNames, out List<Expr> kwValues) {
            Expect("(");
            var args = new List<Expr>();
            kwNames = new List<string>();
            kwValues = new List<Expr>();
            while (!Accept(")")) {
                if (Is("{")) {
                    // LemLib's options: {.forwards = false, .maxSpeed = 60} -> forwards=False, max_speed=60; {} -> nothing.
                    var (items, kn, kv) = BraceItems();
                    if (kn.Count > 0) {
                        kwNames.AddRange(kn);
                        kwValues.AddRange(kv);
                    }
                    else if (items.Count > 0) args.Add(At(new ListExpr { items = items }, Peek()));
                }
                else {
                    var a = Assignment();
                    if (a is CAssign || a is CIncr) throw Error("an assignment or ++ can't be an argument: do it on the line before");
                    args.Add(a);
                }
                if (Accept(")")) break;
                Expect(",", "between arguments");
            }
            return args;
        }

        private Expr MakeCall(Expr func, List<Expr> args, List<string> kwNames, List<Expr> kwValues, CppToken tok) {
            // printf / std::to_string / casts / C++ container calls the interpreter doesn't have.
            if (func is NameExpr n) {
                switch (n.name) {
                    case "printf": func = At(new NameExpr { name = "__printf" }, tok); break;
                    case "to_string": func = At(new NameExpr { name = "__to_string" }, tok); break;
                }
            }
            else if (func is AttrExpr a && a.target is NameExpr s && s.name == "std") {
                switch (a.name) {
                    case "printf": func = At(new NameExpr { name = "__printf" }, tok); break;
                    case "to_string": func = At(new NameExpr { name = "__to_string" }, tok); break;
                }
            }
            var call = At(new CallExpr { func = func }, tok);
            call.args.AddRange(args);
            call.kwNames.AddRange(kwNames);
            call.kwValues.AddRange(kwValues);
            return call;
        }

        private Expr Primary() {
            var tok = Peek();
            switch (tok.kind) {
                case "num": p++; return At(new ConstExpr { value = tok.value }, tok);
                case "str": {
                    p++;
                    var s = (string)tok.value;
                    while (Peek().kind == "str") s += (string)Next().value;   // "a" "b" joins
                    return At(new ConstExpr { value = s }, tok);
                }
                case "chr": p++; return At(new ConstExpr { value = tok.value }, tok);
                case "eof": throw Error("the program ends in the middle of an expression");
            }
            if (Accept("(")) {
                var e = Assignment();
                Expect(")", "to close the parentheses");
                return e;
            }
            if (Is("[")) return Lambda();
            if (Is("{")) return BraceList();
            if (tok.kind == "id") {
                switch (tok.text) {
                    case "true": p++; return At(new ConstExpr { value = true }, tok);
                    case "false": p++; return At(new ConstExpr { value = false }, tok);
                    case "nullptr": case "NULL": p++; return At(new ConstExpr { value = PyNone.Value }, tok);
                    case "this": p++; return At(new NameExpr { name = "self" }, tok);
                    case "static_cast": case "dynamic_cast": case "reinterpret_cast": case "const_cast": {
                        p++;
                        Expect("<");
                        TryType(out var type);
                        if (Is(">>")) { t[p] = new CppToken { kind = "op", text = ">", line = Peek().line, col = Peek().col }; t.Insert(p + 1, new CppToken { kind = "op", text = ">", line = Peek().line, col = Peek().col + 1 }); }
                        Expect(">");
                        Expect("(");
                        var v = Assignment();
                        Expect(")");
                        return Cast(type, v, tok);
                    }
                }
                // A name, maybe qualified (pros::delay, lemlib::Omniwheel::NEW_4, std::abs), maybe a type with a
                // template argument used as a constructor (std::vector<int>{1, 2}).
                var parts = new List<string> { Next().text };
                if (Is("<") && TemplateNamed(parts[0]) && LooksLikeTemplate()) SkipTemplate();
                while (Is("::") && IsId(1)) {
                    p++;
                    parts.Add(Next().text);
                    if (Is("<") && TemplateNamed(string.Join("::", parts)) && LooksLikeTemplate()) SkipTemplate();
                }
                var kind = KindOf(string.Join("::", parts));
                if (kind is "vector" or "str" or "map" && (Is("{") || Is("("))) {
                    var type = new TypeInfo { name = string.Join("::", parts), kind = kind };
                    if (Is("{")) return Construct(type, BraceItems(), tok);
                    var args = Arguments(out var kn, out var kv);
                    if (kind == "vector" && args.Count == 2) return At(new BinExpr { op = "*", left = At(new ListExpr { items = { args[1] } }, tok), right = args[0] }, tok);
                    return Construct(type, (args, kn, kv), tok);
                }
                if (parts.Count == 1 && classes.ContainsKey(parts[0]) && Is("{")) {
                    var (items, kn, kv) = BraceItems();
                    var call = At(new CallExpr { func = At(new NameExpr { name = parts[0] }, tok) }, tok);
                    call.args.AddRange(items);
                    call.kwNames.AddRange(kn);
                    call.kwValues.AddRange(kv);
                    return call;
                }
                return Qualified(parts, tok);
            }
            throw Error("'" + tok.text + "' can't start an expression");
        }

        // After a name: is "<" a template argument list (std::vector<int>) rather than less-than?
        private bool LooksLikeTemplate() {
            var save = p;
            try {
                SkipTemplate();
                return Is("(") || Is("{") || Is("::") || IsId() || Is(">");
            }
            catch (SyntaxErr) { return false; }
            finally { p = save; }
        }

        private Expr Qualified(List<string> parts, CppToken tok) {
            // Your own namespaces just hold names; std:: and the robot libraries are modules.
            var i = 0;
            while (i < parts.Count - 1 && namespaces.Contains(parts[i])) i++;
            if (i > 0 || parts.Count == 1) {
                var name = PyName(parts[i]);
                Expr e = MemberOrName(name, tok);
                for (var k = i + 1; k < parts.Count; k++) e = At(new AttrExpr { target = e, name = PyName(parts[k]) }, tok);
                return e;
            }
            Expr result = At(new NameExpr { name = parts[0] == "std" ? "std" : PyName(parts[0]) }, tok);
            for (var k = 1; k < parts.Count; k++) {
                // pros::v5::Motor, pros::c:: -> pros
                if ((parts[0] == "pros" && (parts[k] == "v5" || parts[k] == "c" || parts[k] == "v5::adi")) || (parts[k] == "std" && k == 0)) continue;
                result = At(new AttrExpr { target = result, name = parts[k] }, tok);
            }
            return result;
        }

        // A bare name inside a method may be a member of its class (this->x without the this->).
        private Expr MemberOrName(string name, CppToken tok) {
            var c = ctx;
            while (c != null && !c.isModule) {
                if (c.declared.Contains(name)) return At(new NameExpr { name = name }, tok);
                if (c.cls != null && classes.TryGetValue(c.cls, out var info) && IsMember(info, name))
                    return At(new AttrExpr { target = At(new NameExpr { name = "self" }, tok), name = name }, tok);
                c = c.parent;
            }
            return At(new NameExpr { name = name }, tok);
        }

        private bool IsMember(ClassInfo info, string name) {
            for (var depth = 0; info != null && depth < 8; depth++) {
                if (info.members.Contains(name)) return true;
                info = info.baseName != null && classes.TryGetValue(info.baseName, out var b) ? b : null;
            }
            return false;
        }

        private Expr Lambda() {
            var tok = Expect("[");
            while (!Accept("]")) p++;   // captures: everything is reachable
            var def = At(new FuncDef { name = "__lambda" + lambdas++ }, tok);
            var fctx = new Ctx { parent = ctx, def = def, cls = ctx.cls };
            if (Is("(")) Parameters(def, fctx);
            while (Is("mutable") || Is("noexcept") || Is("constexpr")) p++;
            if (Accept("->")) TryType(out _);
            def.body = Body(fctx);
            hoist.Add(def);
            ctx.declared.Add(def.name);
            return At(new NameExpr { name = def.name }, tok);
        }

        // ------------------------------------------------------------------ helpers

        // What a value is, when it's plain to see (int: C++ integer rules apply).
        private string TypeOfExpr(Expr e) {
            switch (e) {
                case ConstExpr c: return c.value is long ? "int" : c.value is double ? "float" : c.value is bool ? "bool" : c.value is string ? "str" : null;
                case NameExpr n: {
                    for (var c = ctx; c != null; c = c.parent)
                        if (c.types.TryGetValue(n.name, out var k)) return k;
                    return module.types.TryGetValue(n.name, out var m) ? m : null;
                }
                case AttrExpr a when a.target is NameExpr s && s.name == "self": {
                    for (var c = ctx; c != null; c = c.parent)
                        if (c.cls != null && classes.TryGetValue(c.cls, out var info) && info.types.TryGetValue(a.name, out var k)) return k;
                    return null;
                }
                case BinExpr b when b.op is "+" or "-" or "*" or "<<" or ">>" or "&" or "|" or "^": {
                    var l = TypeOfExpr(b.left);
                    var r = TypeOfExpr(b.right);
                    if (l == "float" || r == "float") return "float";
                    if (l == "str" || r == "str") return "str";
                    return l == "int" && r == "int" ? "int" : null;
                }
                case BinExpr b2 when b2.op == "/": return "float";
                case CallExpr call when call.func is NameExpr f && (f.name == "__cint" || f.name == "__cdiv" || f.name == "__cmod" || f.name == "len"):
                    if (f.name == "__cdiv" || f.name == "__cmod") {
                        var l = TypeOfExpr(call.args[0]);
                        var r = TypeOfExpr(call.args[1]);
                        return l == "int" && r == "int" ? "int" : l == "float" || r == "float" ? "float" : null;
                    }
                    return "int";
                case CallExpr call2 when call2.func is NameExpr f2 && f2.name == "float": return "float";
                case UnaryExpr u when u.op == "-": return TypeOfExpr(u.operand);
                case UnaryExpr u2 when u2.op == "not": return "bool";
                case CompareExpr: case BoolExpr: return "bool";
            }
            return null;
        }

        private static string PyName(string name) => PyKeywords.Contains(name) ? name + "_" : name;

        /// <summary>maxSpeed -> max_speed (LemLib's options become keyword arguments).</summary>
        public static string Snake(string name) {
            var sb = new StringBuilder();
            for (var i = 0; i < name.Length; i++) {
                var c = name[i];
                if (char.IsUpper(c) && i > 0 && !char.IsUpper(name[i - 1])) sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }
    }
}
