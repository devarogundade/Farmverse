#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Farmverse.Contracts
{
    /// <summary>
    /// A small, strict, allocation-conscious JSON reader and writer for wire values.
    ///
    /// Reading produces exactly what the Firebase SDKs produce — maps as
    /// <c>Dictionary&lt;string, object&gt;</c>, arrays as <c>List&lt;object&gt;</c>,
    /// integers as <c>long</c>, other numbers as <c>double</c> — so a value read from
    /// disk (a persisted action batch, a cached catalogue) goes through the same
    /// generated <c>FromWire</c> as a value from the network. No reflection, so it is
    /// safe under IL2CPP code stripping.
    /// </summary>
    public static class WireJson
    {
        /// <summary>Parse JSON text into a wire value.</summary>
        public static object? Parse(string json)
        {
            if (json == null)
            {
                throw new ArgumentNullException(nameof(json));
            }
            var reader = new Reader(json);
            var value = reader.ReadValue(0);
            reader.SkipWhitespace();
            if (!reader.AtEnd)
            {
                throw reader.Error("unexpected trailing characters");
            }
            return value;
        }

        /// <summary>
        /// Serialize a wire value. With <paramref name="sortKeys"/> the output is
        /// canonical (keys ordered ordinally), which is what equality checks and
        /// content hashes need.
        /// </summary>
        public static string Serialize(object? value, bool sortKeys = false)
        {
            var builder = new StringBuilder(256);
            Write(builder, value, sortKeys, 0);
            return builder.ToString();
        }

        private const int MaxDepth = 64;

        // ------------------------------------------------------------------
        // writer
        // ------------------------------------------------------------------

        private static void Write(StringBuilder builder, object? value, bool sortKeys, int depth)
        {
            if (depth > MaxDepth)
            {
                throw new WireFormatException("WireJson", "value is nested too deeply");
            }
            switch (value)
            {
                case null:
                    builder.Append("null");
                    return;
                case string text:
                    WriteString(builder, text);
                    return;
                case bool flag:
                    builder.Append(flag ? "true" : "false");
                    return;
                case long l:
                    builder.Append(l.ToString(CultureInfo.InvariantCulture));
                    return;
                case int i:
                    builder.Append(i.ToString(CultureInfo.InvariantCulture));
                    return;
                case double d:
                    WriteDouble(builder, d);
                    return;
                case float f:
                    WriteDouble(builder, f);
                    return;
                case IWireObject wire:
                    Write(builder, wire.ToWire(), sortKeys, depth);
                    return;
                case IDictionary map:
                    WriteMap(builder, map, sortKeys, depth);
                    return;
                case IList list:
                    builder.Append('[');
                    for (var index = 0; index < list.Count; index++)
                    {
                        if (index > 0)
                        {
                            builder.Append(',');
                        }
                        Write(builder, list[index], sortKeys, depth + 1);
                    }
                    builder.Append(']');
                    return;
                case short s:
                    builder.Append(s.ToString(CultureInfo.InvariantCulture));
                    return;
                case byte b:
                    builder.Append(b.ToString(CultureInfo.InvariantCulture));
                    return;
                case uint ui:
                    builder.Append(ui.ToString(CultureInfo.InvariantCulture));
                    return;
                default:
                    throw new WireFormatException("WireJson", "cannot serialize a " + value.GetType().Name);
            }
        }

        private static void WriteMap(StringBuilder builder, IDictionary map, bool sortKeys, int depth)
        {
            var keys = new List<string>(map.Count);
            foreach (var key in map.Keys)
            {
                if (!(key is string text))
                {
                    throw new WireFormatException("WireJson", "map key is not a string");
                }
                keys.Add(text);
            }
            if (sortKeys)
            {
                keys.Sort(StringComparer.Ordinal);
            }
            builder.Append('{');
            for (var index = 0; index < keys.Count; index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }
                WriteString(builder, keys[index]);
                builder.Append(':');
                Write(builder, map[keys[index]], sortKeys, depth + 1);
            }
            builder.Append('}');
        }

        private static void WriteDouble(StringBuilder builder, double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d))
            {
                throw new WireFormatException("WireJson", "NaN and infinity are not JSON");
            }
            if (Math.Floor(d) == d && Math.Abs(d) <= Wire.MaxSafeInteger)
            {
                builder.Append(((long)d).ToString(CultureInfo.InvariantCulture));
                return;
            }
            builder.Append(d.ToString("R", CultureInfo.InvariantCulture));
        }

        private static void WriteString(StringBuilder builder, string text)
        {
            builder.Append('"');
            foreach (var c in text)
            {
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                        {
                            builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(c);
                        }
                        break;
                }
            }
            builder.Append('"');
        }

        // ------------------------------------------------------------------
        // reader
        // ------------------------------------------------------------------

        private sealed class Reader
        {
            private readonly string _text;
            private int _position;

            public Reader(string text)
            {
                _text = text;
            }

            public bool AtEnd => _position >= _text.Length;

            public WireFormatException Error(string problem) =>
                new WireFormatException("WireJson@" + _position, problem);

            public void SkipWhitespace()
            {
                while (_position < _text.Length)
                {
                    var c = _text[_position];
                    if (c != ' ' && c != '\t' && c != '\n' && c != '\r')
                    {
                        return;
                    }
                    _position++;
                }
            }

            public object? ReadValue(int depth)
            {
                if (depth > MaxDepth)
                {
                    throw Error("value is nested too deeply");
                }
                SkipWhitespace();
                if (AtEnd)
                {
                    throw Error("unexpected end of input");
                }
                var c = _text[_position];
                switch (c)
                {
                    case '{': return ReadObject(depth);
                    case '[': return ReadArray(depth);
                    case '"': return ReadString();
                    case 't': ExpectWord("true"); return true;
                    case 'f': ExpectWord("false"); return false;
                    case 'n': ExpectWord("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9'))
                        {
                            return ReadNumber();
                        }
                        throw Error("unexpected character '" + c + "'");
                }
            }

            private Dictionary<string, object> ReadObject(int depth)
            {
                _position++; // {
                var map = new Dictionary<string, object>();
                SkipWhitespace();
                if (Peek() == '}')
                {
                    _position++;
                    return map;
                }
                while (true)
                {
                    SkipWhitespace();
                    if (Peek() != '"')
                    {
                        throw Error("expected a property name");
                    }
                    var key = ReadString();
                    SkipWhitespace();
                    if (Peek() != ':')
                    {
                        throw Error("expected ':'");
                    }
                    _position++;
                    if (map.ContainsKey(key))
                    {
                        throw Error("duplicate property '" + key + "'");
                    }
                    map[key] = ReadValue(depth + 1)!;
                    SkipWhitespace();
                    var next = Peek();
                    _position++;
                    if (next == '}')
                    {
                        return map;
                    }
                    if (next != ',')
                    {
                        throw Error("expected ',' or '}'");
                    }
                }
            }

            private List<object> ReadArray(int depth)
            {
                _position++; // [
                var list = new List<object>();
                SkipWhitespace();
                if (Peek() == ']')
                {
                    _position++;
                    return list;
                }
                while (true)
                {
                    list.Add(ReadValue(depth + 1)!);
                    SkipWhitespace();
                    var next = Peek();
                    _position++;
                    if (next == ']')
                    {
                        return list;
                    }
                    if (next != ',')
                    {
                        throw Error("expected ',' or ']'");
                    }
                }
            }

            private string ReadString()
            {
                _position++; // opening quote
                var builder = new StringBuilder();
                while (true)
                {
                    if (AtEnd)
                    {
                        throw Error("unterminated string");
                    }
                    var c = _text[_position++];
                    if (c == '"')
                    {
                        return builder.ToString();
                    }
                    if (c < 0x20)
                    {
                        throw Error("control character in string");
                    }
                    if (c != '\\')
                    {
                        builder.Append(c);
                        continue;
                    }
                    if (AtEnd)
                    {
                        throw Error("unterminated escape");
                    }
                    var escape = _text[_position++];
                    switch (escape)
                    {
                        case '"': builder.Append('"'); break;
                        case '\\': builder.Append('\\'); break;
                        case '/': builder.Append('/'); break;
                        case 'b': builder.Append('\b'); break;
                        case 'f': builder.Append('\f'); break;
                        case 'n': builder.Append('\n'); break;
                        case 'r': builder.Append('\r'); break;
                        case 't': builder.Append('\t'); break;
                        case 'u':
                            if (_position + 4 > _text.Length)
                            {
                                throw Error("truncated unicode escape");
                            }
                            var hex = _text.Substring(_position, 4);
                            if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                            {
                                throw Error("invalid unicode escape");
                            }
                            builder.Append((char)code);
                            _position += 4;
                            break;
                        default:
                            throw Error("invalid escape '\\" + escape + "'");
                    }
                }
            }

            private object ReadNumber()
            {
                var start = _position;
                if (Peek() == '-')
                {
                    _position++;
                }
                var integral = true;
                while (!AtEnd)
                {
                    var c = _text[_position];
                    if (c >= '0' && c <= '9')
                    {
                        _position++;
                    }
                    else if (c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-')
                    {
                        integral = false;
                        _position++;
                    }
                    else
                    {
                        break;
                    }
                }
                var token = _text.Substring(start, _position - start);
                if (integral && long.TryParse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var l))
                {
                    return l;
                }
                if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) &&
                    !double.IsInfinity(d))
                {
                    return d;
                }
                throw Error("invalid number '" + token + "'");
            }

            private void ExpectWord(string word)
            {
                if (string.CompareOrdinal(_text, _position, word, 0, word.Length) != 0)
                {
                    throw Error("expected '" + word + "'");
                }
                _position += word.Length;
            }

            private char Peek() => AtEnd ? '\0' : _text[_position];
        }
    }
}
