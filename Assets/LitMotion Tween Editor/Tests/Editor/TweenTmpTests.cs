#if LMTE_SUPPORT_TMP
using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEngine;

namespace LitMotion.TweenEditor.Tests
{
    /// <summary>
    /// Covers the two TMP-only types: progressive reveal and per-character animation.
    /// </summary>
    /// <remarks>
    /// These need TMP Essential Resources, because without <c>TMP_Settings</c> TextMeshPro
    /// cannot lay out a single character and every assertion here would be vacuous. The fixture
    /// says so and skips rather than passing on an empty text.
    /// </remarks>
    public sealed class TweenTmpTests
    {
        GameObject canvas;
        GameObject owner;
        TMP_Text text;
        readonly List<MotionHandle> handles = new();
        readonly List<MotionHandle> allHandles = new();

        [SetUp]
        public void SetUp()
        {
            if (TMP_Settings.defaultFontAsset == null)
            {
                Assert.Ignore("TMP Essential Resources are not imported in this project.");
            }

            handles.Clear();

            // A canvas-less TextMeshProUGUI generates no geometry, so every character count
            // would read zero and the assertions below would be vacuous.
            canvas = new GameObject("canvas", typeof(Canvas));
            owner = new GameObject("tmp", typeof(RectTransform));
            owner.transform.SetParent(canvas.transform, false);

            text = owner.AddComponent<TextMeshProUGUI>();
            text.text = "LitMotion";
            text.ForceMeshUpdate();

            Assert.AreEqual(9, text.textInfo.characterCount, "TMP did not lay the text out");
        }

        [TearDown]
        public void TearDown()
        {
            for (var i = 0; i < allHandles.Count; i++)
            {
                if (allHandles[i].IsActive()) allHandles[i].Cancel();
            }

            handles.Clear();
            allHandles.Clear();
            if (canvas != null) Object.DestroyImmediate(canvas);
        }

        int BuildPreserved(TweenStep step, out string error)
        {
            handles.Clear();
            var count = TweenStepBuilder.Build(step, owner, null, handles, out error);

            for (var i = 0; i < handles.Count; i++)
            {
                handles[i].Preserve();

                // Tracked separately: a test that builds more than once would otherwise
                // orphan the earlier motions, and they complete against a destroyed object.
                allHandles.Add(handles[i]);
            }

            return count;
        }

        void SetTime(float seconds)
        {
            for (var i = 0; i < handles.Count; i++)
            {
                var handle = handles[i];
                handle.Time = seconds;
            }
        }

        static TweenStep Step(TweenType type)
        {
            return new TweenStep
            {
                Type = type,
                Enabled = true,
                Duration = 1f,
                Ease = Ease.Linear,
                FromCurrent = false,
            };
        }

        // --- TextReveal ---

        [Test]
        public void RevealCountsTheUnitsOfTheLiveText()
        {
            Assert.AreEqual(9, TweenChannelAccessor.CountUnits(text, TweenTextUnit.Characters));
            Assert.AreEqual(1, TweenChannelAccessor.CountUnits(text, TweenTextUnit.Words));
        }

        [Test]
        public void RevealIsCarriedAsAFractionOfTheText()
        {
            // Endpoints are 0-1 rather than a character count, so the same step keeps working
            // when the string changes length.
            var step = Step(TweenType.TextReveal);
            step.To = new Vector4(1f, 0f, 0f, 0f);

            Assert.AreEqual(1, BuildPreserved(step, out var error), error);

            SetTime(0f);
            Assert.AreEqual(0, text.maxVisibleCharacters);

            SetTime(0.5f);
            Assert.AreEqual(4, text.maxVisibleCharacters, "half of nine characters, rounded");
        }

        [Test]
        public void AFullRevealLeavesTheTextUnbounded()
        {
            // Writing the current character count back would clip the text the moment it grew.
            var step = Step(TweenType.TextReveal);
            step.To = new Vector4(1f, 0f, 0f, 0f);

            Assert.AreEqual(1, BuildPreserved(step, out _));

            SetTime(1f);
            Assert.AreEqual(int.MaxValue, text.maxVisibleCharacters);
        }

        [Test]
        public void RevealReadsBackAsAClampedFraction()
        {
            // TMP parks maxVisibleCharacters at int.MaxValue, which must read as 1, not as a
            // number far past the end of the text.
            text.maxVisibleCharacters = int.MaxValue;
            var context = TweenChannelContext.For(Step(TweenType.TextReveal));

            Assert.IsTrue(TweenChannelAccessor.TryRead(TweenChannelKey.TmpMaxVisible, text, context, out var value));
            Assert.AreEqual(1f, value.x, 1e-4f);

            text.maxVisibleCharacters = 0;
            Assert.IsTrue(TweenChannelAccessor.TryRead(TweenChannelKey.TmpMaxVisible, text, context, out value));
            Assert.AreEqual(0f, value.x, 1e-4f);
        }

        [Test]
        public void RevealHonoursTheUnitOption()
        {
            var step = Step(TweenType.TextReveal);
            step.TextUnit = TweenTextUnit.Words;
            step.To = new Vector4(1f, 0f, 0f, 0f);

            Assert.AreEqual(1, BuildPreserved(step, out _));

            SetTime(0f);
            Assert.AreEqual(0, text.maxVisibleWords);
        }

        // --- TMPCharacter ---

        [Test]
        public void ANegativeIndexBuildsOneMotionPerCharacter()
        {
            // This is what turns a single step into a wave.
            var step = Step(TweenType.TMPCharacter);
            step.CharacterIndex = -1;
            step.Stagger = 0.05f;
            step.To = new Vector4(0f, 20f, 0f, 0f);

            var built = BuildPreserved(step, out var error);

            Assert.AreEqual(9, built, error);
        }

        [Test]
        public void AFixedIndexBuildsExactlyOneMotion()
        {
            var step = Step(TweenType.TMPCharacter);
            step.CharacterIndex = 3;

            Assert.AreEqual(1, BuildPreserved(step, out var error), error);
        }

        [Test]
        public void AnIndexPastTheEndSaysHowManyCharactersThereAre()
        {
            var step = Step(TweenType.TMPCharacter);
            step.CharacterIndex = 99;

            Assert.AreEqual(0, BuildPreserved(step, out var error));
            StringAssert.Contains("99", error);
            StringAssert.Contains("9", error);
        }

        [Test]
        public void EmptyTextIsReportedRatherThanAnimatingNothing()
        {
            text.text = string.Empty;
            text.ForceMeshUpdate();

            var step = Step(TweenType.TMPCharacter);
            step.CharacterIndex = -1;

            Assert.AreEqual(0, BuildPreserved(step, out var error));
            StringAssert.Contains("no characters", error);
        }

        [Test]
        public void EveryPerCharacterChannelBuilds()
        {
            foreach (TweenTMPCharChannel channel in System.Enum.GetValues(typeof(TweenTMPCharChannel)))
            {
                var step = Step(TweenType.TMPCharacter);
                step.CharacterIndex = 0;
                step.TMPCharChannel = channel;

                Assert.AreEqual(1, BuildPreserved(step, out var error), channel + ": " + error);
            }
        }

        [Test]
        public void PerCharacterStateIsSnapshottedAsAMeshRefresh()
        {
            // There is no readable property behind it, so the snapshot entry exists purely to
            // force a rebuild on restore.
            var animation = new TweenAnimation();
            animation.Steps.Clear();

            var step = Step(TweenType.TMPCharacter);
            step.CharacterIndex = 0;
            animation.Steps.Add(step);

            var snapshot = new TweenValueSnapshot();
            snapshot.CaptureAnimation(animation, owner);

            Assert.AreEqual(1, snapshot.Count);
            Assert.DoesNotThrow(() => snapshot.Restore());
        }
    }
}
#endif
