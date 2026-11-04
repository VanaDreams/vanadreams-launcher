using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vanadreams.Services;

namespace Vanadreams.Tests
{
    [TestClass]
    public class LaunchDiagnosisTests
    {
        // Jker_s81's loader window, 4 Nov 2026: logged in, profile loaded, then the game quit with no error line
        private const string GameQuitWindow =
            "[11/04/26 11:18:58] Resolving 'vanadreams.online' ...\n" +
            "[11/04/26 11:18:58] Resolved server address to '50.65.252.107:54231'\n" +
            "[11/04/26 11:19:00] Successfully logged in as Lestrange!\n" +
            "[11/04/26 11:19:00] Connected to server!\n" +
            "[11/04/26 11:19:01] Connected to the profile server.\n" +
            "[11/04/26 11:19:01] Profile loaded.\n" +
            "[11/04/26 11:19:02] Closing...\n";

        private string _root;

        [TestInitialize] public void Init() { _root = Path.Combine(Path.GetTempPath(), "vdl-diag-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_root); }
        [TestCleanup] public void Done() { try { Directory.Delete(_root, true); } catch (Exception) { } }

        [TestMethod]
        public void Profile_loaded_then_closing_is_the_game_quitting()
        {
            List<string> errors;
            Assert.AreEqual(LoaderStop.GameQuit, LaunchDiagnosis.Classify(GameQuitWindow, out errors));
            Assert.AreEqual(0, errors.Count);
        }

        [TestMethod]
        public void An_error_line_wins_and_is_quoted()
        {
            List<string> errors;
            var text = "[11/04/26 10:00:00] Resolving 'vanadreams.online' ...\n[11/04/26 10:00:01] Failed to login. Invalid username or password.\n[11/04/26 10:00:01] Closing...";
            Assert.AreEqual(LoaderStop.LoaderError, LaunchDiagnosis.Classify(text, out errors));
            CollectionAssert.AreEqual(new[] { "Failed to login. Invalid username or password." }, errors);
            var said = LaunchDiagnosis.Sentence(LoaderStop.LoaderError, errors, new List<LaunchDiagnosis.Finding>(), text);
            StringAssert.Contains(said, "Invalid username or password");
            StringAssert.Contains(said, "Profiles page");
        }

        [TestMethod]
        public void The_servers_words_after_error_from_remote_are_kept()
        {
            List<string> errors;
            var text = "[a] Error from remote:\n[a] Your account is banned.\n[a] Closing...";
            Assert.AreEqual(LoaderStop.LoaderError, LaunchDiagnosis.Classify(text, out errors));
            Assert.AreEqual("Error from remote: Your account is banned.", errors[0]);
        }

        [TestMethod]
        public void Nothing_read_is_unknown_and_a_closing_without_profile_is_other()
        {
            List<string> errors;
            Assert.AreEqual(LoaderStop.Unknown, LaunchDiagnosis.Classify(null, out errors));
            Assert.AreEqual(LoaderStop.Unknown, LaunchDiagnosis.Classify("  \n ", out errors));
            Assert.AreEqual(LoaderStop.Other, LaunchDiagnosis.Classify("[x] Resolving 'a' ...\n[x] Closing...", out errors));
            Assert.IsFalse(LaunchDiagnosis.IsErrorLine("Closing..."), "the closing line itself is not the reason");
        }

        [TestMethod]
        public void The_game_quit_message_clears_the_login_and_names_what_was_found()
        {
            var findings = new List<LaunchDiagnosis.Finding>
            {
                new LaunchDiagnosis.Finding { Text = "Windows' game folder (InstallFolder 0001): C:\\Games\\Vanadreams\\FINAL FANTASY XI", Problem = false },
                new LaunchDiagnosis.Finding { Text = "The started copy, D:\\CEXI\\FINAL FANTASY XI, is missing FFXiMain.dll", Problem = true },
            };
            var said = LaunchDiagnosis.Sentence(LoaderStop.GameQuit, new List<string>(), findings, GameQuitWindow);
            StringAssert.Contains(said, "the game itself quit");
            StringAssert.Contains(said, "D:\\CEXI\\FINAL FANTASY XI, is missing FFXiMain.dll");
            Assert.IsFalse(said.Contains("C:\\Games"), "only problems are listed to the player");
            StringAssert.Contains(said, "Low spec");
            Assert.IsFalse(said.Contains("wrong password"), "the login worked, so the password is never blamed");
        }

        [TestMethod]
        public void The_record_holds_the_window_and_every_check_without_passwords()
        {
            var record = LaunchDiagnosis.Record(new DateTime(2026, 11, 4, 11, 19, 2), "Vanadreams", LaunchOutcome.ClosedEarly, LoaderStop.GameQuit,
                GameQuitWindow + "launched --pass hunter2", null,
                new[] { new LaunchDiagnosis.Finding { Text = "No other copy of the game was running" } });
            StringAssert.Contains(record, "Profile loaded.");
            StringAssert.Contains(record, "ok: No other copy of the game was running");
            StringAssert.Contains(record, "GameQuit");
            Assert.IsFalse(record.Contains("hunter2"));
            StringAssert.Contains(LaunchDiagnosis.Record(DateTime.Now, "p", LaunchOutcome.ClosedEarly, LoaderStop.Unknown, null, "attach refused (5)", null), "attach refused (5)");
        }

        [TestMethod]
        public void Prefixes_and_blank_rows_are_trimmed()
        {
            Assert.AreEqual("Profile loaded.", LaunchDiagnosis.StripPrefix("[11/04/26 11:19:01] Profile loaded."));
            Assert.AreEqual("Closing...", LaunchDiagnosis.StripPrefix("[a][b] Closing..."));
            Assert.AreEqual("a\n\nb", LoaderConsole.Tidy(new[] { "a   ", "", "b", "", "   " }));
            Assert.AreEqual("", LoaderConsole.Tidy(null));
            CollectionAssert.AreEqual(new[] { "c", "d" }, LaunchDiagnosis.LastLines("a\nb\n\nc\nd\n", 2));
        }

        [TestMethod]
        public void Missing_game_files_and_dlls_beside_the_loader_are_listed()
        {
            var game = Path.Combine(_root, "FINAL FANTASY XI");
            Directory.CreateDirectory(Path.Combine(game, "ROM", "0"));
            foreach (var f in new[] { "FFXi.dll", "FFXiVersions.dll", "VTABLE.DAT", "FTABLE.DAT" })
                File.WriteAllBytes(Path.Combine(game, f), new byte[] { 0 });
            CollectionAssert.AreEqual(new[] { "FFXiMain.dll" }, LaunchDiagnosis.MissingGameFiles(game));
            Directory.Delete(Path.Combine(game, "ROM"), true);
            CollectionAssert.AreEqual(new[] { "FFXiMain.dll", "ROM" }, LaunchDiagnosis.MissingGameFiles(game));
            Assert.AreEqual(0, LaunchDiagnosis.MissingGameFiles(null).Count);

            var boot = Path.Combine(_root, "bootloader");
            Directory.CreateDirectory(boot);
            File.WriteAllBytes(Path.Combine(boot, "d3d8.dll"), new byte[] { 0 });
            File.WriteAllBytes(Path.Combine(boot, "dinput8.dll"), new byte[] { 0 });
            File.WriteAllBytes(Path.Combine(boot, "d3d9.dll.off"), new byte[] { 0 });
            CollectionAssert.AreEqual(new[] { "d3d8.dll", "dinput8.dll" }, LaunchDiagnosis.LoadedBesideLoader(boot));
            Assert.AreEqual(0, LaunchDiagnosis.LoadedBesideLoader(Path.Combine(_root, "nope")).Count);
        }

        [TestMethod]
        public void The_bug_report_carries_the_last_launch_and_the_checks()
        {
            var data = Path.Combine(_root, "data");
            Directory.CreateDirectory(data);
            File.WriteAllText(Path.Combine(data, "launcher.log"), "x");
            File.WriteAllText(LaunchDiagnosis.LastLaunchPath(data), GameQuitWindow);
            var zip = Path.Combine(_root, "r.zip");
            var names = BugReport.Build(zip, data, null, null, "0.2.57", new DateTime(2026, 11, 4), "ok: checks ran --pass hunter2");
            CollectionAssert.Contains(names, "launcher/last-launch.txt");
            CollectionAssert.Contains(names, "checks.txt");
            using (var z = ZipFile.OpenRead(zip))
            using (var r = new StreamReader(z.GetEntry("checks.txt").Open()))
            {
                var text = r.ReadToEnd();
                StringAssert.Contains(text, "checks ran");
                Assert.IsFalse(text.Contains("hunter2"));
            }
            // without checks, as before
            var names2 = BugReport.Build(Path.Combine(_root, "r2.zip"), data, null, null, "0.2.57", new DateTime(2026, 11, 4));
            Assert.IsFalse(names2.Contains("checks.txt"));
        }
    }
}
