// Test-only stand-in for System.Web.Extensions on PowerShell 7 / .NET (Core).
// Windows PowerShell 5.1 and the packaged EXE use the real JavaScriptSerializer.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace System.Web.Script.Serialization {
    public class JavaScriptSerializer {
        public int MaxJsonLength { get; set; }
        public object DeserializeObject(string input) {
            try {
                using (var document = JsonDocument.Parse(input ?? "")) return Convert(document.RootElement);
            } catch (JsonException ex) { throw new ArgumentException(ex.Message, ex); }
        }
        static object Convert(JsonElement element) {
            switch (element.ValueKind) {
                case JsonValueKind.Object:
                    var map = new Dictionary<string, object>();
                    foreach (var property in element.EnumerateObject()) map[property.Name] = Convert(property.Value);
                    return map;
                case JsonValueKind.Array:
                    var list = new List<object>();
                    foreach (var item in element.EnumerateArray()) list.Add(Convert(item));
                    return list.ToArray();
                case JsonValueKind.String: return element.GetString();
                case JsonValueKind.Number:
                    int i; long l;
                    if (element.TryGetInt32(out i)) return i;
                    if (element.TryGetInt64(out l)) return l;
                    return element.GetDecimal();
                case JsonValueKind.True: return true;
                case JsonValueKind.False: return false;
                default: return null;
            }
        }
        public string Serialize(object value) { return JsonSerializer.Serialize(value); }
    }
}
