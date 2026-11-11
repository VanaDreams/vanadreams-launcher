using System;
using System.Globalization;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vanadreams.Services;

namespace Vanadreams.Tests
{
    [TestClass]
    public class UiLanguageTests
    {
        [TestMethod]
        public void Auto_uses_display_language_and_other_languages_keep_English()
        {
            Assert.AreEqual("ja", UiLanguage.Resolve("auto", CultureInfo.GetCultureInfo("ja-JP")));
            Assert.AreEqual("en", UiLanguage.Resolve("auto", CultureInfo.GetCultureInfo("en-CA")));
            Assert.AreEqual("en", UiLanguage.Resolve("auto", CultureInfo.GetCultureInfo("fr-FR")));
            Assert.AreEqual("en", UiLanguage.Resolve(null, CultureInfo.InvariantCulture));
        }

        [TestMethod]
        public void Manual_choice_overrides_Windows_and_bad_values_fall_back_to_auto()
        {
            var jp = CultureInfo.GetCultureInfo("ja-JP");
            var en = CultureInfo.GetCultureInfo("en-US");
            Assert.AreEqual("en", UiLanguage.Resolve("en", jp));
            Assert.AreEqual("ja", UiLanguage.Resolve("ja", en));
            Assert.AreEqual("ja", UiLanguage.Resolve("JA", en));
            Assert.AreEqual("ja", UiLanguage.Resolve("broken", jp));
        }

        [TestMethod]
        public void Settings_migrate_old_files_and_preserve_the_manual_choice()
        {
            var dir = Path.Combine(Path.GetTempPath(), "vdl-language-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var file = Path.Combine(dir, "settings.json");
                File.WriteAllText(file, "{\"lastProfile\":\"test-player\",\"musicOn\":false}");
                var settings = LauncherSettings.Load(file);
                Assert.AreEqual("auto", settings.UiLanguage);
                settings.UiLanguage = "ja";
                settings.Save();
                var saved = LauncherSettings.Load(file);
                Assert.AreEqual("ja", saved.UiLanguage);
                Assert.AreEqual("test-player", saved.LastProfile);
                Assert.IsFalse(saved.MusicOn);
            }
            finally { Directory.Delete(dir, true); }
        }

        [TestMethod]
        public void Translation_preserves_English_unknown_values_and_format_arguments()
        {
            Assert.AreEqual("Username", UiText.Translate("Username", "en"));
            Assert.AreEqual("ユーザー名", UiText.Translate("Username", "ja"));
            Assert.AreEqual("untranslated / player supplied", UiText.Translate("untranslated / player supplied", "ja"));
            Assert.AreEqual("保存先: D:\\Games\\日本語", UiText.FormatFor("ja", "Installed at {0}", @"D:\Games\日本語"));
        }

        [TestMethod]
        public void Japanese_recovery_advice_keeps_the_original_loader_error()
        {
            const string raw = "Invalid username or password";
            UiText.Initialize("ja");
            try
            {
                System.Collections.Generic.List<string> errors;
                var stop = LaunchDiagnosis.Classify(raw, out errors);
                Assert.AreEqual(LoaderStop.LoaderError, stop);
                var message = LaunchDiagnosis.Sentence(stop, errors, null, raw);
                StringAssert.Contains(message, raw);
                StringAssert.Contains(message, "パスワード");
                StringAssert.Contains(message, "プロファイル");
                Assert.IsTrue(LaunchDiagnosis.IsErrorLine(raw));
            }
            finally { UiText.Initialize("en"); }
        }

        [TestMethod]
        public void Context_keys_translate_without_changing_the_common_English_text()
        {
            UiText.Initialize("ja");
            try { Assert.AreEqual("ウィンドウ", UiText.FromKey("window-mode")); }
            finally { UiText.Initialize("en"); }
            Assert.AreEqual("Window", UiText.FromKey("window-mode"));
            Assert.AreEqual("  Paths / 日本語 / {literal}  ", UiText.Translate("  Paths / 日本語 / {literal}  ", "en"));
        }

        [TestMethod]
        public void Launcher_language_never_changes_game_language_or_numeric_culture()
        {
            var command = LoaderCommand.Parse("--server vanadreams.org --lang 1");
            var before = command.ToIniCommand();
            var culture = CultureInfo.CurrentCulture;
            UiText.Initialize("ja");
            Assert.AreEqual("ja", UiText.Language);
            Assert.AreEqual(before, command.ToIniCommand());
            Assert.AreEqual(1, command.Lang);
            Assert.AreSame(culture, CultureInfo.CurrentCulture);
            UiText.Initialize("en");
        }
    }
}
