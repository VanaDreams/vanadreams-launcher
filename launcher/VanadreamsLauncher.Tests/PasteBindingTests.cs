using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vanadreams.Services;

namespace Vanadreams.Tests
{
    [TestClass]
    public class PasteBindingTests
    {
        private const string Default = "/bind $^v /paste";
        private static string Build(string yours = "", string config = null)
        {
            var entry = new ScriptEntry { Id = "test", Kind = LoadKind.Addon, LoadName = "test" };
            if (config != null) entry.ConfigLines.Add(config);
            return ScriptWriter.Build(new[] { entry }, ScriptWriter.YoursMarker + "\n" + yours);
        }
        [TestMethod]
        public void Fresh_script_has_exactly_one_stock_binding_and_roundtrips()
        {
            var first = Build();
            Assert.AreEqual(1, first.Split(new[] { Default }, StringSplitOptions.None).Length - 1);
            Assert.AreEqual(first, ScriptWriter.Build(new[] { new ScriptEntry { Id = "test", Kind = LoadKind.Addon, LoadName = "test" } }, first));
        }
        [TestMethod]
        public void Custom_ctrl_v_and_existing_stock_binding_are_preserved_without_duplicate()
        {
            foreach (var line in new[] { "/bind ^v /echo mine", "/BIND $^V /echo mine", Default })
            {
                var result = Build(line);
                Assert.AreEqual(line + Environment.NewLine, ScriptWriter.ExtractYours(result));
                Assert.AreEqual(line == Default ? 1 : 0, result.Split(new[] { Default }, StringSplitOptions.None).Length - 1);
            }
        }
        [TestMethod]
        public void Explicit_unbind_policy_is_preserved()
        {
            foreach (var line in new[] { "/unbind ^v", "/unBind $^V", "/unbind all", "/unbind *", "/unbindall" })
            {
                var result = Build(line);
                Assert.IsFalse(result.Contains(Default));
                Assert.AreEqual(line + Environment.NewLine, ScriptWriter.ExtractYours(result));
            }
        }
        [TestMethod]
        public void Catalogue_override_is_not_duplicated()
        {
            foreach (var line in new[] { Default, "/bind ^v /echo mine", "/unbind $^v" })
            {
                var result = Build("", line);
                Assert.AreEqual(line == Default ? 1 : 0, result.Split(new[] { Default }, StringSplitOptions.None).Length - 1);
            }
        }
        [TestMethod]
        public void Comments_and_other_modifiers_do_not_disable_default()
        {
            foreach (var line in new[] { "# /unbind ^v", "/bind ^!v /echo other", "/bind +^v /echo other", "/unbind F12" })
                Assert.IsTrue(Build(line).Contains(Default));
        }
        [TestMethod]
        public void Removing_custom_policy_restores_default_without_changing_other_settings()
        {
            var result = Build("/ambient 255 255 255 255", "/bind F12 /fps");
            Assert.IsTrue(result.Contains(Default));
            Assert.IsTrue(result.Contains("/bind F12 /fps"));
            Assert.AreEqual("/ambient 255 255 255 255" + Environment.NewLine, ScriptWriter.ExtractYours(result));
        }
    }
}
