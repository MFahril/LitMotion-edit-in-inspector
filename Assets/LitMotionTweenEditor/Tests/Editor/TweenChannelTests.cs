using NUnit.Framework;
using UnityEngine;
#if LMTE_SUPPORT_UGUI
using UnityEngine.UI;
#endif

namespace LitMotion.TweenEditor.Tests
{
    /// <summary>
    /// Covers channel resolution, reading, writing and axis masking.
    /// </summary>
    public sealed class TweenChannelTests
    {
        const float Tolerance = 1e-4f;

        GameObject plain;
        GameObject ui;

        [SetUp]
        public void SetUp()
        {
            plain = new GameObject("plain");

            ui = new GameObject("ui", typeof(RectTransform), typeof(CanvasGroup));
        }

        [TearDown]
        public void TearDown()
        {
            if (plain != null) Object.DestroyImmediate(plain);
            if (ui != null) Object.DestroyImmediate(ui);
        }

        static TweenStep Step(TweenType type)
        {
            return new TweenStep { Type = type, Enabled = true, Duration = 1f };
        }

        // --- Channel resolution ---

        [Test]
        public void MoveOnAPlainTransformUsesLocalPosition()
        {
            var step = Step(TweenType.Move);
            step.Space = TweenSpace.Local;

            var key = TweenChannelAccessor.GetChannelKey(step, plain.transform);

            Assert.AreEqual(TweenChannelKey.LocalPosition, key);
        }

        [Test]
        public void MoveOnARectTransformUsesAnchoredPosition()
        {
            // Animating localPosition on a RectTransform fights the anchor layout, so a UI move
            // must resolve to anchoredPosition instead.
            var step = Step(TweenType.Move);
            step.Space = TweenSpace.Local;

            var key = TweenChannelAccessor.GetChannelKey(step, ui.transform);

            Assert.AreEqual(TweenChannelKey.AnchoredPosition3D, key);
        }

        [Test]
        public void WorldSpaceMoveUsesWorldPositionEvenOnARectTransform()
        {
            var step = Step(TweenType.Move);
            step.Space = TweenSpace.World;

            Assert.AreEqual(TweenChannelKey.WorldPosition,
                TweenChannelAccessor.GetChannelKey(step, ui.transform));
        }

        [Test]
        public void PunchOnTheScaleChannelResolvesToLocalScale()
        {
            var step = Step(TweenType.Punch);
            step.Channel = TweenChannel.Scale;

            Assert.AreEqual(TweenChannelKey.LocalScale,
                TweenChannelAccessor.GetChannelKey(step, plain.transform));
        }

        [Test]
        public void FadePrefersCanvasGroupOverGraphic()
        {
            var step = Step(TweenType.Fade);
            var resolved = TweenBindingResolver.Resolve(step, ui, out var error);

            Assert.IsNull(error, error);
            Assert.IsInstanceOf<CanvasGroup>(resolved);
            Assert.AreEqual(TweenChannelKey.CanvasGroupAlpha,
                TweenChannelAccessor.GetChannelKey(step, resolved));
        }

        [Test]
        public void ResolvingAnImpossibleStepReportsWhatWasNeeded()
        {
            // A plain GameObject has no RectTransform, so SizeDelta cannot bind.
            var step = Step(TweenType.SizeDelta);

            var resolved = TweenBindingResolver.Resolve(step, plain, out var error);

            Assert.IsNull(resolved);
            Assert.IsNotNull(error);
            StringAssert.Contains("RectTransform", error);
        }

        // --- Read and write round-trips ---

        [Test]
        public void LocalPositionRoundTrips()
        {
            plain.transform.localPosition = new Vector3(1f, 2f, 3f);

            Assert.IsTrue(TweenChannelAccessor.TryRead(
                TweenChannelKey.LocalPosition, plain.transform, out var read));
            Assert.AreEqual(1f, read.x, Tolerance);
            Assert.AreEqual(2f, read.y, Tolerance);
            Assert.AreEqual(3f, read.z, Tolerance);

            Assert.IsTrue(TweenChannelAccessor.TryWrite(
                TweenChannelKey.LocalPosition, plain.transform,
                new Vector4(4f, 5f, 6f, 0f), TweenAxis.XYZ));

            Assert.AreEqual(new Vector3(4f, 5f, 6f), plain.transform.localPosition);
        }

        [Test]
        public void CanvasGroupAlphaRoundTrips()
        {
            var group = ui.GetComponent<CanvasGroup>();
            group.alpha = 0.25f;

            Assert.IsTrue(TweenChannelAccessor.TryRead(
                TweenChannelKey.CanvasGroupAlpha, group, out var read));
            Assert.AreEqual(0.25f, read.x, Tolerance);

            Assert.IsTrue(TweenChannelAccessor.TryWrite(
                TweenChannelKey.CanvasGroupAlpha, group, new Vector4(0.75f, 0f, 0f, 0f), TweenAxis.All));
            Assert.AreEqual(0.75f, group.alpha, Tolerance);
        }

        // --- Axis masking ---

        [Test]
        public void MaskedWriteLeavesUnselectedAxesAlone()
        {
            plain.transform.localPosition = new Vector3(1f, 2f, 3f);

            TweenChannelAccessor.TryWrite(TweenChannelKey.LocalPosition, plain.transform,
                new Vector4(9f, 9f, 9f, 0f), TweenAxis.X);

            Assert.AreEqual(9f, plain.transform.localPosition.x, Tolerance, "X was selected");
            Assert.AreEqual(2f, plain.transform.localPosition.y, Tolerance, "Y must be preserved");
            Assert.AreEqual(3f, plain.transform.localPosition.z, Tolerance, "Z must be preserved");
        }

        [Test]
        public void ColorChannelsIgnoreAxisMaskingSoAlphaStillAnimates()
        {
            // Regression guard: TweenStep.Axis defaults to XYZ. If a colour channel honoured that
            // mask, alpha (the W component) would silently never be written.
            var step = Step(TweenType.Color);
            step.Axis = TweenAxis.XYZ;

            var axis = TweenChannelAccessor.GetEffectiveAxis(step, TweenChannelKey.GraphicColor);

            Assert.AreEqual(TweenAxis.All, axis);
        }

        [Test]
        public void ScalarChannelsIgnoreAxisMasking()
        {
            var step = Step(TweenType.Fade);
            step.Axis = TweenAxis.Y;

            Assert.AreEqual(TweenAxis.All,
                TweenChannelAccessor.GetEffectiveAxis(step, TweenChannelKey.CanvasGroupAlpha));
        }

        [Test]
        public void AnEmptyAxisMaskFallsBackToAllAxesRatherThanWritingNothing()
        {
            var step = Step(TweenType.Move);
            step.Axis = TweenAxis.None;

            Assert.AreEqual(TweenAxis.XYZ,
                TweenChannelAccessor.GetEffectiveAxis(step, TweenChannelKey.LocalPosition));
        }

        [Test]
        public void TwoDimensionalChannelsNarrowTheMaskToXY()
        {
            var step = Step(TweenType.SizeDelta);
            step.Axis = TweenAxis.XYZ;

            Assert.AreEqual(TweenAxis.XY,
                TweenChannelAccessor.GetEffectiveAxis(step, TweenChannelKey.SizeDelta));
        }
    }
}
