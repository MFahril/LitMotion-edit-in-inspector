using NUnit.Framework;
using UnityEngine;
using LitMotion.TweenEditor.Editor;

namespace LitMotion.TweenEditor.Tests
{
    /// <summary>
    /// Covers the timeline's snapping maths and ruler tick selection.
    /// </summary>
    public sealed class TweenTimelineTests
    {
        const float Tolerance = 1e-4f;

        static TweenStep Step(float startTime, float duration)
        {
            return new TweenStep
            {
                Type = TweenType.Move,
                Enabled = true,
                StartTime = startTime,
                Duration = duration,
                Loops = 1,
            };
        }

        static TweenAnimation Animation(params TweenStep[] steps)
        {
            var animation = new TweenAnimation();
            animation.Steps.Clear();
            animation.Steps.AddRange(steps);
            return animation;
        }

        // --- Snapping ---

        [Test]
        public void SnapDisabledReturnsTheInputUnchanged()
        {
            var context = new TweenTimelineContext { SnapEnabled = false };

            Assert.AreEqual(0.3337f, context.Snap(0.3337f), Tolerance);
        }

        [Test]
        public void SnapClampsNegativeTimesToZero()
        {
            var context = new TweenTimelineContext { SnapEnabled = false };

            Assert.AreEqual(0f, context.Snap(-5f), Tolerance);
        }

        [Test]
        public void SnapFallsBackToTheFixedGrid()
        {
            var context = new TweenTimelineContext { SnapEnabled = true, PixelsPerSecond = 200f };
            context.SnapTargets.Clear();

            // 0.07 is nearest the 0.05 grid multiple at 0.05.
            Assert.AreEqual(0.05f, context.Snap(0.06f), Tolerance);
            Assert.AreEqual(0.10f, context.Snap(0.09f), Tolerance);
        }

        [Test]
        public void SnapPrefersANeighbouringClipEdgeOverTheGrid()
        {
            var context = new TweenTimelineContext { SnapEnabled = true, PixelsPerSecond = 200f };
            context.RebuildSnapTargets(Animation(Step(0f, 0.37f)), -1);

            // 0.368 is within the pixel threshold of the neighbour's end at 0.37, which must win
            // over the 0.35 grid multiple.
            Assert.AreEqual(0.37f, context.Snap(0.368f), Tolerance);
        }

        [Test]
        public void SnapPrefersThePlayheadWhenItIsClosest()
        {
            var context = new TweenTimelineContext
            {
                SnapEnabled = true,
                PixelsPerSecond = 200f,
                PlayheadTime = 0.413f,
            };
            context.SnapTargets.Clear();

            Assert.AreEqual(0.413f, context.Snap(0.415f), Tolerance);
        }

        [Test]
        public void SnapThresholdScalesWithZoomSoTheMagnetFeelsConstant()
        {
            var animation = Animation(Step(0f, 1f));

            // Zoomed out, 1.1 is only ~4px from the clip end at 1.0, so it snaps.
            var zoomedOut = new TweenTimelineContext { SnapEnabled = true, PixelsPerSecond = 40f };
            zoomedOut.RebuildSnapTargets(animation, -1);
            Assert.AreEqual(1f, zoomedOut.Snap(1.1f), Tolerance, "should snap when zoomed out");

            // Zoomed in, the same 0.1s gap is 160px, far outside the threshold, so it does not.
            var zoomedIn = new TweenTimelineContext { SnapEnabled = true, PixelsPerSecond = 1600f };
            zoomedIn.RebuildSnapTargets(animation, -1);
            Assert.AreNotEqual(1f, zoomedIn.Snap(1.1f), "should not snap when zoomed in");
        }

        [Test]
        public void RebuildSnapTargetsExcludesTheDraggedStep()
        {
            var context = new TweenTimelineContext { SnapEnabled = true, PixelsPerSecond = 200f };
            var animation = Animation(Step(0f, 0.5f), Step(2f, 0.5f));

            context.RebuildSnapTargets(animation, 1);

            // A clip must not snap to its own edges, or dragging it would stick in place.
            Assert.IsFalse(context.SnapTargets.Contains(2f));
            Assert.IsFalse(context.SnapTargets.Contains(2.5f));
            Assert.IsTrue(context.SnapTargets.Contains(0.5f));
        }

        [Test]
        public void RebuildSnapTargetsSkipsDisabledSteps()
        {
            var context = new TweenTimelineContext();
            var disabled = Step(3f, 1f);
            disabled.Enabled = false;

            context.RebuildSnapTargets(Animation(disabled), -1);

            Assert.IsFalse(context.SnapTargets.Contains(3f));
        }

        [Test]
        public void RebuildSnapTargetsAlwaysIncludesZero()
        {
            var context = new TweenTimelineContext();
            context.RebuildSnapTargets(Animation(), -1);

            Assert.IsTrue(context.SnapTargets.Contains(0f));
        }

        // --- Ruler ---

        [Test]
        public void TickIntervalGrowsAsTheTimelineZoomsOut()
        {
            var zoomedIn = TweenTimelineRuler.ChooseTickInterval(1600f);
            var mid = TweenTimelineRuler.ChooseTickInterval(220f);
            var zoomedOut = TweenTimelineRuler.ChooseTickInterval(40f);

            Assert.Less(zoomedIn, mid, "a zoomed-in ruler can afford finer ticks");
            Assert.Less(mid, zoomedOut, "a zoomed-out ruler needs coarser ticks");
        }

        [Test]
        public void TickIntervalAlwaysLeavesRoomForALabel()
        {
            // Whatever the zoom, consecutive ticks must be at least ~56px apart or the labels
            // would overlap and the ruler becomes unreadable.
            var zooms = new[] { 40f, 80f, 150f, 220f, 500f, 900f, 1600f };

            for (var i = 0; i < zooms.Length; i++)
            {
                var interval = TweenTimelineRuler.ChooseTickInterval(zooms[i]);
                Assert.GreaterOrEqual(interval * zooms[i], 56f, "at " + zooms[i] + " px/s");
            }
        }
    }
}
