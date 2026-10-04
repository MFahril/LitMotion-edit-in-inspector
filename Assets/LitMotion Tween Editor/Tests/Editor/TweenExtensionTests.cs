using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using LitMotion.TweenEditor.Editor;

namespace LitMotion.TweenEditor.Tests
{
    /// <summary>
    /// Covers user-defined tween channels: registration, description, building, masking, preview
    /// and exact restore, and what happens when a channel goes missing.
    /// </summary>
    /// <remarks>
    /// The channels here are registered by hand rather than with the attribute, deliberately: an
    /// attributed class in the test assembly would be found by the scan and show up in every
    /// author's add menu.
    /// </remarks>
    public sealed class TweenExtensionTests
    {
        const float Tolerance = 1e-4f;
        const string RangeId = "lmte.tests.light-range";
        const string CenterId = "lmte.tests.box-center";

        /// <summary>A float channel on a component no built-in type animates.</summary>
        sealed class LightRangeChannel : ITweenExtensionChannel
        {
            public string DisplayName => "Light Range";
            public TweenValueShape Shape => TweenValueShape.Float;
            public string Unit => "m";

            public bool TryResolve(TweenStep step, GameObject gameObject, out Object target, out string error)
            {
                target = gameObject.GetComponent<Light>();
                error = target == null ? "Requires a Light." : null;
                return target != null;
            }

            public bool TryRead(Object target, out Vector4 value)
            {
                value = Vector4.zero;
                if (target is not Light light) return false;
                value.x = light.range;
                return true;
            }

            public bool TryWrite(Object target, Vector4 value)
            {
                if (target is not Light light) return false;
                light.range = value.x;
                return true;
            }
        }

        /// <summary>A vector channel, so axis masking has components to mask.</summary>
        sealed class BoxCenterChannel : ITweenExtensionChannel
        {
            public string DisplayName => "Box Center";
            public TweenValueShape Shape => TweenValueShape.Vector3;
            public string Unit => "";

            public bool TryResolve(TweenStep step, GameObject gameObject, out Object target, out string error)
            {
                target = gameObject.GetComponent<BoxCollider>();
                error = target == null ? "Requires a BoxCollider." : null;
                return target != null;
            }

            public bool TryRead(Object target, out Vector4 value)
            {
                value = Vector4.zero;
                if (target is not BoxCollider box) return false;
                value = box.center;
                return true;
            }

            public bool TryWrite(Object target, Vector4 value)
            {
                if (target is not BoxCollider box) return false;
                box.center = value;
                return true;
            }
        }

        GameObject owner;
        Light light;
        BoxCollider box;
        readonly List<MotionHandle> handles = new();

        [SetUp]
        public void SetUp()
        {
            TweenExtensionRegistry.Register(RangeId, new LightRangeChannel(), "Tests");
            TweenExtensionRegistry.Register(CenterId, new BoxCenterChannel(), "Tests");

            owner = new GameObject("extension-target");
            light = owner.AddComponent<Light>();
            light.range = 10f;
            box = owner.AddComponent<BoxCollider>();
            box.center = new Vector3(1f, 2f, 3f);
        }

        [TearDown]
        public void TearDown()
        {
            for (var i = 0; i < handles.Count; i++)
            {
                if (handles[i].IsActive()) handles[i].Cancel();
            }

            handles.Clear();
            TweenExtensionRegistry.Unregister(RangeId);
            TweenExtensionRegistry.Unregister(CenterId);

            if (owner != null) Object.DestroyImmediate(owner);
        }

        static TweenStep Extension(string id)
        {
            return new TweenStep
            {
                Type = TweenType.Extension,
                ExtensionId = id,
                Duration = 1f,
                Ease = Ease.Linear,
                FromCurrent = false,
            };
        }

        static TweenAnimation Animation(params TweenStep[] steps)
        {
            var animation = new TweenAnimation { Id = "Test" };
            animation.Steps.Clear();
            animation.Steps.AddRange(steps);
            return animation;
        }

        MotionHandle BuildOne(TweenStep step)
        {
            var built = new List<MotionHandle>();
            var count = TweenStepBuilder.Build(step, owner, null, built, out var error);

            Assert.IsNull(error, error);
            Assert.AreEqual(1, count);

            var handle = built[0];
            handle.Preserve();
            handles.Add(handle);
            return handle;
        }

        // --- Registry ---

        [Test]
        public void RegisteredChannelsCanBeFound()
        {
            Assert.IsTrue(TweenExtensionRegistry.TryGet(RangeId, out var entry));
            Assert.AreEqual("Light Range", entry.DisplayName);
            Assert.AreEqual("Tests", entry.Category);
            Assert.IsInstanceOf<LightRangeChannel>(TweenExtensionRegistry.Find(RangeId));
        }

        [Test]
        public void TheFirstRegistrationOfAnIdWins()
        {
            // A second package must not be able to swap out a channel that steps rely on.
            Assert.IsFalse(TweenExtensionRegistry.Register(RangeId, new BoxCenterChannel()));
            Assert.IsInstanceOf<LightRangeChannel>(TweenExtensionRegistry.Find(RangeId));
        }

        [Test]
        public void EmptyIdsAndNullChannelsAreRefused()
        {
            Assert.IsFalse(TweenExtensionRegistry.Register("", new LightRangeChannel()));
            Assert.IsFalse(TweenExtensionRegistry.Register("lmte.tests.null", null));
            Assert.IsNull(TweenExtensionRegistry.Find(""));
            Assert.IsNull(TweenExtensionRegistry.Find(null));
        }

        [Test]
        public void ChannelsAreListedSortedByCategoryThenName()
        {
            var all = TweenExtensionRegistry.All;

            for (var i = 1; i < all.Count; i++)
            {
                var order = string.CompareOrdinal(all[i - 1].Category, all[i].Category);
                if (order == 0) order = string.CompareOrdinal(all[i - 1].DisplayName, all[i].DisplayName);
                Assert.LessOrEqual(order, 0, all[i - 1].DisplayName + " before " + all[i].DisplayName);
            }
        }

        [Test]
        public void AttributedChannelsAreFoundByTheScan()
        {
            // The sample channels live in the project's own assembly, which the tests cannot
            // reference; they are the scan's real-world fixture when present.
            if (!TweenExtensionRegistry.TryGet("com.litmotion.samples.light-intensity", out var entry))
            {
                Assert.Ignore("The sample extension channels are not in this project.");
            }

            Assert.AreEqual("Lighting", entry.Category);
            Assert.AreEqual(TweenValueShape.Float, entry.Channel.Shape);
        }

        [Test]
        public void ThePlayerScanSkipsAssembliesThatCannotDeclareAChannel()
        {
            // The attribute lives in the runtime assembly, so nothing that does not reference it
            // can carry it; reading those assemblies' types was the bulk of the first-use hitch.
            Assert.IsFalse(TweenExtensionRegistry.MayDefineChannels(typeof(object).Assembly), "mscorlib");
            Assert.IsFalse(TweenExtensionRegistry.MayDefineChannels(typeof(GameObject).Assembly), "UnityEngine");
            Assert.IsFalse(TweenExtensionRegistry.MayDefineChannels(typeof(Assert).Assembly), "NUnit");

            Assert.IsTrue(TweenExtensionRegistry.MayDefineChannels(typeof(TweenStep).Assembly), "the runtime itself");
            Assert.IsTrue(TweenExtensionRegistry.MayDefineChannels(GetType().Assembly), "an assembly referencing it");
        }

        [Test]
        public void ThePlayerScanFindsWhatTheEditorScanFinds()
        {
            // The player cannot use TypeCache, so its own scan must reach the same answer
            // while skipping most of the domain.
            // The same call a player makes, deliberately: this checks the player's scan, and a
            // player has no domain reload for the analyzer's concern to apply to.
#pragma warning disable UAC0005
            var assemblies = System.AppDomain.CurrentDomain.GetAssemblies();
#pragma warning restore UAC0005
            var scanned = TweenExtensionRegistry.ScanAssemblies(assemblies);
            var expected = UnityEditor.TypeCache.GetTypesWithAttribute<TweenExtensionChannelAttribute>();

            CollectionAssert.AreEquivalent(expected, scanned);

            var considered = 0;
            for (var i = 0; i < assemblies.Length; i++)
            {
                if (TweenExtensionRegistry.MayDefineChannels(assemblies[i])) considered++;
            }

            Assert.Less(considered, assemblies.Length / 2,
                "the filter should rule out most of a Unity domain (" + considered + " of " + assemblies.Length + ")");
        }

        [Test]
        public void PrewarmScansWithoutWaitingForAStep()
        {
            var attributed = UnityEditor.TypeCache.GetTypesWithAttribute<TweenExtensionChannelAttribute>().Count;
            if (attributed == 0) Assert.Ignore("No attributed channels in this project to scan for.");

            TweenExtensionRegistry.Rescan();
            var changes = 0;
            void OnChanged() => changes++;
            TweenExtensionRegistry.Changed += OnChanged;

            try
            {
                TweenExtensionRegistry.Prewarm();
            }
            finally
            {
                TweenExtensionRegistry.Changed -= OnChanged;

                // Rescan dropped the fixture's hand registrations; put them back for TearDown.
                TweenExtensionRegistry.Register(RangeId, new LightRangeChannel(), "Tests");
                TweenExtensionRegistry.Register(CenterId, new BoxCenterChannel(), "Tests");
            }

            Assert.AreEqual(0, changes, "the scan itself is not a change anyone needs to hear about");
            Assert.AreEqual(attributed + 2, TweenExtensionRegistry.All.Count,
                "every attributed channel, plus the fixture's two");
        }

        // --- Description ---

        [Test]
        public void StepsAreNamedAfterTheirChannel()
        {
            Assert.AreEqual("Light Range", Extension(RangeId).DisplayName);

            // A missing channel keeps its raw id, so a broken step still says what it was for.
            Assert.AreEqual("gone.channel", Extension("gone.channel").DisplayName);
            Assert.AreEqual("Extension", Extension("").DisplayName);
        }

        [Test]
        public void ChannelInfoComesFromTheChannel()
        {
            var info = TweenChannelInfo.Describe(Extension(RangeId), owner);

            Assert.AreEqual(TweenValueShape.Float, info.Shape);
            Assert.AreEqual("m", info.Unit);
            Assert.AreEqual("Light Range", info.ChannelName);
            Assert.IsTrue(info.IsResolved);
            Assert.IsTrue(info.SupportsRead);

            var vector = TweenChannelInfo.Describe(Extension(CenterId), owner);
            Assert.AreEqual(TweenValueShape.Vector3, vector.Shape);
            Assert.AreEqual("Z", vector.ComponentLabel(2));
        }

        [Test]
        public void GrabAndApplyWorkThroughTheChannel()
        {
            var step = Extension(RangeId);

            Assert.IsTrue(TweenChannelInfo.TryRead(step, owner, out var value));
            Assert.AreEqual(10f, value.x, Tolerance);

            Assert.IsTrue(TweenChannelInfo.TryWrite(step, owner, new Vector4(4f, 0f, 0f, 0f)));
            Assert.AreEqual(4f, light.range, Tolerance);
        }

        // --- Building ---

        [Test]
        public void AFloatChannelInterpolates()
        {
            var step = Extension(RangeId);
            step.From = new Vector4(0f, 0f, 0f, 0f);
            step.To = new Vector4(20f, 0f, 0f, 0f);

            var handle = BuildOne(step);
            handle.Time = 0.25;

            Assert.AreEqual(5f, light.range, Tolerance);
        }

        [Test]
        public void RelativeModeReadsTheLiveValue()
        {
            var step = Extension(RangeId);
            step.FromCurrent = true;
            step.Relative = true;
            step.To = new Vector4(10f, 0f, 0f, 0f);

            var handle = BuildOne(step);
            handle.Time = 1.0;

            Assert.AreEqual(20f, light.range, Tolerance);
        }

        [Test]
        public void AxisMaskingAppliesToVectorChannels()
        {
            var step = Extension(CenterId);
            step.From = Vector4.zero;
            step.To = new Vector4(10f, 10f, 10f, 0f);
            step.Axis = TweenAxis.X;

            var handle = BuildOne(step);
            handle.Time = 1.0;

            // Only X is the step's to write; Y and Z keep their live values.
            Assert.AreEqual(10f, box.center.x, Tolerance);
            Assert.AreEqual(2f, box.center.y, Tolerance);
            Assert.AreEqual(3f, box.center.z, Tolerance);
        }

        [Test]
        public void AMissingChannelReportsInsteadOfThrowing()
        {
            var built = new List<MotionHandle>();
            var count = TweenStepBuilder.Build(Extension("gone.channel"), owner, null, built, out var error);

            Assert.AreEqual(0, count);
            StringAssert.Contains("gone.channel", error);
        }

        [Test]
        public void AnUnresolvableTargetUsesTheChannelsOwnReason()
        {
            Object.DestroyImmediate(light);

            TweenBindingResolver.Resolve(Extension(RangeId), owner, out var error);

            StringAssert.Contains("Requires a Light.", error);
        }

        // --- Preview ---

        [Test]
        public void APreviewRestoresEveryExtensionChannelExactly()
        {
            var range = Extension(RangeId);
            range.To = new Vector4(50f, 0f, 0f, 0f);

            var center = Extension(CenterId);
            center.To = new Vector4(-5f, -5f, -5f, 0f);

            // Two extension channels on one object share a channel key; the snapshot must still
            // treat them as two things to restore.
            var preview = new TweenPreviewController();
            try
            {
                Assert.IsTrue(preview.Begin(Animation(range, center), owner));
                preview.Scrub(1f);

                Assert.AreEqual(50f, light.range, Tolerance);
                Assert.AreEqual(-5f, box.center.x, Tolerance);
            }
            finally
            {
                preview.Stop();
            }

            Assert.AreEqual(10f, light.range, Tolerance);
            Assert.AreEqual(new Vector3(1f, 2f, 3f), box.center);
        }

        // --- Editor integration ---

        [Test]
        public void ResettingAnExtensionStepKeepsItsChannel()
        {
            var step = Extension(RangeId);
            TweenStepDefaults.Apply(step, TweenType.Extension, 0f);

            Assert.AreEqual(RangeId, step.ExtensionId);
        }

        [Test]
        public void ChangingAwayFromExtensionClearsTheId()
        {
            var step = Extension(RangeId);
            TweenStepDefaults.Apply(step, TweenType.Move, 0f);

            Assert.AreEqual(string.Empty, step.ExtensionId);
        }

        [Test]
        public void ExtensionStepsUseTheExtensionFamily()
        {
            Assert.AreEqual(TweenFamily.Extension, TweenTimelineStyles.FamilyOf(TweenType.Extension));
            Assert.IsTrue(TweenStepFields.For(TweenType.Extension).ExtensionId);
        }

        [Test]
        public void TheBindingCheckAgreesWithTheBuilder()
        {
            Assert.IsTrue(TweenBindingStatus.Check(Extension(RangeId), owner, out _));
            Assert.IsFalse(TweenBindingStatus.Check(Extension("gone.channel"), owner, out var message));
            StringAssert.Contains("gone.channel", message);
        }
    }
}
