#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using Firebase.Firestore;

namespace Farmverse.Infrastructure.Firebase
{
    /// <summary>
    /// Normalizes SDK values into the wire shape the generated contracts read:
    /// string-keyed maps, lists, long, double, string, bool. Firestore timestamps
    /// become epoch milliseconds (only audit fields use them, 04 conventions).
    /// </summary>
    internal static class Values
    {
        public static object? ToWire(object? value)
        {
            switch (value)
            {
                case null: return null;
                case string _: return value;
                case bool _: return value;
                case long _: return value;
                case int i: return (long)i;
                case double _: return value;
                case float f: return (double)f;
                case Timestamp t: return t.ToDateTimeOffset().ToUnixTimeMilliseconds();
                case IDictionary<string, object> map: return ToWireMap(map);
                case IDictionary dictionary:
                    var converted = new Dictionary<string, object>(dictionary.Count);
                    foreach (DictionaryEntry entry in dictionary)
                    {
                        var v = ToWire(entry.Value);
                        if (v != null) converted[Convert.ToString(entry.Key)!] = v;
                    }
                    return converted;
                case IList list:
                    var items = new List<object>(list.Count);
                    foreach (var item in list)
                    {
                        var v = ToWire(item);
                        if (v != null) items.Add(v);
                    }
                    return items;
                default: return value;
            }
        }

        public static Dictionary<string, object> ToWireMap(IDictionary<string, object> map)
        {
            var result = new Dictionary<string, object>(map.Count);
            foreach (var pair in map)
            {
                var v = ToWire(pair.Value);
                if (v != null) result[pair.Key] = v;
            }
            return result;
        }
    }
}
