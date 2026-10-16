// Json.cs -- the little JSON the two halves of Hand Poser exchange, read and written without any library: objects
// become Dictionary<string, object>, arrays List<object>, numbers double, plus string, bool and null.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

static class Json {
    public static object Parse(string s) {
        int i = 0;
        object v = Value(s, ref i);
        Skip(s, ref i);
        if (i != s.Length) throw new FormatException("trailing text at " + i);
        return v;
    }

    public static string Write(object v) {
        var sb = new StringBuilder();
        Emit(sb, v);
        return sb.ToString();
    }

    // ---- helpers for the shapes the app uses ------------------------------------------------------------------------
    public static Dictionary<string, object> Obj(object v) { return v as Dictionary<string, object>; }
    public static List<object> Arr(object v) { return v as List<object>; }
    public static string Str(object v) { return v as string ?? ""; }
    public static double Num(object v, double dflt) { return v is double ? (double)v : dflt; }
    public static bool Bool(object v) { return v is bool && (bool)v; }

    // ---- reading ----------------------------------------------------------------------------------------------------
    static void Skip(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

    static object Value(string s, ref int i) {
        Skip(s, ref i);
        if (i >= s.Length) throw new FormatException("unexpected end");
        char c = s[i];
        if (c == '{') return Object(s, ref i);
        if (c == '[') return Array(s, ref i);
        if (c == '"') return String(s, ref i);
        if (Word(s, ref i, "true")) return true;
        if (Word(s, ref i, "false")) return false;
        if (Word(s, ref i, "null")) return null;
        return Number(s, ref i);
    }

    static bool Word(string s, ref int i, string w) {
        if (string.CompareOrdinal(s, i, w, 0, w.Length) != 0) return false;
        i += w.Length;
        return true;
    }

    static Dictionary<string, object> Object(string s, ref int i) {
        var d = new Dictionary<string, object>();
        i++;
        Skip(s, ref i);
        if (i < s.Length && s[i] == '}') { i++; return d; }
        while (true) {
            Skip(s, ref i);
            string k = String(s, ref i);
            Skip(s, ref i);
            if (i >= s.Length || s[i] != ':') throw new FormatException("':' expected at " + i);
            i++;
            d[k] = Value(s, ref i);
            Skip(s, ref i);
            if (i >= s.Length) throw new FormatException("unexpected end in object");
            if (s[i] == ',') { i++; continue; }
            if (s[i] == '}') { i++; return d; }
            throw new FormatException("',' or '}' expected at " + i);
        }
    }

    static List<object> Array(string s, ref int i) {
        var a = new List<object>();
        i++;
        Skip(s, ref i);
        if (i < s.Length && s[i] == ']') { i++; return a; }
        while (true) {
            a.Add(Value(s, ref i));
            Skip(s, ref i);
            if (i >= s.Length) throw new FormatException("unexpected end in array");
            if (s[i] == ',') { i++; continue; }
            if (s[i] == ']') { i++; return a; }
            throw new FormatException("',' or ']' expected at " + i);
        }
    }

    static string String(string s, ref int i) {
        if (i >= s.Length || s[i] != '"') throw new FormatException("string expected at " + i);
        i++;
        var sb = new StringBuilder();
        while (i < s.Length) {
            char c = s[i++];
            if (c == '"') return sb.ToString();
            if (c != '\\') { sb.Append(c); continue; }
            if (i >= s.Length) break;
            char e = s[i++];
            switch (e) {
                case '"': sb.Append('"'); break;
                case '\\': sb.Append('\\'); break;
                case '/': sb.Append('/'); break;
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case 'u':
                    if (i + 4 > s.Length) throw new FormatException("bad \\u escape");
                    sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber));
                    i += 4;
                    break;
                default: throw new FormatException("bad escape \\" + e);
            }
        }
        throw new FormatException("unterminated string");
    }

    static object Number(string s, ref int i) {
        int start = i;
        while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
        double d;
        if (i == start || !double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out d))
            throw new FormatException("number expected at " + start);
        return d;
    }

    // ---- writing ----------------------------------------------------------------------------------------------------
    static void Emit(StringBuilder sb, object v) {
        if (v == null) { sb.Append("null"); return; }
        if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
        if (v is string) { Quote(sb, (string)v); return; }
        if (v is double || v is float || v is int || v is long) {
            double d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
            sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
            return;
        }
        var dict = v as IDictionary<string, object>;
        if (dict != null) {
            sb.Append('{');
            bool first = true;
            foreach (var kv in dict) {
                if (!first) sb.Append(',');
                first = false;
                Quote(sb, kv.Key);
                sb.Append(':');
                Emit(sb, kv.Value);
            }
            sb.Append('}');
            return;
        }
        var list = v as System.Collections.IEnumerable;
        if (list != null) {
            sb.Append('[');
            bool first = true;
            foreach (var e in list) {
                if (!first) sb.Append(',');
                first = false;
                Emit(sb, e);
            }
            sb.Append(']');
            return;
        }
        Quote(sb, v.ToString());
    }

    static void Quote(StringBuilder sb, string s) {
        sb.Append('"');
        foreach (char c in s) {
            switch (c) {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }
}
