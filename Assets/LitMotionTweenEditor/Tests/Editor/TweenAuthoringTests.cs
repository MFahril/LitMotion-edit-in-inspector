using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using LitMotion.TweenEditor.Editor;

namespace LitMotion.TweenEditor.Tests
{
    /// <summary>
    /// Covers per-type step defaults and the clipboard round trip.
    /// </summary>
    public sealed class TweenAuthoringTests
    {
        const float Tolerance = 1e-4f;

        string savedClipboard;

        [SetUp]
        public void SetUp()
        {
            // The clipboard is shared editor state, so put the user's back afterwards.
            savedClipboard = EditorGUIUtility.systemCopyBuffer;
        }

        [TearDown]
        public void TearDown()
        {
            EditorGUIUtility.systemCopyBuffer = savedClipboard;
        }

        // --- Defaults ---

        [Test]
        public void DefaultsPlaceTheStepAtTheRequestedTime()
        {
            var step = new TweenStep();
            TweenStepDefaults.Apply(step, TweenType.Move, 1.25f);

            Assert.AreEqual(TweenType.Move, step.Type);
            Assert.AreEqual(1.25f, step.StartTime, Tolerance);
            Assert.IsTrue(step.Enabled);
            Assert.AreEqual(1, step.Loops);
        }

        [Test]
        public void DefaultsNeverLeaveAZeroLengthAnimatingStep()
        {
            // A new step must do something visible when previewed. Only the two timeline
            // primitives are allowed to have no duration.
            foreach (TweenType type in System.Enum.GetValues(typeof(TweenType)))
            {
                var step = new TweenStep();
                TweenStepDefaults.Apply(step, type, 0f);

                if (type == TweenType.Callback) continue;

                Assert.Greater(step.Duration, 0f, type + " should have a non-zero duration");
            }
        }

        [Test]
        public void DefaultsNeverLeaveANullCurveOrEvent()
        {
            foreach (TweenType type in System.Enum.GetValues(typeof(TweenType)))
            {
                var step = new TweenStep();
                TweenStepDefaults.Apply(step, type, 0f);

                Assert.IsNotNull(step.CustomCurve, type + " curve");
                Assert.IsNotNull(step.TextFormat, type + " text format");
            }
        }

        [Test]
        public void DefaultsOverwriteValuesCarriedOverFromAnotherType()
        {
            // Unity's InsertArrayElementAtIndex clones the previous element, so a new step
            // arrives carrying the old one's payload. Defaults must fully reset it.
            var step = new TweenStep();
            TweenStepDefaults.Apply(step, TweenType.Move, 0f);
            step.JumpPower = 999f;
            step.Frequency = 77;
            step.Label = "stale";

            TweenStepDefaults.Apply(step, TweenType.Fade, 0f);

            Assert.AreEqual(TweenType.Fade, step.Type);
            Assert.AreEqual(string.Empty, step.Label);
            Assert.AreEqual(1f, step.JumpPower, Tolerance);
            Assert.AreEqual(10, step.Frequency);
        }

        [Test]
        public void JumpDefaultsToLinearBecauseTheArcFollowsEasedProgress()
        {
            var step = new TweenStep();
            TweenStepDefaults.Apply(step, TweenType.Jump, 0f);

            Assert.AreEqual(Ease.Linear, step.Ease);
            Assert.Greater(step.JumpPower, 0f);
        }

        [Test]
        public void CallbackDefaultsToAZeroLengthMarker()
        {
            var step = new TweenStep();
            TweenStepDefaults.Apply(step, TweenType.Callback, 0.5f);

            Assert.AreEqual(0f, step.Duration, Tolerance);
        }

        // --- Clipboard ---

        [Test]
        public void StepSurvivesACopyPasteRoundTrip()
        {
            var source = new TweenStep();
            TweenStepDefaults.Apply(source, TweenType.Shake, 0.75f);
            source.Label = "Impact";
            source.Duration = 0.42f;
            source.Frequency = 17;
            source.DampingRatio = 0.6f;
            source.RandomSeed = 1234u;
            source.Ease = Ease.InOutBack;

            TweenStepClipboard.CopyStep(source);
            Assert.IsTrue(TweenStepClipboard.HasStep);

            var destination = new TweenStep();
            Assert.IsTrue(TweenStepClipboard.PasteInto(destination));

            Assert.AreEqual(TweenType.Shake, destination.Type);
            Assert.AreEqual("Impact", destination.Label);
            Assert.AreEqual(0.75f, destination.StartTime, Tolerance);
            Assert.AreEqual(0.42f, destination.Duration, Tolerance);
            Assert.AreEqual(17, destination.Frequency);
            Assert.AreEqual(0.6f, destination.DampingRatio, Tolerance);
            Assert.AreEqual(1234u, destination.RandomSeed);
            Assert.AreEqual(Ease.InOutBack, destination.Ease);
        }

        [Test]
        public void PasteOverwritesInPlaceRatherThanReplacingTheInstance()
        {
            // The caller holds the destination inside a serialized List, so a swapped reference
            // would be silently discarded.
            var source = new TweenStep();
            TweenStepDefaults.Apply(source, TweenType.Fade, 0f);
            TweenStepClipboard.CopyStep(source);

            var destination = new TweenStep();
            var before = destination;
            TweenStepClipboard.PasteInto(destination);

            Assert.AreSame(before, destination);
            Assert.AreEqual(TweenType.Fade, destination.Type);
        }

        [Test]
        public void ForeignClipboardContentIsIgnored()
        {
            EditorGUIUtility.systemCopyBuffer = "C:/some/path/that/is/not/json.txt";

            Assert.IsFalse(TweenStepClipboard.HasStep);

            var destination = new TweenStep();
            TweenStepDefaults.Apply(destination, TweenType.Move, 0f);

            Assert.IsFalse(TweenStepClipboard.PasteInto(destination), "must refuse foreign content");
            Assert.AreEqual(TweenType.Move, destination.Type, "destination must be untouched");
        }

        [Test]
        public void MalformedJsonIsIgnoredRatherThanThrowing()
        {
            // Carries the marker so it passes the cheap check, but is not valid JSON.
            EditorGUIUtility.systemCopyBuffer = "{\"marker\":\"__LMTE_Step_v1\", broken";

            var destination = new TweenStep();
            Assert.DoesNotThrow(() => TweenStepClipboard.PasteInto(destination));
        }

        [Test]
        public void AnimationSurvivesACopyPasteRoundTrip()
        {
            var source = new TweenAnimation { Id = "Hide", Loops = 3, PlaybackSpeed = 1.5f };
            source.Steps.Clear();

            var a = new TweenStep();
            TweenStepDefaults.Apply(a, TweenType.Fade, 0f);
            source.Steps.Add(a);

            var b = new TweenStep();
            TweenStepDefaults.Apply(b, TweenType.Scale, 0.2f);
            source.Steps.Add(b);

            TweenStepClipboard.CopyAnimation(source);
            Assert.IsTrue(TweenStepClipboard.HasAnimation);

            var destination = new TweenAnimation();
            Assert.IsTrue(TweenStepClipboard.PasteInto(destination));

            Assert.AreEqual("Hide", destination.Id);
            Assert.AreEqual(3, destination.Loops);
            Assert.AreEqual(1.5f, destination.PlaybackSpeed, Tolerance);
            Assert.AreEqual(2, destination.Steps.Count);
            Assert.AreEqual(TweenType.Fade, destination.Steps[0].Type);
            Assert.AreEqual(TweenType.Scale, destination.Steps[1].Type);
            Assert.AreEqual(0.2f, destination.Steps[1].StartTime, Tolerance);

            // Every pasted step must come back with usable reference members.
            for (var i = 0; i < destination.Steps.Count; i++)
            {
                Assert.IsNotNull(destination.Steps[i].CustomCurve, "step " + i + " curve");
                Assert.IsNotNull(destination.Steps[i].OnCallback, "step " + i + " callback");
                Assert.IsNotNull(destination.Steps[i].OnProgress, "step " + i + " progress event");
            }
        }

        [Test]
        public void AStepPayloadIsNotMistakenForAnAnimation()
        {
            var step = new TweenStep();
            TweenStepDefaults.Apply(step, TweenType.Move, 0f);
            TweenStepClipboard.CopyStep(step);

            Assert.IsTrue(TweenStepClipboard.HasStep);
            Assert.IsFalse(TweenStepClipboard.HasAnimation);
        }
    }
}
