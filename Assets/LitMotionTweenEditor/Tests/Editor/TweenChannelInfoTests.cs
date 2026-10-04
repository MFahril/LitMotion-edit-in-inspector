using NUnit.Framework;
using UnityEngine;

namespace LitMotion.TweenEditor.Tests
{
    /// <summary>
    /// Covers the public channel description the inspector picks its widgets from.
    /// </summary>
    /// <remarks>
    /// These are the assertions that keep a Move step from being drawn as four numbers. Shape
    /// is deliberately derived from the step's own type and options rather than from whatever
    /// resolved, so every case here holds with and without a target.
    /// </remarks>
    public sealed class TweenChannelInfoTests
    {
        GameObject owner;

        [SetUp]
        public void SetUp()
        {
            owner = GameObject.CreatePrimitive(PrimitiveType.Cube);
            owner.transform.localPosition = new Vector3(1f, 2f, 3f);
        }

        [TearDown]
        public void TearDown()
        {
            if (owner != null) Object.DestroyImmediate(owner);
        }

        static TweenStep Step(TweenType type)
        {
            return new TweenStep { Type = type, Enabled = true, Duration = 1f };
        }

        // --- Shape ---

        [Test]
        public void PositionalChannelsAreThreeComponents()
        {
            // The headline fix: a Move step must not be drawn as a Vector4.
            foreach (var type in new[] { TweenType.Move, TweenType.Rotate, TweenType.Scale, TweenType.Jump })
            {
                var info = TweenChannelInfo.Describe(Step(type), owner);

                Assert.AreEqual(TweenValueShape.Vector3, info.Shape, type.ToString());
                Assert.AreEqual(3, info.ComponentCount, type.ToString());
            }
        }

        [Test]
        public void RectChannelsAreTwoComponents()
        {
            foreach (var type in new[] { TweenType.SizeDelta, TweenType.Pivot, TweenType.Anchors })
            {
                var info = TweenChannelInfo.Describe(Step(type), owner);

                Assert.AreEqual(TweenValueShape.Vector2, info.Shape, type.ToString());
                Assert.AreEqual(2, info.ComponentCount, type.ToString());
            }
        }

        [Test]
        public void ScalarChannelsAreOneComponent()
        {
            foreach (var type in new[]
                     {
                         TweenType.Fade, TweenType.FillAmount, TweenType.VolumeWeight,
                         TweenType.AudioVolume, TweenType.AudioPitch, TweenType.TextReveal,
                         TweenType.TextCounter,
                     })
            {
                var info = TweenChannelInfo.Describe(Step(type), owner);

                Assert.AreEqual(TweenValueShape.Float, info.Shape, type.ToString());
                Assert.AreEqual(1, info.ComponentCount, type.ToString());
            }
        }

        [Test]
        public void ColorChannelsAreColors()
        {
            Assert.AreEqual(TweenValueShape.Color, TweenChannelInfo.Describe(Step(TweenType.Color), owner).Shape);
        }

        [Test]
        public void CameraPropertyShapeFollowsTheChosenProperty()
        {
            var step = Step(TweenType.CameraProperty);

            step.CameraProperty = TweenCameraProperty.FieldOfView;
            Assert.AreEqual(TweenValueShape.Float, TweenChannelInfo.Describe(step, owner).Shape);

            step.CameraProperty = TweenCameraProperty.BackgroundColor;
            Assert.AreEqual(TweenValueShape.Color, TweenChannelInfo.Describe(step, owner).Shape);
        }

        [Test]
        public void MaterialPropertyShapeFollowsTheChosenKind()
        {
            var step = Step(TweenType.MaterialProperty);

            step.MaterialPropertyKind = TweenMaterialPropertyKind.Float;
            Assert.AreEqual(TweenValueShape.Float, TweenChannelInfo.Describe(step, owner).Shape);

            step.MaterialPropertyKind = TweenMaterialPropertyKind.Color;
            Assert.AreEqual(TweenValueShape.Color, TweenChannelInfo.Describe(step, owner).Shape);

            // A shader vector is the one case where all four components are meaningful.
            step.MaterialPropertyKind = TweenMaterialPropertyKind.Vector;
            var info = TweenChannelInfo.Describe(step, owner);
            Assert.AreEqual(TweenValueShape.Vector4, info.Shape);
            Assert.AreEqual(4, info.ComponentCount);
        }

        [Test]
        public void PerCharacterShapeFollowsTheChosenChannel()
        {
            var step = Step(TweenType.TMPCharacter);

            step.TMPCharChannel = TweenTMPCharChannel.Position;
            Assert.AreEqual(TweenValueShape.Vector3, TweenChannelInfo.Describe(step, owner).Shape);

            step.TMPCharChannel = TweenTMPCharChannel.Alpha;
            Assert.AreEqual(TweenValueShape.Float, TweenChannelInfo.Describe(step, owner).Shape);

            step.TMPCharChannel = TweenTMPCharChannel.Color;
            Assert.AreEqual(TweenValueShape.Color, TweenChannelInfo.Describe(step, owner).Shape);
        }

        [Test]
        public void ShapeDoesNotDependOnHavingATarget()
        {
            // The inspector draws the right widget before anything is assigned, and the widget
            // must not change shape the moment a target appears.
            var step = Step(TweenType.Move);

            Assert.AreEqual(TweenValueShape.Vector3, TweenChannelInfo.Describe(step, (GameObject)null).Shape);
            Assert.AreEqual(TweenValueShape.Vector3, TweenChannelInfo.Describe(step, owner).Shape);
        }

        // --- Labels and units ---

        [Test]
        public void VectorComponentsAreLabelledByAxis()
        {
            var info = TweenChannelInfo.Describe(Step(TweenType.Move), owner);

            Assert.AreEqual("X", info.ComponentLabel(0));
            Assert.AreEqual("Y", info.ComponentLabel(1));
            Assert.AreEqual("Z", info.ComponentLabel(2));
        }

        [Test]
        public void ColorComponentsAreLabelledByChannel()
        {
            var info = TweenChannelInfo.Describe(Step(TweenType.Color), owner);

            Assert.AreEqual("R", info.ComponentLabel(0));
            Assert.AreEqual("A", info.ComponentLabel(3));
        }

        [Test]
        public void SizeDeltaIsLabelledAsWidthAndHeight()
        {
            // X and Y would be technically true and practically useless on a rect size.
            var info = TweenChannelInfo.Describe(Step(TweenType.SizeDelta), owner);

            Assert.AreEqual("W", info.ComponentLabel(0));
            Assert.AreEqual("H", info.ComponentLabel(1));
        }

        [Test]
        public void OutOfRangeComponentLabelsAreEmptyRatherThanThrowing()
        {
            var info = TweenChannelInfo.Describe(Step(TweenType.Fade), owner);

            Assert.AreEqual(string.Empty, info.ComponentLabel(3));
            Assert.AreEqual(string.Empty, info.ComponentLabel(-1));
        }

        [Test]
        public void NormalizedChannelsDeclareTheirRange()
        {
            // This is what turns the Fade field into a 0-1 slider.
            Assert.AreEqual("0-1", TweenChannelInfo.Describe(Step(TweenType.Fade), owner).Unit);
            Assert.AreEqual("deg", TweenChannelInfo.Describe(Step(TweenType.Rotate), owner).Unit);
            Assert.AreEqual("x", TweenChannelInfo.Describe(Step(TweenType.Scale), owner).Unit);
        }

        [Test]
        public void TheChannelNameSaysWhatWasActuallyResolved()
        {
            // Fade finding a CanvasGroup rather than the Image the author was looking at is the
            // kind of thing that has to be visible.
            var panel = new GameObject("panel", typeof(CanvasGroup));

            try
            {
                var info = TweenChannelInfo.Describe(Step(TweenType.Fade), panel);

                Assert.AreEqual("Canvas Group Alpha", info.ChannelName);
                Assert.IsTrue(info.IsResolved);
            }
            finally
            {
                Object.DestroyImmediate(panel);
            }
        }

        [Test]
        public void AnUnresolvedChannelSaysSo()
        {
            // A RectTransform-only type on a plain cube cannot bind.
            var info = TweenChannelInfo.Describe(Step(TweenType.SizeDelta), owner);

            Assert.IsFalse(info.IsResolved);
        }

        // --- Read and write ---

        [Test]
        public void ReadingReturnsTheLiveValue()
        {
            Assert.IsTrue(TweenChannelInfo.TryRead(Step(TweenType.Move), owner, out var value));
            Assert.AreEqual(new Vector3(1f, 2f, 3f), (Vector3)value);
        }

        [Test]
        public void WritingMovesTheObject()
        {
            Assert.IsTrue(TweenChannelInfo.TryWrite(Step(TweenType.Move), owner, new Vector4(4f, 5f, 6f, 0f)));
            Assert.AreEqual(new Vector3(4f, 5f, 6f), owner.transform.localPosition);
        }

        [Test]
        public void WritingIgnoresTheAxisMask()
        {
            // Grab and apply mean "put the object here", not "animate these axes", so a masked
            // step still applies its whole value.
            var step = Step(TweenType.Move);
            step.Axis = TweenAxis.X;

            TweenChannelInfo.TryWrite(step, owner, new Vector4(7f, 8f, 9f, 0f));

            Assert.AreEqual(new Vector3(7f, 8f, 9f), owner.transform.localPosition);
        }

        [Test]
        public void WriteOnlyChannelsReportThatTheyCannotBeRead()
        {
            Assert.IsFalse(TweenChannelInfo.Describe(Step(TweenType.TextCounter), owner).SupportsRead);
            Assert.IsFalse(TweenChannelInfo.Describe(Step(TweenType.TMPCharacter), owner).SupportsRead);
            Assert.IsTrue(TweenChannelInfo.Describe(Step(TweenType.Move), owner).SupportsRead);
        }

        [Test]
        public void TimelinePrimitivesHaveNothingToReadOrWrite()
        {
            foreach (var type in new[] { TweenType.Interval, TweenType.Callback, TweenType.Custom })
            {
                Assert.IsFalse(TweenChannelInfo.TryRead(Step(type), owner, out _), type.ToString());
                Assert.IsFalse(TweenChannelInfo.TryWrite(Step(type), owner, Vector4.one), type.ToString());
            }
        }

        [Test]
        public void ANullStepIsDescribedWithoutThrowing()
        {
            var info = TweenChannelInfo.Describe(null, owner);

            Assert.AreEqual(string.Empty, info.ChannelName);
            Assert.IsFalse(info.IsResolved);
            Assert.IsFalse(TweenChannelInfo.TryRead(null, owner, out _));
        }

        [Test]
        public void MaterialResolutionOnlyAppliesToMaterialSteps()
        {
            Assert.IsNull(TweenChannelInfo.ResolveMaterial(Step(TweenType.Move), owner));
            Assert.IsNotNull(TweenChannelInfo.ResolveMaterial(Step(TweenType.MaterialProperty), owner));
        }
    }
}
