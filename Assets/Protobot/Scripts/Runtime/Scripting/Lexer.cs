using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Protobot.Scripting {
    public enum Tok { Name, Int, Float, Str, FStr, Op, Newline, Indent, Dedent, End }

    public struct Token {
        public Tok kind;
        public string text;     // a name, an operator, a string's value
        public object value;    // numbers; f-strings: List<object> of string / FPart
        public int line, column;
        public override string ToString() => kind + " '" + text + "' line " + line;
    }

    /// <summary>An f-string's {expression!conv:spec} piece (its expression is parsed by the parser).</summary>
    public sealed class FPart {
        public string expr, spec, conv;
        public int line, column;
    }

    /// <summary>V2 (robot code): turns Python source into tokens, with INDENT/DEDENT for blocks.</summary>
    public sealed class Lexer {
        private readonly string src;
        private int pos, line = 1, lineStart;
        private readonly List<Token> tokens = new();
        private readonly Stack<int> indents = new();
        private int depth;   // brackets open: newlines and indentation don't count inside

        private static readonly string[] Ops3 = { "**=", "//=", ">>=", "<<=", "..." };
        private static readonly string[] Ops2 = { "**", "//", "==", "!=", "<=", ">=", "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=", "->", "<<", ">>", ":=" };
        private const string Ops1 = "+-*/%<>=()[]{},:.;@&|^~";

        public Lexer(string source) {
            src = source.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\t", "    ");
            indents.Push(0);
        }

        public static List<Token> Tokenize(string source) => new Lexer(source).Run();

        private SyntaxErr Error(string message) => new(message, line, pos - lineStart + 1);

        private void Add(Tok kind, string text, object value = null, int start = -1) {
            var s = start < 0 ? pos : start;
            tokens.Add(new Token { kind = kind, text = text, value = value, line = line, column = s - lineStart + 1 });
        }

        private List<Token> Run() {
            var atLineStart = true;
            while (pos < src.Length) {
                // V2: a line that starts inside brackets has no indentation; without this, closing the bracket at the
                // end of it made its line end look like a blank line and the statement's newline was lost
                // (a print(...) continued onto a second line, then wait(1): "expected the end of the line but found 'wait'").
                if (atLineStart && depth > 0) atLineStart = false;
                if (atLineStart && depth == 0) {
                    atLineStart = false;
                    // Measure the indentation; blank and comment-only lines don't count.
                    var col = 0;
                    var p = pos;
                    while (p < src.Length && src[p] == ' ') {
                        col++;
                        p++;
                    }
                    if (p >= src.Length || src[p] == '\n' || src[p] == '#') {
                        pos = p;
                        while (pos < src.Length && src[pos] != '\n') pos++;
                        if (pos < src.Length) NextLine();
                        atLineStart = true;
                        continue;
                    }
                    pos = p;
                    if (col > indents.Peek()) {
                        indents.Push(col);
                        Add(Tok.Indent, "");
                    }
                    else {
                        while (col < indents.Peek()) {
                            indents.Pop();
                            Add(Tok.Dedent, "");
                        }
                        if (col != indents.Peek()) throw Error("this line's indentation doesn't match any line above it");
                    }
                }
                var c = src[pos];
                if (c == '\n') {
                    if (depth == 0 && tokens.Count > 0 && tokens[tokens.Count - 1].kind != Tok.Newline) Add(Tok.Newline, "");
                    NextLine();
                    atLineStart = true;
                    continue;
                }
                if (c == ' ') {
                    pos++;
                    continue;
                }
                if (c == '#') {
                    while (pos < src.Length && src[pos] != '\n') pos++;
                    continue;
                }
                if (c == '\\' && pos + 1 < src.Length && src[pos + 1] == '\n') {
                    pos++;
                    NextLine();
                    continue;
                }
                if (char.IsLetter(c) || c == '_') {
                    // String prefixes: r, f, b, u, rf, fr...
                    var start = pos;
                    while (pos < src.Length && (char.IsLetterOrDigit(src[pos]) || src[pos] == '_')) pos++;
                    var word = src.Substring(start, pos - start);
                    if (pos < src.Length && (src[pos] == '"' || src[pos] == '\'') && word.Length <= 2 && IsPrefix(word)) {
                        var lower = word.ToLowerInvariant();
                        ReadString(lower.Contains("r"), lower.Contains("f"), start);
                        continue;
                    }
                    tokens.Add(new Token { kind = Tok.Name, text = word, line = line, column = start - lineStart + 1 });
                    continue;
                }
                if (char.IsDigit(c) || c == '.' && pos + 1 < src.Length && char.IsDigit(src[pos + 1])) {
                    ReadNumber();
                    continue;
                }
                if (c == '"' || c == '\'') {
                    ReadString(false, false, pos);
                    continue;
                }
                var op = MatchOp();
                if (op == null) throw Error("the character '" + c + "' can't be used here");
                if (op == "(" || op == "[" || op == "{") depth++;
                else if (op == ")" || op == "]" || op == "}") depth = System.Math.Max(0, depth - 1);
                Add(Tok.Op, op);
                pos += op.Length;
            }
            if (tokens.Count > 0 && tokens[tokens.Count - 1].kind != Tok.Newline) Add(Tok.Newline, "");
            while (indents.Count > 1) {
                indents.Pop();
                Add(Tok.Dedent, "");
            }
            Add(Tok.End, "");
            return tokens;
        }

        private static bool IsPrefix(string w) {
            foreach (var ch in w.ToLowerInvariant())
                if (ch != 'r' && ch != 'f' && ch != 'b' && ch != 'u') return false;
            return true;
        }

        private void NextLine() {
            pos++;
            line++;
            lineStart = pos;
        }

        private string MatchOp() {
            foreach (var o in Ops3)
                if (string.CompareOrdinal(src, pos, o, 0, 3) == 0) return o;
            foreach (var o in Ops2)
                if (string.CompareOrdinal(src, pos, o, 0, 2) == 0) return o;
            return Ops1.IndexOf(src[pos]) >= 0 ? src[pos].ToString() : null;
        }

        private void ReadNumber() {
            var start = pos;
            if (src[pos] == '0' && pos + 1 < src.Length && "xXbBoO".IndexOf(src[pos + 1]) >= 0) {
                var kind = char.ToLowerInvariant(src[pos + 1]);
                pos += 2;
                var digits = new StringBuilder();
                while (pos < src.Length && (char.IsLetterOrDigit(src[pos]) || src[pos] == '_')) {
                    if (src[pos] != '_') digits.Append(src[pos]);
                    pos++;
                }
                var radix = kind == 'x' ? 16 : kind == 'b' ? 2 : 8;
                try {
                    Add(Tok.Int, src.Substring(start, pos - start), System.Convert.ToInt64(digits.ToString(), radix), start);
                }
                catch {
                    throw Error("'" + src.Substring(start, pos - start) + "' isn't a number");
                }
                return;
            }
            var isFloat = false;
            while (pos < src.Length && (char.IsDigit(src[pos]) || src[pos] == '_')) pos++;
            if (pos < src.Length && src[pos] == '.' && !(pos + 1 < src.Length && src[pos + 1] == '.')) {
                isFloat = true;
                pos++;
                while (pos < src.Length && (char.IsDigit(src[pos]) || src[pos] == '_')) pos++;
            }
            if (pos < src.Length && (src[pos] == 'e' || src[pos] == 'E')) {
                var save = pos;
                pos++;
                if (pos < src.Length && (src[pos] == '+' || src[pos] == '-')) pos++;
                if (pos < src.Length && char.IsDigit(src[pos])) {
                    isFloat = true;
                    while (pos < src.Length && char.IsDigit(src[pos])) pos++;
                }
                else pos = save;
            }
            var text = src.Substring(start, pos - start).Replace("_", "");
            if (pos < src.Length && (char.IsLetter(src[pos]) || src[pos] == '_')) throw Error("a number can't run into a name ('" + text + src[pos] + "')");
            if (isFloat) Add(Tok.Float, text, double.Parse(text, CultureInfo.InvariantCulture), start);
            else if (long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var l)) Add(Tok.Int, text, l, start);
            else Add(Tok.Float, text, double.Parse(text, CultureInfo.InvariantCulture), start);
        }

        private void ReadString(bool raw, bool format, int start) {
            var startLine = line;
            var startCol = start - lineStart + 1;
            var quote = src[pos];
            var triple = pos + 2 < src.Length && src[pos + 1] == quote && src[pos + 2] == quote;
            pos += triple ? 3 : 1;
            var sb = new StringBuilder();
            while (true) {
                if (pos >= src.Length) throw new SyntaxErr("this string never ends (a missing " + quote + ")", startLine, startCol);
                var c = src[pos];
                if (triple) {
                    if (c == quote && pos + 2 < src.Length && src[pos + 1] == quote && src[pos + 2] == quote) {
                        pos += 3;
                        break;
                    }
                }
                else if (c == quote) {
                    pos++;
                    break;
                }
                if (c == '\n') {
                    if (!triple) throw new SyntaxErr("this string never ends on its line (a missing " + quote + ")", startLine, startCol);
                    sb.Append('\n');
                    NextLine();
                    continue;
                }
                if (c == '\\' && !raw && pos + 1 < src.Length) {
                    var n = src[pos + 1];
                    pos += 2;
                    switch (n) {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case >= '0' and <= '7': {   // V2: octal (\033 = escape, for console colors)
                            var v = n - '0';
                            for (var k = 1; k < 3 && pos < src.Length && src[pos] >= '0' && src[pos] <= '7'; k++) v = v * 8 + (src[pos++] - '0');
                            sb.Append((char)v);
                            break;
                        }
                        case '\\': sb.Append('\\'); break;
                        case '\'': sb.Append('\''); break;
                        case '"': sb.Append('"'); break;
                        case '\n': line++; lineStart = pos; break;
                        case 'x' when pos + 2 <= src.Length:
                            sb.Append((char)System.Convert.ToInt32(src.Substring(pos, 2), 16));
                            pos += 2;
                            break;
                        case 'u' when pos + 4 <= src.Length:
                            sb.Append((char)System.Convert.ToInt32(src.Substring(pos, 4), 16));
                            pos += 4;
                            break;
                        default: sb.Append('\\').Append(n); break;
                    }
                    continue;
                }
                sb.Append(c);
                pos++;
            }
            var text = sb.ToString();
            // Two strings side by side join ("a" "b").
            if (!format && tokens.Count > 0 && tokens[tokens.Count - 1].kind == Tok.Str) {
                var last = tokens[tokens.Count - 1];
                last.text += text;
                tokens[tokens.Count - 1] = last;
                return;
            }
            if (!format) {
                tokens.Add(new Token { kind = Tok.Str, text = text, line = startLine, column = startCol });
                return;
            }
            tokens.Add(new Token { kind = Tok.FStr, text = text, value = SplitF(text, startLine, startCol), line = startLine, column = startCol });
        }

        /// <summary>An f-string's text and {expression} pieces.</summary>
        private static List<object> SplitF(string s, int line, int col) {
            var parts = new List<object>();
            var sb = new StringBuilder();
            for (var i = 0; i < s.Length; i++) {
                var c = s[i];
                if (c == '{' && i + 1 < s.Length && s[i + 1] == '{') {
                    sb.Append('{');
                    i++;
                    continue;
                }
                if (c == '}' && i + 1 < s.Length && s[i + 1] == '}') {
                    sb.Append('}');
                    i++;
                    continue;
                }
                if (c == '}') throw new SyntaxErr("f-string: a single '}' (write '}}' for the character)", line, col);
                if (c != '{') {
                    sb.Append(c);
                    continue;
                }
                if (sb.Length > 0) {
                    parts.Add(sb.ToString());
                    sb.Clear();
                }
                // Find the expression's end: '}' (or ! / : at depth 0, outside strings).
                var d = 0;
                var j = i + 1;
                int conv = -1, spec = -1;
                char inStr = '\0';
                for (; j < s.Length; j++) {
                    var ch = s[j];
                    if (inStr != '\0') {
                        if (ch == inStr) inStr = '\0';
                        continue;
                    }
                    if (ch == '\'' || ch == '"') inStr = ch;
                    else if (ch == '(' || ch == '[' || ch == '{') d++;
                    else if ((ch == ')' || ch == ']' || ch == '}') && d > 0) d--;
                    else if (ch == '}' && d == 0) break;
                    else if (ch == '!' && d == 0 && spec < 0 && j + 1 < s.Length && s[j + 1] != '=') conv = j;
                    else if (ch == ':' && d == 0 && spec < 0) spec = j;
                }
                if (j >= s.Length) throw new SyntaxErr("f-string: a '{' without its '}'", line, col);
                var exprEnd = conv >= 0 ? conv : spec >= 0 ? spec : j;
                var part = new FPart {
                    expr = s.Substring(i + 1, exprEnd - i - 1),
                    conv = conv >= 0 ? s.Substring(conv + 1, (spec >= 0 ? spec : j) - conv - 1) : null,
                    spec = spec >= 0 ? s.Substring(spec + 1, j - spec - 1) : null,
                    line = line,
                    column = col,
                };
                if (part.expr.Trim().Length == 0) throw new SyntaxErr("f-string: empty {}", line, col);
                // f"{x=}" prints "x=" then the value.
                if (part.expr.TrimEnd().EndsWith("=") && !part.expr.TrimEnd().EndsWith("==")) {
                    parts.Add(part.expr);
                    part.expr = part.expr.TrimEnd().TrimEnd('=');
                    if (part.conv == null && part.spec == null) part.conv = "r";
                }
                parts.Add(part);
                i = j;
            }
            if (sb.Length > 0) parts.Add(sb.ToString());
            return parts;
        }
    }
}
