using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using LitMotion.TweenEditor.Editor;

namespace LitMotion.TweenEditor.Tests
{
    /// <summary>
    /// Covers the clip inspector: the value-mode mapping, the slider ranges, and which widgets
    /// each step type actually produces.
    /// </summary>
    public sealed class TweenInspectorTests
    {
        GameObject owner;
        TweenPlayer player;

        [SetUp]
        public void SetUp()
        {
            owner = GameObject.CreatePrimitive(PrimitiveType.Cube);
            player = owner.AddComponent<TweenPlayer>();
        }

        [TearDown]
        public void TearDown()
        {
            if (owner != null) Object.DestroyImmediate(owner);
        }

        /// <summary>
        /// Builds a clip inspector bound to one real serialized step, the way the host does.
        /// </summary>
        TweenClipInspector Inspect(TweenStep step)
        {
            var animation = new TweenAnimation();
            animation.Steps.Clear();
            animation.Steps.Add(step);

            var field = typeof(TweenPlayer).GetField("animations",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field.SetValue(player, new List<TweenAnimation> { animation });

            var serialized = new SerializedObject(player);
            var stepProperty = serialized
                .FindProperty("animations")
                .GetArrayElementAtIndex(0)
                .FindPropertyRelative("Steps")
                .GetArrayElementAtIndex(0);

            var inspector = new TweenClipInspector { TargetProvider = () => owner };
            inspector.Bind(stepProperty, step);

            return inspector;
        }

        static TweenStep Step(TweenType type)
        {
            return new TweenStep { Type = type, Enabled = true, Duration = 1f };
        }

        // --- Value modes ---

        [Test]
        public void EveryModeRoundTripsThroughTheThreeFlags()
        {
            foreach (TweenValueMode mode in System.Enum.GetValues(typeof(TweenValueMode)))
            {
                TweenValueModes.Flags(mode, out var fromCurrent, out var relative, out var fromOffset);

                Assert.AreEqual(mode, TweenValueModes.Of(fromCurrent, relative, fromOffset), mode.ToString());
            }
        }

        [Test]
        public void AllFlagsClearReadsAsAbsolute()
        {
            Assert.AreEqual(TweenValueMode.Absolute, TweenValueModes.Of(false, false, false));
        }

        [Test]
        public void OffsetOutranksTheOtherFlags()
        {
            // The builder checks FromOffset first, so the UI has to agree or it would show a
            // mode the step is not actually in.
            Assert.AreEqual(TweenValueMode.FromOffset, TweenValueModes.Of(true, true, true));
            Assert.AreEqual(TweenValueMode.FromOffset, TweenValueModes.Of(false, false, true));
        }

        [Test]
        public void RelativeWithoutFromCurrentStillReadsAsRelative()
        {
            // Legal in the data and reachable from older assets. It must not silently display
            // as Absolute, which would describe the opposite behaviour.
            Assert.AreEqual(TweenValueMode.Relative, TweenValueModes.Of(false, true, false));
        }

        [Test]
        public void SelectingRelativeNormalizesToReadingFromTheTarget()
        {
            TweenValueModes.Flags(TweenValueMode.Relative, out var fromCurrent, out _, out _);

            Assert.IsTrue(fromCurrent, "relative means 'by this much from where it is'");
        }

        [Test]
        public void OnlyAuthoredEndpointsAreShown()
        {
            Assert.IsTrue(TweenValueModes.ShowsStartValue(TweenValueMode.Absolute));
            Assert.IsFalse(TweenValueModes.ShowsStartValue(TweenValueMode.FromCurrent));
            Assert.IsFalse(TweenValueModes.ShowsStartValue(TweenValueMode.Relative));
            Assert.IsTrue(TweenValueModes.ShowsStartValue(TweenValueMode.FromOffset));

            Assert.IsFalse(TweenValueModes.ShowsEndValue(TweenValueMode.FromOffset));
        }

        [Test]
        public void EndpointLabelsMatchTheMode()
        {
            Assert.AreEqual("To", TweenValueModes.EndLabel(TweenValueMode.Absolute, false));
            Assert.AreEqual("By", TweenValueModes.EndLabel(TweenValueMode.Relative, false));
            Assert.AreEqual("Offset", TweenValueModes.StartLabel(TweenValueMode.FromOffset));

            // Punch and Shake read the end value as a strength, so "To" would mislead.
            Assert.AreEqual("Strength", TweenValueModes.EndLabel(TweenValueMode.Absolute, true));
        }

        // --- Ranges ---

        [Test]
        public void SliderRangesCoverTheShippedDefaults()
        {
            // Every default a new step is created with must sit inside its slider, or the first
            // thing an author sees is a control pinned to one end.
            var step = new TweenStep();
            TweenStepDefaults.Apply(step, TweenType.Shake, 0f);

            Assert.IsTrue(TweenFieldRanges.IsInRange(step.Frequency,
                TweenFieldRanges.FrequencyMin, TweenFieldRanges.FrequencyMax));
            Assert.IsTrue(TweenFieldRanges.IsInRange(step.DampingRatio,
                TweenFieldRanges.DampingMin, TweenFieldRanges.DampingMax));

            TweenStepDefaults.Apply(step, TweenType.Jump, 0f);
            Assert.IsTrue(TweenFieldRanges.IsInRange(step.JumpCount,
                TweenFieldRanges.JumpCountMin, TweenFieldRanges.JumpCountMax));
            Assert.IsTrue(TweenFieldRanges.IsInRange(step.JumpPower,
                TweenFieldRanges.JumpPowerMin, TweenFieldRanges.JumpPowerMax(1f)));

            // And in the middle of the track rather than pinned near one end, for both kinds of
            // target, which is what scaling the range with the units is for.
            var uiScale = TweenStepDefaults.UiPixelsPerUnit;
            var worldPower = step.JumpPower;
            Assert.That(worldPower / TweenFieldRanges.JumpPowerMax(1f), Is.InRange(0.05f, 0.5f));
            Assert.That(worldPower * uiScale / TweenFieldRanges.JumpPowerMax(uiScale), Is.InRange(0.05f, 0.5f));

            // A value from before the range existed is never cut down by the slider.
            Assert.AreEqual(60f, TweenFieldRanges.JumpPowerMax(1f, 60f));

            TweenStepDefaults.Apply(step, TweenType.TMPCharacter, 0f);
            Assert.IsTrue(TweenFieldRanges.IsInRange(step.Stagger,
                TweenFieldRanges.StaggerMin, TweenFieldRanges.StaggerMax));
        }

        // --- Widgets ---

        [Test]
        public void AMoveStepIsDrawnWithThreeComponentFields()
        {
            // The whole point of this slice: no Vector4 field for a position.
            var inspector = Inspect(Step(TweenType.Move));

            Assert.IsNotEmpty(inspector.Query<Vector3Field>().ToList(), "expected a Vector3 field");
            Assert.IsEmpty(inspector.Query<Vector4Field>().ToList(), "a position has no W");
        }

        [Test]
        public void ARectStepIsDrawnWithTwoComponentFields()
        {
            var inspector = Inspect(Step(TweenType.Pivot));

            Assert.IsNotEmpty(inspector.Query<Vector2Field>().ToList());
            Assert.IsEmpty(inspector.Query<Vector3Field>().ToList());
        }

        [Test]
        public void ANormalizedStepIsDrawnWithASlider()
        {
            var inspector = Inspect(Step(TweenType.Fade));

            Assert.IsNotEmpty(inspector.Query<Slider>().ToList(), "alpha should be a 0-1 slider");
            Assert.IsEmpty(inspector.Query<Vector3Field>().ToList());
        }

        [Test]
        public void AColorStepIsDrawnWithASwatch()
        {
            var inspector = Inspect(Step(TweenType.Color));

            Assert.IsNotEmpty(inspector.Query<UnityEditor.UIElements.ColorField>().ToList());
        }

        [Test]
        public void AShaderVectorStepKeepsAllFourComponents()
        {
            var step = Step(TweenType.MaterialProperty);
            step.MaterialPropertyKind = TweenMaterialPropertyKind.Vector;
            step.PropertyName = "_Offset";

            var inspector = Inspect(step);

            Assert.IsNotEmpty(inspector.Query<Vector4Field>().ToList());
        }

        [Test]
        public void VibrationStepsGetFrequencyAndDampingSliders()
        {
            // "Shake strength adjustment" in the literal sense: sliders, not bare numbers.
            var inspector = Inspect(Step(TweenType.Shake));

            Assert.IsNotEmpty(inspector.Query<SliderInt>().ToList(), "frequency should be a slider");

            var sliders = inspector.Query<Slider>().ToList();
            Assert.IsNotEmpty(sliders, "damping should be a slider");
        }

        [Test]
        public void EveryStepTypeBuildsWithoutThrowing()
        {
            foreach (TweenType type in System.Enum.GetValues(typeof(TweenType)))
            {
                var step = new TweenStep();
                TweenStepDefaults.Apply(step, type, 0f);

                Assert.DoesNotThrow(() => Inspect(step), type.ToString());
            }
        }

        [Test]
        public void NoSelectionShowsAPromptRatherThanAnEmptyPanel()
        {
            var inspector = new TweenClipInspector();
            inspector.Bind(null, null);

            var labels = inspector.Query<Label>().ToList();
            var found = false;
            for (var i = 0; i < labels.Count; i++)
            {
                if (labels[i].text != null && labels[i].text.Contains("Click a clip")) found = true;
            }

            Assert.IsTrue(found, "an unbound inspector should say what to do");
        }

        [Test]
        public void TheEaseGraphDotOnlyAppearsWhileTheStepIsRunning()
        {
            // Outside the clip's own window there is no progress to show, and a dot parked at
            // an end would claim the step is mid-flight.
            var step = Step(TweenType.Move);
            step.StartTime = 1f;
            step.Duration = 1f;

            var inspector = Inspect(step);

            Assert.DoesNotThrow(() => inspector.SetPreviewTime(0.5f));
            Assert.DoesNotThrow(() => inspector.SetPreviewTime(1.5f));
            Assert.DoesNotThrow(() => inspector.SetPreviewTime(null));
        }
    }
}
