using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
#if LMTE_SUPPORT_UGUI
using UnityEngine.UI;
#endif

namespace LitMotion.TweenEditor.Tests
{
    /// <summary>
    /// Covers the text channels: a formatted counter, a scrambled string, and the snapshot
    /// path that puts the original text back.
    /// </summary>
    /// <remarks>
    /// Exercised against uGUI's Text rather than TMP so the tests run in any project. The TMP
    /// specific types (TextReveal, TMPCharacter) are covered here only as far as channel
    /// selection, which is the part that does not need a font asset.
    /// </remarks>
    public sealed class TweenTextTests
    {
        GameObject owner;
        readonly List<MotionHandle> handles = new();
        readonly List<MotionHandle> allHandles = new();

#if LMTE_SUPPORT_UGUI
        Text text;
#endif

        [SetUp]
        public void SetUp()
        {
            handles.Clear();
            owner = new GameObject("text");

#if LMTE_SUPPORT_UGUI
            text = owner.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = "original";
#endif
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
            if (owner != null) Object.DestroyImmediate(owner);
        }

        /// <summary>MotionHandle is a struct, so the list element must be copied to drive it.</summary>
        void SetTime(float seconds)
        {
            var handle = handles[0];
            handle.Time = seconds;
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

        static TweenStep Step(TweenType type)
        {
            return new TweenStep { Type = type, Enabled = true, Duration = 1f, Ease = Ease.Linear };
        }

        // --- Channel selection ---

        [Test]
        public void EachTextTypeSelectsItsOwnChannel()
        {
            var probe = new GameObject("probe");

            try
            {
                Assert.AreEqual(TweenChannelKey.TmpMaxVisible,
                    TweenChannelAccessor.GetChannelKey(Step(TweenType.TextReveal), probe.transform));
                Assert.AreEqual(TweenChannelKey.TextNumber,
                    TweenChannelAccessor.GetChannelKey(Step(TweenType.TextCounter), probe.transform));
                Assert.AreEqual(TweenChannelKey.TextString,
                    TweenChannelAccessor.GetChannelKey(Step(TweenType.TextScramble), probe.transform));
                Assert.AreEqual(TweenChannelKey.TmpCharacter,
                    TweenChannelAccessor.GetChannelKey(Step(TweenType.TMPCharacter), probe.transform));
            }
            finally
            {
                Object.DestroyImmediate(probe);
            }
        }

        [Test]
        public void TheWriteOnlyChannelsReportThatTheyCannotBeRead()
        {
            // This is what lets a step keep a leftover FromCurrent without failing to build.
            Assert.IsFalse(TweenChannelAccessor.SupportsRead(TweenChannelKey.TextNumber));
            Assert.IsFalse(TweenChannelAccessor.SupportsRead(TweenChannelKey.TextString));
            Assert.IsFalse(TweenChannelAccessor.SupportsRead(TweenChannelKey.TmpCharacter));
            Assert.IsTrue(TweenChannelAccessor.SupportsRead(TweenChannelKey.TmpMaxVisible));
            Assert.IsTrue(TweenChannelAccessor.SupportsRead(TweenChannelKey.LocalPosition));
        }

#if LMTE_SUPPORT_UGUI

        // --- Counter ---

        [Test]
        public void CounterWritesTheFormattedNumber()
        {
            var step = Step(TweenType.TextCounter);
            step.FromCurrent = false;
            step.From = Vector4.zero;
            step.To = new Vector4(100f, 0f, 0f, 0f);
            step.TextFormat = "Score: {0:0}";

            Assert.AreEqual(1, BuildPreserved(step, out var error), error);

            SetTime(0f);
            Assert.AreEqual("Score: 0", text.text);

            SetTime(0.5f);
            Assert.AreEqual("Score: 50", text.text);

            SetTime(1f);
            Assert.AreEqual("Score: 100", text.text);
        }

        [Test]
        public void CounterKeepsBuildingWhenFromCurrentIsLeftOverFromAnotherType()
        {
            // Switching a Move step to a counter leaves FromCurrent ticked. Refusing to build
            // there would read as the feature being broken.
            var step = Step(TweenType.TextCounter);
            step.FromCurrent = true;
            step.From = Vector4.zero;
            step.To = new Vector4(10f, 0f, 0f, 0f);
            step.TextFormat = "{0:0}";

            Assert.AreEqual(1, BuildPreserved(step, out var error), error);
            Assert.IsNull(error);

            SetTime(1f);
            Assert.AreEqual("10", text.text);
        }

        [Test]
        public void ABrokenFormatStringIsRefusedRatherThanThrowingEveryFrame()
        {
            var step = Step(TweenType.TextCounter);
            step.TextFormat = "{0:0";

            var context = TweenChannelContext.For(step);

            Assert.DoesNotThrow(() => TweenChannelAccessor.TryWrite(
                TweenChannelKey.TextNumber, text, new Vector4(1f, 0f, 0f, 0f), TweenAxis.All, context));

            Assert.IsFalse(TweenChannelAccessor.TryWrite(
                TweenChannelKey.TextNumber, text, new Vector4(1f, 0f, 0f, 0f), TweenAxis.All, context));

            Assert.AreEqual("original", text.text, "a refused write must leave the text alone");
        }

        // --- Scramble ---

        [Test]
        public void ScrambleLandsOnTheTargetText()
        {
            var step = Step(TweenType.TextScramble);
            step.FromCurrent = false;
            step.TargetText = "finished";
            step.ScrambleMode = ScrambleMode.All;

            Assert.AreEqual(1, BuildPreserved(step, out var error), error);

            SetTime(1f);
            Assert.AreEqual("finished", text.text);
        }

        [Test]
        public void ScrambleGrowsOutOfTheLiveTextWhenAskedTo()
        {
            var step = Step(TweenType.TextScramble);
            step.FromCurrent = true;
            step.TargetText = "done";

            Assert.AreEqual(1, BuildPreserved(step, out var error), error);

            SetTime(0f);
            Assert.AreEqual("original", text.text, "at the start it should still read the live text");
        }

        [Test]
        public void CustomScrambleWithoutCharactersSaysWhatIsMissing()
        {
            // LitMotion throws on ScrambleMode.Custom with no characters, so this has to be
            // caught before the motion is created.
            var step = Step(TweenType.TextScramble);
            step.TargetText = "abc";
            step.ScrambleMode = ScrambleMode.Custom;
            step.ScrambleChars = string.Empty;

            Assert.AreEqual(0, BuildPreserved(step, out var error));
            StringAssert.Contains("scramble characters", error);
        }

        [Test]
        public void CustomScrambleCharactersAreAccepted()
        {
            var step = Step(TweenType.TextScramble);
            step.TargetText = "abc";
            step.ScrambleMode = ScrambleMode.Custom;
            step.ScrambleChars = "#*%";

            Assert.AreEqual(1, BuildPreserved(step, out var error), error);
        }

        [Test]
        public void TextTooLongForTheFixedStringIsReportedNotTruncated()
        {
            var step = Step(TweenType.TextScramble);
            step.FromCurrent = false;
            step.TargetText = new string('x', 600);

            Assert.AreEqual(0, BuildPreserved(step, out var error));
            StringAssert.Contains("500", error);
        }

        // --- Snapshot ---

        [Test]
        public void SnapshotPutsTheOriginalTextBack()
        {
            var animation = new TweenAnimation();
            animation.Steps.Clear();

            var step = Step(TweenType.TextCounter);
            step.FromCurrent = false;
            step.To = new Vector4(42f, 0f, 0f, 0f);
            step.TextFormat = "{0:0}";
            animation.Steps.Add(step);

            var snapshot = new TweenValueSnapshot();
            snapshot.CaptureAnimation(animation, owner);

            Assert.AreEqual(1, snapshot.Count, "the text channel must be captured");

            text.text = "scribbled over";
            snapshot.Restore();

            Assert.AreEqual("original", text.text);
        }

        [Test]
        public void SnapshotCapturesTextOnceAcrossSeveralSteps()
        {
            // Two steps writing the same string must not restore twice, or the second restore
            // would undo nothing and the capture order would stop being meaningful.
            var animation = new TweenAnimation();
            animation.Steps.Clear();
            animation.Steps.Add(Step(TweenType.TextCounter));
            animation.Steps.Add(Step(TweenType.TextCounter));

            var snapshot = new TweenValueSnapshot();
            snapshot.CaptureAnimation(animation, owner);

            Assert.AreEqual(1, snapshot.Count);
        }
#endif
    }
}
