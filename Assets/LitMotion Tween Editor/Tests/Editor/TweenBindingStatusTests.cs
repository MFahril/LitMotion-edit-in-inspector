using NUnit.Framework;
using UnityEngine;
using LitMotion.TweenEditor.Editor;

namespace LitMotion.TweenEditor.Tests
{
    /// <summary>
    /// Covers the up-front binding check behind the clip badges and the "7 OK · 1 failing"
    /// readout, and the fix buttons offered beside a failure.
    /// </summary>
    public sealed class TweenBindingStatusTests
    {
        GameObject cube;
        GameObject panel;

        [SetUp]
        public void SetUp()
        {
            cube = GameObject.CreatePrimitive(PrimitiveType.Cube);

            // A RectTransform is what makes an object UI, which is what the UI-only fixes need.
            panel = new GameObject("panel", typeof(RectTransform));
        }

        [TearDown]
        public void TearDown()
        {
            if (cube != null) Object.DestroyImmediate(cube);
            if (panel != null) Object.DestroyImmediate(panel);
        }

        static TweenStep Step(TweenType type)
        {
            var step = new TweenStep();
            TweenStepDefaults.Apply(step, type, 0f);
            return step;
        }

        static TweenAnimation Animation(params TweenStep[] steps)
        {
            var animation = new TweenAnimation { Id = "Test" };
            animation.Steps.Clear();
            animation.Steps.AddRange(steps);
            return animation;
        }

        [Test]
        public void ATransformStepBindsOnAnyObject()
        {
            Assert.IsTrue(TweenBindingStatus.Check(Step(TweenType.Move), cube, out var message));
            Assert.IsNull(message);
        }

        [Test]
        public void AFadeOnA3DObjectFailsWithTheRequirement()
        {
            Assert.IsFalse(TweenBindingStatus.Check(Step(TweenType.Fade), cube, out var message));
            StringAssert.Contains("CanvasGroup", message);
        }

        [Test]
        public void DisabledAndTimelineStepsAlwaysPass()
        {
            var fade = Step(TweenType.Fade);
            fade.Enabled = false;

            Assert.IsTrue(TweenBindingStatus.Check(fade, cube, out _));
            Assert.IsTrue(TweenBindingStatus.Check(Step(TweenType.Interval), cube, out _));
            Assert.IsTrue(TweenBindingStatus.Check(Step(TweenType.Callback), null, out _));
        }

        [Test]
        public void TheReportCountsOkAndFailingSteps()
        {
            var report = TweenBindingStatus.Evaluate(
                Animation(Step(TweenType.Move), Step(TweenType.Scale), Step(TweenType.Fade)), cube);

            Assert.AreEqual(3, report.Binding);
            Assert.AreEqual(2, report.Ok);
            Assert.AreEqual(1, report.Failing);
            Assert.AreEqual("2 OK · 1 failing", report.Summary);

            Assert.IsNotNull(report.For(2), "the Fade at index 2 is the failure");
            Assert.IsNull(report.For(0));
        }

        [Test]
        public void AllPassingReadsAsAllOk()
        {
            var report = TweenBindingStatus.Evaluate(Animation(Step(TweenType.Move), Step(TweenType.Rotate)), cube);

            Assert.AreEqual("All 2 OK", report.Summary);
            Assert.AreEqual(0, report.Issues.Count);
        }

        [Test]
        public void WithNoTargetNothingIsChecked()
        {
            // A preset with no Preview On object: every step would "fail", which says nothing.
            var report = TweenBindingStatus.Evaluate(Animation(Step(TweenType.Fade)), null);

            Assert.IsFalse(report.HasTarget);
            Assert.AreEqual(0, report.Failing);
            Assert.AreEqual("No target", report.Summary);
        }

        [Test]
        public void ACustomStepIsACaveatNotAFailure()
        {
            var report = TweenBindingStatus.Evaluate(Animation(Step(TweenType.Custom)), cube);

            Assert.AreEqual(0, report.Failing);
            Assert.AreEqual(1, report.Issues.Count);
            Assert.IsFalse(report.Issues[0].BlocksBinding);
            StringAssert.Contains("cannot put back", report.Issues[0].Message);
        }

        [Test]
        public void AMaterialStepWithoutAPropertyFails()
        {
            var material = new Material(Shader.Find("Unlit/Color"));
            cube.GetComponent<MeshRenderer>().sharedMaterial = material;

            try
            {
                var step = Step(TweenType.MaterialProperty);
                Assert.IsFalse(TweenBindingStatus.Check(step, cube, out var message));
                StringAssert.Contains("shader property", message);

                step.PropertyName = "_Color";
                step.MaterialPropertyKind = TweenMaterialPropertyKind.Color;
                Assert.IsTrue(TweenBindingStatus.Check(step, cube, out _));

                step.PropertyName = "_DoesNotExist";
                Assert.IsFalse(TweenBindingStatus.Check(step, cube, out message));
                StringAssert.Contains("_DoesNotExist", message);
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        // --- Fixes ---

        [Test]
        public void AUiFadeOffersACanvasGroupAndTheFixWorks()
        {
            var fade = Step(TweenType.Fade);
            Assert.IsFalse(TweenBindingStatus.Check(fade, panel, out _));

            var fixes = TweenStepFixes.For(fade, panel);
            Assert.AreEqual(1, fixes.Count);
            StringAssert.Contains("Canvas Group", fixes[0].Label);

            fixes[0].Apply();

            Assert.IsNotNull(panel.GetComponent<CanvasGroup>());
            Assert.IsTrue(TweenBindingStatus.Check(fade, panel, out _));
        }

        [Test]
        public void A3DFadeOffersNoCanvasGroup()
        {
            // A CanvasGroup would make the step bind and then change nothing on screen.
            Assert.AreEqual(0, TweenStepFixes.For(Step(TweenType.Fade), cube).Count);
        }

        [Test]
        public void AnAudioStepOffersAnAudioSource()
        {
            var volume = Step(TweenType.AudioVolume);
            var fixes = TweenStepFixes.For(volume, cube);

            Assert.AreEqual(1, fixes.Count);
            fixes[0].Apply();

            Assert.IsNotNull(cube.GetComponent<AudioSource>());
            Assert.IsTrue(TweenBindingStatus.Check(volume, cube, out _));
        }

        [Test]
        public void NoFixIsOfferedForAnAmbiguousRequirement()
        {
            // Turning a 3D object into a UI element is not a repair.
            Assert.AreEqual(0, TweenStepFixes.For(Step(TweenType.Pivot), cube).Count);
        }

        [Test]
        public void FixesAreAttachedToFailingIssues()
        {
            var report = TweenBindingStatus.Evaluate(Animation(Step(TweenType.AudioPitch)), cube);

            Assert.AreEqual(1, report.Failing);
            Assert.AreEqual(1, report.Issues[0].Fixes.Count);
        }
    }
}
