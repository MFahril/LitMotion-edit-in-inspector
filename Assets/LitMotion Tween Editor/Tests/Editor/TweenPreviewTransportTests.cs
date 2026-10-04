using NUnit.Framework;
using UnityEngine;
using LitMotion.TweenEditor.Editor;

namespace LitMotion.TweenEditor.Tests
{
    /// <summary>
    /// Covers the preview bar's transport: how playback wraps or stops, frame-stepping, and the
    /// preview-only speed and loop settings.
    /// </summary>
    public sealed class TweenPreviewTransportTests
    {
        const float Tolerance = 1e-5f;

        // --- Advance: the wrapping rules ---

        [Test]
        public void PlaybackAdvancesByTheStep()
        {
            var next = TweenPreviewController.Advance(0.2f, 0.1f, 1f, 1f, false, false, out var playing);

            Assert.AreEqual(0.3f, next, Tolerance);
            Assert.IsTrue(playing);
        }

        [Test]
        public void AFiniteAnimationParksOnTheLastFrameAndStops()
        {
            var next = TweenPreviewController.Advance(0.95f, 0.1f, 1f, 1f, false, false, out var playing);

            Assert.AreEqual(1f, next, Tolerance);
            Assert.IsFalse(playing);
        }

        [Test]
        public void TheLoopToggleWrapsInsteadOfStopping()
        {
            var next = TweenPreviewController.Advance(0.95f, 0.1f, 1f, 1f, false, true, out var playing);

            Assert.AreEqual(0.05f, next, Tolerance);
            Assert.IsTrue(playing);
        }

        [Test]
        public void AnInfiniteAnimationWrapsBySingleLoop()
        {
            // Duration is one loop for an infinite animation; the wrap uses the loop length.
            var next = TweenPreviewController.Advance(0.45f, 0.1f, 0.5f, 0.5f, true, false, out var playing);

            Assert.AreEqual(0.05f, next, Tolerance);
            Assert.IsTrue(playing);
        }

        [Test]
        public void NegativeStepsDoNotRunBackwards()
        {
            var next = TweenPreviewController.Advance(0.5f, -1f, 1f, 1f, false, false, out _);

            Assert.AreEqual(0.5f, next, Tolerance);
        }

        // --- The controller ---

        [Test]
        public void SpeedMultiplierIsClampedToAUsableRange()
        {
            var preview = new TweenPreviewController();

            preview.SpeedMultiplier = 0f;
            Assert.Greater(preview.SpeedMultiplier, 0f);

            preview.SpeedMultiplier = 1000f;
            Assert.LessOrEqual(preview.SpeedMultiplier, 8f);
        }

        [Test]
        public void FrameSteppingMovesOneSixtiethAndPauses()
        {
            var owner = new GameObject("transport");
            var preview = new TweenPreviewController();

            try
            {
                var animation = new TweenAnimation { Id = "Test" };
                animation.Steps.Clear();
                animation.Steps.Add(new TweenStep
                {
                    Type = TweenType.Move,
                    Duration = 1f,
                    FromCurrent = false,
                    To = new Vector4(1f, 0f, 0f, 0f),
                    Ease = Ease.Linear,
                });

                Assert.IsTrue(preview.Begin(animation, owner));
                preview.Play();
                preview.Scrub(0.5f);

                preview.StepFrames(6);
                Assert.AreEqual(0.6f, preview.Time, 1e-4f);
                Assert.IsFalse(preview.IsPlaying, "stepping pauses");

                preview.StepFrames(-12);
                Assert.AreEqual(0.4f, preview.Time, 1e-4f);
                Assert.AreEqual(0.4f, owner.transform.localPosition.x, 1e-4f, "the pose follows the step");
            }
            finally
            {
                preview.Stop();
                Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void LoopAndSpeedSurviveARebuild()
        {
            // They are the author's viewing choices, not part of the animation, so editing a
            // step while previewing must not reset them.
            var owner = new GameObject("transport");
            var preview = new TweenPreviewController { Loop = true, SpeedMultiplier = 0.25f };

            try
            {
                var animation = new TweenAnimation { Id = "Test" };
                animation.Steps.Clear();
                animation.Steps.Add(new TweenStep { Type = TweenType.Move, Duration = 1f });

                Assert.IsTrue(preview.Begin(animation, owner));
                preview.Rebuild();

                Assert.IsTrue(preview.Loop);
                Assert.AreEqual(0.25f, preview.SpeedMultiplier, Tolerance);
            }
            finally
            {
                preview.Stop();
                Object.DestroyImmediate(owner);
            }
        }
    }
}
