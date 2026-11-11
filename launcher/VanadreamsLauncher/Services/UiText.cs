using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Vanadreams.Services
{
    /// <summary>Explicit launcher-owned text only; English is the unchanged fallback.</summary>
    public static class UiText
    {
        private sealed class Entry
        {
            public string English;
            public string Japanese;
            public bool ContextOnly;
        }
        private static readonly Dictionary<string, Entry> Keys = Load();
        private static readonly Dictionary<string, string> Japanese = Keys.Values.Where(e => !e.ContextOnly)
            .GroupBy(e => e.English, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Japanese, StringComparer.Ordinal);
        public static string Language { get; private set; } = "en";

        public static void Initialize(string choice) => Language = UiLanguage.Resolve(choice, CultureInfo.CurrentUICulture);
        public static string T(string english) => Translate(english, Language);
        public static string F(string english, params object[] args) => FormatFor(Language, english, args);
        public static string Translate(string english, string language)
        {
            string translated;
            return language == "ja" && english != null && Japanese.TryGetValue(english, out translated) && !string.IsNullOrEmpty(translated)
                ? translated : english;
        }
        public static string FormatFor(string language, string english, params object[] args) =>
            string.Format(CultureInfo.CurrentCulture, Translate(english, language), args);

        public static string FromKey(string key)
        {
            Entry entry;
            return key != null && Keys.TryGetValue(key, out entry) ? (Language == "ja" && !string.IsNullOrEmpty(entry.Japanese) ? entry.Japanese : entry.English) : key;
        }

        private static Dictionary<string, Entry> Load()
        {
            var entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
            using (var stream = typeof(UiText).Assembly.GetManifestResourceStream("Vanadreams.Localization.launcher.json"))
            {
                if (stream == null) throw new InvalidOperationException("Missing launcher language catalog.");
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                    foreach (var pair in Json.ParseObject(reader.ReadToEnd()))
                    {
                        var value = Json.Obj(pair.Value);
                        entries.Add(pair.Key, new Entry { English = Json.Str(value, "en", ""), Japanese = Json.Str(value, "ja", ""), ContextOnly = Json.Bool(value, "context") });
                    }
            }
            return entries;
        }
    }
}
