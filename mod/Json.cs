using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CS1McpBridge
{
    // Minimal, dependency-free JSON for the bridge protocol. Supports exactly what
    // the wire protocol needs: objects, arrays, strings, numbers, bools, null.
    // Kept first-party so the mod builds with no external packages. The public
    // surface mirrors common JSON-node libraries (JSON.Parse, node["k"], .AsInt …).

    public static class JSON
    {
        public static JSONNode Parse(string json)
        {
            int i = 0;
            JSONNode node = JSONParser.ParseValue(json, ref i);
            return node;
        }
    }

    public abstract class JSONNode
    {
        public virtual JSONNode this[string key] { get { return JSONNull.CreateOrGet(); } set { } }
        public virtual JSONNode this[int index] { get { return JSONNull.CreateOrGet(); } set { } }

        public virtual string Value { get { return null; } set { } }
        public virtual int Count { get { return 0; } }
        public virtual bool HasKey(string key) { return false; }
        public virtual void Add(JSONNode node) { }

        public virtual double AsDouble { get { return 0.0; } }
        public int AsInt { get { return (int)AsDouble; } }
        public long AsLong { get { return (long)AsDouble; } }
        public float AsFloat { get { return (float)AsDouble; } }
        public virtual bool AsBool { get { return false; } }

        public override string ToString()
        {
            var sb = new StringBuilder();
            Write(sb);
            return sb.ToString();
        }
        internal abstract void Write(StringBuilder sb);

        public static implicit operator JSONNode(string s) { return new JSONString(s); }
        public static implicit operator JSONNode(int n) { return new JSONNumber(n); }
        public static implicit operator JSONNode(long n) { return new JSONNumber(n); }
        public static implicit operator JSONNode(float n) { return new JSONNumber(n); }
        public static implicit operator JSONNode(double n) { return new JSONNumber(n); }
        public static implicit operator JSONNode(bool b) { return new JSONBool(b); }

        internal static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            if (s != null)
            {
                foreach (char c in s)
                {
                    switch (c)
                    {
                        case '"': sb.Append("\\\""); break;
                        case '\\': sb.Append("\\\\"); break;
                        case '\b': sb.Append("\\b"); break;
                        case '\f': sb.Append("\\f"); break;
                        case '\n': sb.Append("\\n"); break;
                        case '\r': sb.Append("\\r"); break;
                        case '\t': sb.Append("\\t"); break;
                        default:
                            if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                            else sb.Append(c);
                            break;
                    }
                }
            }
            sb.Append('"');
        }
    }

    public sealed class JSONString : JSONNode
    {
        string _v;
        public JSONString(string v) { _v = v ?? ""; }
        public override string Value { get { return _v; } set { _v = value ?? ""; } }
        public override double AsDouble
        {
            get { double d; return double.TryParse(_v, NumberStyles.Any, CultureInfo.InvariantCulture, out d) ? d : 0.0; }
        }
        public override bool AsBool { get { return _v == "true" || _v == "1"; } }
        internal override void Write(StringBuilder sb) { WriteString(sb, _v); }
    }

    public sealed class JSONNumber : JSONNode
    {
        double _v;
        public JSONNumber(double v) { _v = v; }
        public override string Value
        {
            get { return _v.ToString(CultureInfo.InvariantCulture); }
            set { double d; if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out d)) _v = d; }
        }
        public override double AsDouble { get { return _v; } }
        public override bool AsBool { get { return _v != 0.0; } }
        internal override void Write(StringBuilder sb)
        {
            if (_v == Math.Floor(_v) && !double.IsInfinity(_v) && Math.Abs(_v) < 9.2e18)
                sb.Append(((long)_v).ToString(CultureInfo.InvariantCulture));
            else
                sb.Append(_v.ToString("R", CultureInfo.InvariantCulture));
        }
    }

    public sealed class JSONBool : JSONNode
    {
        bool _v;
        public JSONBool(bool v) { _v = v; }
        public override string Value { get { return _v ? "true" : "false"; } set { _v = value == "true"; } }
        public override double AsDouble { get { return _v ? 1.0 : 0.0; } }
        public override bool AsBool { get { return _v; } }
        internal override void Write(StringBuilder sb) { sb.Append(_v ? "true" : "false"); }
    }

    public sealed class JSONNull : JSONNode
    {
        static readonly JSONNull Instance = new JSONNull();
        public static JSONNull CreateOrGet() { return Instance; }
        internal override void Write(StringBuilder sb) { sb.Append("null"); }
    }

    public sealed class JSONObject : JSONNode
    {
        readonly Dictionary<string, JSONNode> _d = new Dictionary<string, JSONNode>();
        readonly List<string> _order = new List<string>();

        public override JSONNode this[string key]
        {
            get { JSONNode n; return _d.TryGetValue(key, out n) ? n : JSONNull.CreateOrGet(); }
            set
            {
                if (value == null) value = JSONNull.CreateOrGet();
                if (!_d.ContainsKey(key)) _order.Add(key);
                _d[key] = value;
            }
        }
        public override bool HasKey(string key) { return _d.ContainsKey(key); }
        public override int Count { get { return _order.Count; } }
        internal override void Write(StringBuilder sb)
        {
            sb.Append('{');
            for (int i = 0; i < _order.Count; i++)
            {
                if (i > 0) sb.Append(',');
                WriteString(sb, _order[i]);
                sb.Append(':');
                _d[_order[i]].Write(sb);
            }
            sb.Append('}');
        }
    }

    public sealed class JSONArray : JSONNode
    {
        readonly List<JSONNode> _l = new List<JSONNode>();
        public override void Add(JSONNode node) { _l.Add(node ?? JSONNull.CreateOrGet()); }
        public override JSONNode this[int index]
        {
            get { return index >= 0 && index < _l.Count ? _l[index] : JSONNull.CreateOrGet(); }
            set { if (index >= 0 && index < _l.Count) _l[index] = value; }
        }
        public override int Count { get { return _l.Count; } }
        internal override void Write(StringBuilder sb)
        {
            sb.Append('[');
            for (int i = 0; i < _l.Count; i++)
            {
                if (i > 0) sb.Append(',');
                _l[i].Write(sb);
            }
            sb.Append(']');
        }
    }

    static class JSONParser
    {
        public static JSONNode ParseValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) throw new FormatException("unexpected end of JSON");
            char c = s[i];
            switch (c)
            {
                case '{': return ParseObject(s, ref i);
                case '[': return ParseArray(s, ref i);
                case '"': return new JSONString(ParseString(s, ref i));
                case 't': Expect(s, ref i, "true"); return new JSONBool(true);
                case 'f': Expect(s, ref i, "false"); return new JSONBool(false);
                case 'n': Expect(s, ref i, "null"); return JSONNull.CreateOrGet();
                default: return ParseNumber(s, ref i);
            }
        }

        static JSONNode ParseObject(string s, ref int i)
        {
            var o = new JSONObject();
            i++; // {
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return o; }
            while (true)
            {
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != '"') throw new FormatException("expected string key at " + i);
                string key = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new FormatException("expected ':' at " + i);
                i++;
                o[key] = ParseValue(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length) throw new FormatException("unterminated object");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; break; }
                throw new FormatException("expected ',' or '}' at " + i);
            }
            return o;
        }

        static JSONNode ParseArray(string s, ref int i)
        {
            var a = new JSONArray();
            i++; // [
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return a; }
            while (true)
            {
                a.Add(ParseValue(s, ref i));
                SkipWs(s, ref i);
                if (i >= s.Length) throw new FormatException("unterminated array");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; break; }
                throw new FormatException("expected ',' or ']' at " + i);
            }
            return a;
        }

        static string ParseString(string s, ref int i)
        {
            i++; // opening quote
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c == '\\')
                {
                    if (i >= s.Length) break;
                    char e = s[i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 <= s.Length)
                            {
                                int cp = int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                                sb.Append((char)cp);
                                i += 4;
                            }
                            break;
                        default: sb.Append(e); break;
                    }
                }
                else sb.Append(c);
            }
            throw new FormatException("unterminated string");
        }

        static JSONNode ParseNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            string num = s.Substring(start, i - start);
            double d;
            if (!double.TryParse(num, NumberStyles.Any, CultureInfo.InvariantCulture, out d))
                throw new FormatException("invalid number '" + num + "'");
            return new JSONNumber(d);
        }

        static void Expect(string s, ref int i, string literal)
        {
            if (i + literal.Length > s.Length || s.Substring(i, literal.Length) != literal)
                throw new FormatException("expected '" + literal + "' at " + i);
            i += literal.Length;
        }

        static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i++;
        }
    }
}
