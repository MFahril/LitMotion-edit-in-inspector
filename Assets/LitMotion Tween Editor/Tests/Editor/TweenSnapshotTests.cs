using NUnit.Framework;
using UnityEngine;

namespace LitMotion.TweenEditor.Tests
{
    /// <summary>
    /// Covers snapshot capture and restore, which is what guarantees an editor preview leaves
    /// the scene exactly as it found it.
    /// </summary>
    public sealed class TweenSnapshotTests
    {
        const float Tolerance = 1e-4f;

        GameObject go;

        [SetUp]
        public void SetUp()
        {
            go = new GameObject("snapshot-target", typeof(CanvasGroup));
            go.transform.localPosition = new Vector3(1f, 2f, 3f);
            go.transform.localScale = new Vector3(2f, 2f, 2f);
            go.GetComponent<CanvasGroup>().alpha = 0.4f;
        }

        [TearDown]
        public void TearDown()
        {
            if (go != null) Object.DestroyImmediate(go);
        }

        static TweenAnimation Animation(params TweenStep[] steps)
        {
            var animation = new TweenAnimation { Id = "Test" };
            animation.Steps.Clear();
            animation.Steps.AddRange(steps);
            return animation;
        }

        static TweenStep Step(TweenType type)
        {
            return new TweenStep { Type = type, Enabled = true, Duration = 1f };
        }

        [Test]
        public void RestorePutsEveryCapturedChannelBack()
        {
            var animation = Animation(Step(TweenType.Move), Step(TweenType.Scale), Step(TweenType.Fade));

            var snapshot = new TweenValueSnapshot();
            snapshot.CaptureAnimation(animation, go);

            // Scribble over everything the animation touches.
            go.transform.localPosition = Vector3.one * 99f;
            go.transform.localScale = Vector3.one * 99f;
            go.GetComponent<CanvasGroup>().alpha = 1f;

            snapshot.Restore();

            Assert.AreEqual(new Vector3(1f, 2f, 3f), go.transform.localPosition);
            Assert.AreEqual(new Vector3(2f, 2f, 2f), go.transform.localScale);
            Assert.AreEqual(0.4f, go.GetComponent<CanvasGroup>().alpha, Tolerance);
        }

        [Test]
        public void ChannelsSharedBySeveralStepsAreCapturedOnce()
        {
            // Move and a position Punch both write local position; capturing twice would mean
            // restoring a value that the first capture had already perturbed.
            var punch = Step(TweenType.Punch);
            punch.Channel = TweenChannel.Position;

            var snapshot = new TweenValueSnapshot();
            snapshot.CaptureAnimation(Animation(Step(TweenType.Move), punch), go);

            Assert.AreEqual(1, snapshot.Count);
        }

        [Test]
        public void DisabledStepsAreNotCaptured()
        {
            var disabled = Step(TweenType.Move);
            disabled.Enabled = false;

            var snapshot = new TweenValueSnapshot();
            snapshot.CaptureAnimation(Animation(disabled), go);

            Assert.IsTrue(snapshot.IsEmpty);
        }

        [Test]
        public void UnbindableStepsAreNotCaptured()
        {
            // No RectTransform on this GameObject, so there is nothing to snapshot.
            var snapshot = new TweenValueSnapshot();
            snapshot.CaptureAnimation(Animation(Step(TweenType.SizeDelta)), go);

            Assert.IsTrue(snapshot.IsEmpty);
        }

        [Test]
        public void TimelinePrimitivesCaptureNothing()
        {
            var snapshot = new TweenValueSnapshot();
            snapshot.CaptureAnimation(
                Animation(Step(TweenType.Interval), Step(TweenType.Callback), Step(TweenType.Custom)), go);

            Assert.IsTrue(snapshot.IsEmpty);
        }

        [Test]
        public void RestoreAndClearLeavesNothingBehind()
        {
            var snapshot = new TweenValueSnapshot();
            snapshot.CaptureAnimation(Animation(Step(TweenType.Move)), go);

            snapshot.RestoreAndClear();

            Assert.IsTrue(snapshot.IsEmpty);
        }

        [Test]
        public void RestoreSurvivesADestroyedTarget()
        {
            var snapshot = new TweenValueSnapshot();
            snapshot.CaptureAnimation(Animation(Step(TweenType.Move)), go);

            Object.DestroyImmediate(go);
            go = null;

            // A preview must tear down cleanly even if the user deleted the object mid-scrub.
            Assert.DoesNotThrow(() => snapshot.Restore());
        }

        [Test]
        public void AnchorsSetToBothCapturesMinAndMax()
        {
            var rect = new GameObject("rect", typeof(RectTransform));
            try
            {
                var step = Step(TweenType.Anchors);
                step.AnchorTarget = TweenAnchorTarget.Both;

                var snapshot = new TweenValueSnapshot();
                snapshot.CaptureAnimation(Animation(step), rect);

                Assert.AreEqual(2, snapshot.Count);
            }
            finally
            {
                Object.DestroyImmediate(rect);
            }
        }

        [Test]
        public void TwoPropertiesOfOneMaterialAreBothRestored()
        {
            // Regression: both steps resolve to the same material and the same channel key, so
            // the second was treated as a duplicate and never captured -- and a preview left that
            // property permanently changed. UI/Default ships with every project and has several
            // float properties, so this does not depend on the render pipeline.
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var material = new Material(Shader.Find("UI/Default"));
            cube.GetComponent<MeshRenderer>().sharedMaterial = material;

            try
            {
                material.SetFloat("_Stencil", 3f);
                material.SetFloat("_ColorMask", 7f);

                var stencil = Step(TweenType.MaterialProperty);
                stencil.PropertyName = "_Stencil";
                var mask = Step(TweenType.MaterialProperty);
                mask.PropertyName = "_ColorMask";

                var snapshot = new TweenValueSnapshot();
                snapshot.CaptureAnimation(Animation(stencil, mask), cube);
                Assert.AreEqual(2, snapshot.Count);

                material.SetFloat("_Stencil", 100f);
                material.SetFloat("_ColorMask", 100f);
                snapshot.Restore();

                Assert.AreEqual(3f, material.GetFloat("_Stencil"), Tolerance);
                Assert.AreEqual(7f, material.GetFloat("_ColorMask"), Tolerance);
            }
            finally
            {
                Object.DestroyImmediate(cube);
                Object.DestroyImmediate(material);
            }
        }
    }
}
