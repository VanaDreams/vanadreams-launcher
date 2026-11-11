using System;
using System.Globalization;

namespace Vanadreams.Services
{
    /// <summary>Launcher presentation only. Never selects FFXI files or xiloader's --lang.</summary>
    public static class UiLanguage
    {
        public static string Normalize(string choice)
        {
            var value = (choice ?? "").Trim().ToLowerInvariant();
            return value == "en" || value == "ja" ? value : "auto";
        }

        public static string Resolve(string choice, CultureInfo displayCulture)
        {
            var value = Normalize(choice);
            if (value != "auto") return value;
            return string.Equals(displayCulture?.TwoLetterISOLanguageName, "ja", StringComparison.OrdinalIgnoreCase) ? "ja" : "en";
        }
    }
}
