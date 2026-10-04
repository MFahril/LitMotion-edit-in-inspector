using System;
using NUnit.Framework;
using UnityEngine;
using LitMotion.TweenEditor.Editor;

namespace LitMotion.TweenEditor.Tests
{
    /// <summary>
    /// Covers the Preferences-backed settings, and that the timeline reads through them.
    /// </summary>
    /// <remarks>
    /// The settings object is a per-user singleton, so each test saves it first and puts it
    /// back afterwards rather than leaving an author's preferences changed by a test run.
    /// </remarks>
    public sealed class TweenEditorSettingsTests
    {
        string saved;

        static TweenEditorSettings Settings => TweenEditorSettings.instance;

        [SetUp]
        public void SetUp()
        {
            saved = JsonUtility.ToJson(Settings);
            Settings.ResetToDefaults();
        }

        [TearDown]
        public void TearDown()
        {
            JsonUtility.FromJsonOverwrite(saved, Settings);
            Settings.Commit();
        }

        [Test]
        public void DefaultsMatchTheConstantsTheTimelineShippedWith()
        {
            // Nothing may look different until someone changes something.
            Assert.AreEqual(22f, TweenTimelineStyles.TrackHeight);
            Assert.AreEqual(220f, TweenTimelineStyles.DefaultPixelsPerSecond);
            Assert.AreEqual(40f, TweenTimelineStyles.MinPixelsPerSecond);
            Assert.AreEqual(1600f, TweenTimelineStyles.MaxPixelsPerSecond);
            Assert.AreEqual(0.05f, TweenTimelineStyles.SnapInterval, 1e-6f);
            Assert.AreEqual(6f, TweenTimelineStyles.SnapPixelThreshold);
            Assert.AreEqual(320f, TweenTimelineStyles.MaxBodyHeight);
            Assert.IsTrue(Settings.AutoPreview);
            Assert.IsTrue(Settings.SnapEnabled);
            Assert.IsFalse(Settings.CustomClipColors);
        }

        [Test]
        public void TheTimelineReadsTheSnapIntervalFromSettings()
        {
            Settings.SnapInterval = 0.1f;

            var context = new TweenTimelineContext { PixelsPerSecond = 100f };
            context.SnapTargets.Clear();

            Assert.AreEqual(0.3f, context.Snap(0.27f), 1e-5f, "snaps to a 0.1s grid now");
        }

        [Test]
        public void ADegenerateSnapIntervalIsRefused()
        {
            // Everything divides by it.
            Settings.SnapInterval = 0f;
            Assert.Greater(TweenTimelineStyles.SnapInterval, 0f);
        }

        [Test]
        public void ChangingASettingRaisesChanged()
        {
            var raised = 0;
            void OnChanged() => raised++;

            TweenEditorSettings.Changed += OnChanged;
            try
            {
                Settings.ShowHints = false;
                Settings.ShowHints = false;   // no change, no event
            }
            finally
            {
                TweenEditorSettings.Changed -= OnChanged;
            }

            Assert.AreEqual(1, raised);
        }

        [Test]
        public void CustomColoursOverrideTheBuiltInPalette()
        {
            var builtIn = TweenTimelineStyles.ClipColor(TweenType.Move);

            Settings.SetFamilyColor(TweenFamily.Transform, Color.magenta);
            Assert.AreEqual(builtIn, TweenTimelineStyles.ClipColor(TweenType.Move), "off until enabled");

            Settings.CustomClipColors = true;
            Assert.AreEqual(Color.magenta, TweenTimelineStyles.ClipColor(TweenType.Move));
            Assert.AreEqual(Color.magenta, TweenTimelineStyles.ClipColor(TweenType.Scale), "colours are per family");
        }

        [Test]
        public void EveryTypeBelongsToAFamily()
        {
            foreach (TweenType type in Enum.GetValues(typeof(TweenType)))
            {
                Assert.IsTrue(Enum.IsDefined(typeof(TweenFamily), TweenTimelineStyles.FamilyOf(type)), type.ToString());
            }

            Assert.AreEqual(TweenFamily.Timeline, TweenTimelineStyles.FamilyOf(TweenType.Callback));
            Assert.AreEqual(TweenFamily.Vibration, TweenTimelineStyles.FamilyOf(TweenType.Shake));
        }

        [Test]
        public void EveryTypeHasAnIcon()
        {
            foreach (TweenType type in Enum.GetValues(typeof(TweenType)))
            {
                Assert.IsNotNull(TweenTimelineStyles.IconFor(type), type.ToString());
            }
        }

        // --- Preferred defaults for new steps ---

        [Test]
        public void PreferredDurationAndEaseApplyToTheBaseline()
        {
            var step = new TweenStep();
            TweenStepDefaults.Apply(step, TweenType.Fade, 0f);

            TweenTimelineView.ApplyPreferredDefaults(step, 0.8f, Ease.OutBack);

            Assert.AreEqual(0.8f, step.Duration);
            Assert.AreEqual(Ease.OutBack, step.Ease);
        }

        [Test]
        public void ATypeThatChoseItsOwnTimingKeepsIt()
        {
            // A per-character wave sets its own length and ease; a preference must not make it
            // look broken.
            var step = new TweenStep();
            TweenStepDefaults.Apply(step, TweenType.TMPCharacter, 0f);
            var duration = step.Duration;
            var ease = step.Ease;

            TweenTimelineView.ApplyPreferredDefaults(step, 2f, Ease.Linear);

            Assert.AreEqual(duration, step.Duration);
            Assert.AreEqual(ease, step.Ease);
        }

        [Test]
        public void ACallbackStaysZeroLength()
        {
            var step = new TweenStep();
            TweenStepDefaults.Apply(step, TweenType.Callback, 0f);

            TweenTimelineView.ApplyPreferredDefaults(step, 2f, Ease.Linear);

            Assert.AreEqual(0f, step.Duration);
        }
    }
}
