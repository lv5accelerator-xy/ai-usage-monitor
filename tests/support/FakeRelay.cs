// Test-only in-memory relay for the background worker (no network).
using System;
using System.Collections.Generic;

namespace CodexUsageDesktop {
    public static class FakeRelay {
        public static readonly List<string[]> Requests = new List<string[]>();
        public static string GetResponse = "";
        public static bool Fail;
        static readonly object Gate = new object();
        public static void Install() { lock (Gate) { Requests.Clear(); GetResponse = ""; Fail = false; } RemoteWorker.Http = Handle; }
        public static string Handle(string method, string url, string body) {
            lock (Gate) {
                Requests.Add(new[] { method, url, body });
                if (Fail) throw new System.Net.WebException("Fixture relay offline");
                return method == "GET" ? GetResponse : "{}";
            }
        }
        public static string[][] Snapshot() { lock (Gate) return Requests.ToArray(); }
    }
}

namespace CodexUsageDesktop {
    // Test-only: PowerShell hashtables/arrays -> JavaScriptSerializer-shaped trees, unwrapping PSObject.
    public static class TestTree {
        static object Unwrap(object value) {
            if (value == null) return null;
            var type = value.GetType();
            if (type.FullName == "System.Management.Automation.PSObject") {
                var property = type.GetProperty("BaseObject");
                return Unwrap(property.GetValue(value, null));
            }
            return value;
        }
        public static object From(object value) {
            value = Unwrap(value);
            if (value == null || value is string) return value;
            var dictionary = value as System.Collections.IDictionary;
            if (dictionary != null) {
                var map = new Dictionary<string, object>();
                foreach (System.Collections.DictionaryEntry entry in dictionary) map[Convert.ToString(entry.Key)] = From(entry.Value);
                return map;
            }
            var list = value as System.Collections.IEnumerable;
            if (list != null) {
                var items = new List<object>();
                foreach (object item in list) items.Add(From(item));
                return items.ToArray();
            }
            return value;
        }
    }
}
