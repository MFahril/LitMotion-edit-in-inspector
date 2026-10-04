using NUnit.Framework;
using UnityEngine;
using LitMotion.TweenEditor.Editor;

namespace LitMotion.TweenEditor.Tests
{
    /// <summary>
    /// Covers the preset workflow added in M4: saving part of a timeline as a preset, the preset
    /// browser's search and thumbnails, and the live samples for text steps.
    /// </summary>
    public sealed class TweenPresetWorkflowTests
    {
        static TweenAnimation Animation(params TweenStep[] steps)
        {
            var animation = new TweenAnimation { Id = "Source" };
            animation.Steps.Clear();
            animation.Steps.AddRange(steps);
            return animation;
        }

        static TweenStep Step(TweenType type, float start, float duration, Ease ease = Ease.OutQuad)
        {
            return new TweenStep { Type = type, StartTime = start, Duration = duration, Ease = ease };
        }

        // --- Save Selection as Preset ---

        [Test]
        public void ExtractingShiftsTheSelectionToStartAtZero()
        {
            var animation = Animation(
                Step(TweenType.Move, 0f, 0.5f),
                Step(TweenType.Fade, 1.2f, 0.3f),
                Step(TweenType.Scale, 1.5f, 0.2f));

            var extracted = TweenTimelineCommands.ExtractSelection(animation, new[] { 2, 1 });

            Assert.AreEqual(2, extracted.Steps.Count);
            Assert.AreEqual(TweenType.Fade, extracted.Steps[0].Type, "timeline order, not click order");
            Assert.AreEqual(0f, extracted.Steps[0].StartTime, 1e-5f);
            Assert.AreEqual(0.3f, extracted.Steps[1].StartTime, 1e-5f, "relative timing survives");
        }

        [Test]
        public void ExtractedStepsAreIndependentCopies()
        {
            var animation = Animation(Step(TweenType.Move, 1f, 0.5f));

            var extracted = TweenTimelineCommands.ExtractSelection(animation, new[] { 0 });
            extracted.Steps[0].Duration = 9f;

            Assert.AreEqual(0.5f, animation.Steps[0].Duration);
            Assert.AreEqual(1f, animation.Steps[0].StartTime, "the source is not shifted");
        }

        [Test]
        public void AnEmptyOrInvalidSelectionExtractsNothing()
        {
            var animation = Animation(Step(TweenType.Move, 0f, 0.5f));

            Assert.AreEqual(0, TweenTimelineCommands.ExtractSelection(animation, new int[0]).Steps.Count);
            Assert.AreEqual(0, TweenTimelineCommands.ExtractSelection(animation, new[] { 7 }).Steps.Count);
            Assert.AreEqual(0, TweenTimelineCommands.ExtractSelection(null, new[] { 0 }).Steps.Count);
        }

        [Test]
        public void SavingStripsSceneTargetsButKeepsMaterialAssets()
        {
            var go = new GameObject("scene-object");
            try
            {
                var animation = Animation(Step(TweenType.Move, 0f, 0.5f), Step(TweenType.Fade, 0f, 0.5f));
                animation.Steps[0].Target = go.transform;

                TweenPresetFiles.StripSceneReferences(animation);

                Assert.IsNull(animation.Steps[0].Target);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // --- Preset browser ---

        [Test]
        public void TheSignatureEaseIsTheLongestStepsEase()
        {
            var animation = Animation(
                Step(TweenType.Fade, 0f, 0.2f, Ease.Linear),
                Step(TweenType.Scale, 0f, 0.6f, Ease.OutBack),
                Step(TweenType.Interval, 0f, 2f, Ease.InExpo));

            // The interval is longest but animates nothing, so it does not characterise the preset.
            Assert.AreEqual(Ease.OutBack, TweenPresetBrowser.SignatureEase(animation, out _));
        }

        [Test]
        public void TheBrowserSearchesNamesTypesAndEases()
        {
            var asset = ScriptableObject.CreateInstance<TweenAnimationAsset>();
            asset.name = "PanelPop";
            asset.SetAnimation(Animation(Step(TweenType.Scale, 0f, 0.4f, Ease.OutBack)));

            try
            {
                var entry = new TweenPresetBrowser.Entry(asset, "UI");

                Assert.IsTrue(TweenPresetBrowser.Matches(entry, ""));
                Assert.IsTrue(TweenPresetBrowser.Matches(entry, "panel"));
                Assert.IsTrue(TweenPresetBrowser.Matches(entry, "ui"));
                Assert.IsTrue(TweenPresetBrowser.Matches(entry, "scale"));
                Assert.IsTrue(TweenPresetBrowser.Matches(entry, "outback"));
                Assert.IsFalse(TweenPresetBrowser.Matches(entry, "shake"));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void TheBrowserFindsTheGeneratedPresets()
        {
            var all = TweenPresetBrowser.FindAll();
            if (all.Count == 0) Assert.Ignore("No presets have been generated in this project.");

            Assert.IsTrue(all.TrueForAll(entry => entry.Asset != null));
        }

        // --- Live samples ---

        [Test]
        public void TheCounterSampleFormatsTheEndValue()
        {
            var step = new TweenStep { Type = TweenType.TextCounter, TextFormat = "Score: {0:N0}", To = new Vector4(1500f, 0f, 0f, 0f) };

            Assert.AreEqual(string.Format("Score: {0:N0}", 1500f), TweenClipInspector.CounterSample(step));

            step.TextFormat = "{0:";
            Assert.AreEqual("invalid format string", TweenClipInspector.CounterSample(step));
        }

        [Test]
        public void TheScrambleSampleRevealsPartOfTheTarget()
        {
            var step = new TweenStep
            {
                Type = TweenType.TextScramble,
                TargetText = "SCRAMBLED!",
                ScrambleMode = ScrambleMode.Uppercase,
            };

            var half = TweenScrambleSample.At(step, string.Empty, 0.5f);
            var done = TweenScrambleSample.At(step, string.Empty, 1f);

            Assert.AreEqual("SCRAMBLED!", done, "the end of the motion is the target text");
            StringAssert.StartsWith("SCRAM", half, "half the text is revealed at the halfway point");
            Assert.AreNotEqual("SCRAMBLED!", half);
        }

        [Test]
        public void TheScrambleSampleExplainsAMissingCustomAlphabet()
        {
            var step = new TweenStep { Type = TweenType.TextScramble, TargetText = "x", ScrambleMode = ScrambleMode.Custom };

            StringAssert.Contains("characters", TweenScrambleSample.At(step, string.Empty));
        }
    }
}
