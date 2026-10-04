using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using LitMotion.TweenEditor.Editor;

namespace LitMotion.TweenEditor.Tests
{
    /// <summary>
    /// Covers the timeline's editing arithmetic: split, nudge, align, distribute, trim and zoom.
    /// </summary>
    /// <remarks>
    /// Each command plans before it writes, which is what makes it testable here without a
    /// window, a pointer or a SerializedObject.
    /// </remarks>
    public sealed class TweenTimelineCommandTests
    {
        const float Tolerance = 1e-4f;

        static TweenStep Step(float startTime, float duration, TweenType type = TweenType.Move)
        {
            return new TweenStep
            {
                Type = type,
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

        static List<int> Selection(params int[] indices) => new(indices);

        // --- Split ---

        [Test]
        public void SplitCutsAClipIntoTwoHalvesThatMeet()
        {
            var step = Step(1f, 2f);

            Assert.IsTrue(TweenTimelineCommands.TrySplit(step, 1.5f,
                out var firstDuration, out var secondStart, out var secondDuration));

            Assert.AreEqual(0.5f, firstDuration, Tolerance);
            Assert.AreEqual(1.5f, secondStart, Tolerance);
            Assert.AreEqual(1.5f, secondDuration, Tolerance);

            // No gap and no overlap: the two halves must still cover exactly the original span.
            Assert.AreEqual(step.StartTime + step.Duration, secondStart + secondDuration, Tolerance);
        }

        [Test]
        public void SplittingOutsideTheClipIsRefused()
        {
            var step = Step(1f, 1f);

            Assert.IsFalse(TweenTimelineCommands.CanSplit(step, 0.5f), "before the clip");
            Assert.IsFalse(TweenTimelineCommands.CanSplit(step, 2.5f), "after the clip");
        }

        [Test]
        public void SplittingTooCloseToAnEdgeIsRefused()
        {
            // A sliver is unclickable, and a zero-length half would silently stop animating.
            var step = Step(0f, 1f);

            Assert.IsFalse(TweenTimelineCommands.CanSplit(step, 0.001f));
            Assert.IsFalse(TweenTimelineCommands.CanSplit(step, 0.999f));
            Assert.IsTrue(TweenTimelineCommands.CanSplit(step, 0.5f));
        }

        [Test]
        public void AClipTooShortToHalveCannotBeSplit()
        {
            var step = Step(0f, TweenTimelineCommands.MinDuration);

            Assert.IsFalse(TweenTimelineCommands.CanSplit(step, step.Duration * 0.5f));
        }

        [Test]
        public void AZeroLengthMarkerCannotBeSplit()
        {
            Assert.IsFalse(TweenTimelineCommands.CanSplit(Step(1f, 0f, TweenType.Callback), 1f));
        }

        // --- Nudge ---

        [Test]
        public void NudgeMovesEverySelectedClipByTheSameAmount()
        {
            var animation = Animation(Step(1f, 1f), Step(3f, 1f));

            var plan = TweenTimelineCommands.Nudge(animation, Selection(0, 1), 0.25f);

            Assert.AreEqual(2, plan.Count);
            Assert.AreEqual(1.25f, plan[0].StartTime, Tolerance);
            Assert.AreEqual(3.25f, plan[1].StartTime, Tolerance);
        }

        [Test]
        public void NudgeClampsTheGroupTogetherAtZero()
        {
            // The clip nearest zero decides the limit. Clamping each clip independently would
            // flatten the relative timing the author arranged.
            var animation = Animation(Step(0.1f, 1f), Step(2f, 1f));

            var plan = TweenTimelineCommands.Nudge(animation, Selection(0, 1), -0.5f);

            Assert.AreEqual(0f, plan[0].StartTime, Tolerance);
            Assert.AreEqual(1.9f, plan[1].StartTime, Tolerance, "the gap must be preserved");
        }

        [Test]
        public void NudgeLeavesDurationsAlone()
        {
            var animation = Animation(Step(1f, 0.4f));

            var plan = TweenTimelineCommands.Nudge(animation, Selection(0), 1f);

            Assert.AreEqual(0.4f, plan[0].Duration, Tolerance);
        }

        [Test]
        public void NudgingNothingPlansNothing()
        {
            var animation = Animation(Step(1f, 1f));

            Assert.IsEmpty(TweenTimelineCommands.Nudge(animation, Selection(), 1f));
            Assert.IsEmpty(TweenTimelineCommands.Nudge(animation, null, 1f));
        }

        [Test]
        public void NudgeIgnoresIndicesThatNoLongerExist()
        {
            var animation = Animation(Step(1f, 1f));

            var plan = TweenTimelineCommands.Nudge(animation, Selection(0, 7), 0.5f);

            Assert.AreEqual(1, plan.Count);
            Assert.AreEqual(0, plan[0].Index);
        }

        // --- Align ---

        [Test]
        public void AligningStartsUsesTheEarliestByDefault()
        {
            var animation = Animation(Step(2f, 1f), Step(0.5f, 1f), Step(3f, 1f));

            var plan = TweenTimelineCommands.AlignStarts(animation, Selection(0, 1, 2));

            for (var i = 0; i < plan.Count; i++)
            {
                Assert.AreEqual(0.5f, plan[i].StartTime, Tolerance);
            }
        }

        [Test]
        public void AligningEndsMovesClipsRatherThanResizingThem()
        {
            var animation = Animation(Step(0f, 1f), Step(0f, 0.25f));

            var plan = TweenTimelineCommands.AlignEnds(animation, Selection(0, 1));

            // Both end at 1, and both keep the length they had.
            Assert.AreEqual(0f, plan[0].StartTime, Tolerance);
            Assert.AreEqual(1f, plan[0].Duration, Tolerance);
            Assert.AreEqual(0.75f, plan[1].StartTime, Tolerance);
            Assert.AreEqual(0.25f, plan[1].Duration, Tolerance);
        }

        [Test]
        public void AligningEndsNeverPushesAClipBeforeZero()
        {
            var animation = Animation(Step(0f, 0.2f), Step(0f, 5f));

            var plan = TweenTimelineCommands.AlignEnds(animation, Selection(0, 1));

            for (var i = 0; i < plan.Count; i++)
            {
                Assert.GreaterOrEqual(plan[i].StartTime, 0f);
            }
        }

        [Test]
        public void AligningToAnExplicitTimeOverridesTheDefault()
        {
            var animation = Animation(Step(2f, 1f), Step(5f, 1f));

            var plan = TweenTimelineCommands.AlignStarts(animation, Selection(0, 1), 1.25f);

            Assert.AreEqual(1.25f, plan[0].StartTime, Tolerance);
            Assert.AreEqual(1.25f, plan[1].StartTime, Tolerance);
        }

        // --- Distribute ---

        [Test]
        public void DistributeSpacesTheMiddleClipsEvenly()
        {
            var animation = Animation(Step(0f, 0.2f), Step(0.1f, 0.2f), Step(0.15f, 0.2f), Step(3f, 0.2f));

            var plan = TweenTimelineCommands.Distribute(animation, Selection(0, 1, 2, 3));

            Assert.AreEqual(4, plan.Count);
            Assert.AreEqual(0f, plan[0].StartTime, Tolerance, "the first stays put");
            Assert.AreEqual(1f, plan[1].StartTime, Tolerance);
            Assert.AreEqual(2f, plan[2].StartTime, Tolerance);
            Assert.AreEqual(3f, plan[3].StartTime, Tolerance, "the last stays put");
        }

        [Test]
        public void DistributeWorksFromTimelineOrderNotSelectionOrder()
        {
            // The selection list is in click order, which says nothing about position.
            var animation = Animation(Step(0f, 0.1f), Step(4f, 0.1f), Step(1f, 0.1f));

            var plan = TweenTimelineCommands.Distribute(animation, Selection(1, 2, 0));

            Assert.AreEqual(0, plan[0].Index);
            Assert.AreEqual(2, plan[1].Index);
            Assert.AreEqual(1, plan[2].Index);
            Assert.AreEqual(2f, plan[1].StartTime, Tolerance);
        }

        [Test]
        public void DistributeNeedsThreeClipsToMeanAnything()
        {
            var animation = Animation(Step(0f, 1f), Step(2f, 1f));

            Assert.IsEmpty(TweenTimelineCommands.Distribute(animation, Selection(0, 1)));
        }

        // --- Trim ---

        [Test]
        public void TrimmingTheStartHoldsTheTailStill()
        {
            var animation = Animation(Step(1f, 2f));

            var plan = TweenTimelineCommands.TrimStartTo(animation, 0, 1.5f);

            Assert.AreEqual(1.5f, plan[0].StartTime, Tolerance);
            Assert.AreEqual(1.5f, plan[0].Duration, Tolerance, "the end should still be at 3");
        }

        [Test]
        public void TrimmingTheStartPastTheEndCollapsesRatherThanInverts()
        {
            var animation = Animation(Step(1f, 1f));

            var plan = TweenTimelineCommands.TrimStartTo(animation, 0, 5f);

            Assert.AreEqual(2f, plan[0].StartTime, Tolerance);
            Assert.AreEqual(0f, plan[0].Duration, Tolerance);
        }

        [Test]
        public void TrimmingTheEndHoldsTheHeadStill()
        {
            var animation = Animation(Step(1f, 2f));

            var plan = TweenTimelineCommands.TrimEndTo(animation, 0, 2f);

            Assert.AreEqual(1f, plan[0].StartTime, Tolerance);
            Assert.AreEqual(1f, plan[0].Duration, Tolerance);
        }

        [Test]
        public void TrimmingTheEndBeforeTheStartGivesZeroNotNegative()
        {
            var animation = Animation(Step(2f, 1f));

            var plan = TweenTimelineCommands.TrimEndTo(animation, 0, 0.5f);

            Assert.AreEqual(0f, plan[0].Duration, Tolerance);
        }

        [Test]
        public void MarkersAreNotTrimmable()
        {
            var animation = Animation(Step(1f, 0f, TweenType.Callback));

            Assert.IsEmpty(TweenTimelineCommands.TrimStartTo(animation, 0, 0.5f));
            Assert.IsEmpty(TweenTimelineCommands.TrimEndTo(animation, 0, 2f));
        }

        [Test]
        public void SetStartKeepsTheLengthAndClampsAtZero()
        {
            var animation = Animation(Step(1f, 0.75f));

            var plan = TweenTimelineCommands.SetStart(animation, 0, -3f);

            Assert.AreEqual(0f, plan[0].StartTime, Tolerance);
            Assert.AreEqual(0.75f, plan[0].Duration, Tolerance);
        }

        // --- Zoom ---

        [Test]
        public void FitChoosesAScaleThatShowsTheWholeAnimation()
        {
            var scale = TweenTimelineCommands.FitPixelsPerSecond(2f, 424f);

            // 2 seconds into 424px, less the trailing air, is 200 px/s.
            Assert.AreEqual(200f, scale, 0.5f);
        }

        [Test]
        public void FitStaysWithinTheZoomRange()
        {
            var tiny = TweenTimelineCommands.FitPixelsPerSecond(0.01f, 2000f);
            var huge = TweenTimelineCommands.FitPixelsPerSecond(600f, 300f);

            Assert.LessOrEqual(tiny, TweenTimelineStyles.MaxPixelsPerSecond);
            Assert.GreaterOrEqual(huge, TweenTimelineStyles.MinPixelsPerSecond);
        }

        [Test]
        public void FitOnAnEmptyOrEndlessAnimationFallsBackToTheDefault()
        {
            Assert.AreEqual(TweenTimelineStyles.DefaultPixelsPerSecond,
                TweenTimelineCommands.FitPixelsPerSecond(0f, 500f), Tolerance);

            Assert.AreEqual(TweenTimelineStyles.DefaultPixelsPerSecond,
                TweenTimelineCommands.FitPixelsPerSecond(float.PositiveInfinity, 500f), Tolerance);
        }

        [Test]
        public void ZoomStepsInBothDirectionsAndStaysInRange()
        {
            var inwards = TweenTimelineCommands.ZoomStep(200f, -1f);
            var outwards = TweenTimelineCommands.ZoomStep(200f, 1f);

            Assert.Greater(inwards, 200f);
            Assert.Less(outwards, 200f);

            Assert.AreEqual(TweenTimelineStyles.MaxPixelsPerSecond,
                TweenTimelineCommands.ZoomStep(TweenTimelineStyles.MaxPixelsPerSecond, -1f), Tolerance);
            Assert.AreEqual(TweenTimelineStyles.MinPixelsPerSecond,
                TweenTimelineCommands.ZoomStep(TweenTimelineStyles.MinPixelsPerSecond, 1f), Tolerance);
        }

        [Test]
        public void AnchoredZoomKeepsTheTimeUnderTheCursorPutsItThere()
        {
            // At 100 px/s the cursor sits on t=2 at x=200, 50px into the viewport. After zooming
            // to 200 px/s, t=2 is at x=400, so the scroll must be 350 to leave it 50px in.
            var scroll = TweenTimelineCommands.AnchoredScroll(2f, 50f, 200f);

            Assert.AreEqual(350f, scroll, Tolerance);
        }

        [Test]
        public void AnchoredZoomNeverScrollsBeforeTheStart()
        {
            Assert.AreEqual(0f, TweenTimelineCommands.AnchoredScroll(0.1f, 500f, 100f), Tolerance);
        }

        // --- Selection state ---

        [Test]
        public void SelectingOneReplacesTheSelection()
        {
            var context = new TweenTimelineContext();
            context.SelectOnly(2);
            context.SelectOnly(3);

            Assert.AreEqual(1, context.Selection.Count);
            Assert.AreEqual(3, context.PrimaryIndex);
        }

        [Test]
        public void TogglingAddsThenRemoves()
        {
            var context = new TweenTimelineContext();
            context.ToggleSelection(1);
            context.ToggleSelection(2);

            Assert.AreEqual(2, context.Selection.Count);
            Assert.AreEqual(2, context.PrimaryIndex, "the last click is primary");

            context.ToggleSelection(1);
            Assert.AreEqual(1, context.Selection.Count);
            Assert.IsFalse(context.IsSelected(1));
        }

        [Test]
        public void PruningDropsIndicesPastTheEnd()
        {
            // Deleting steps elsewhere must not leave the selection pointing into thin air.
            var context = new TweenTimelineContext();
            context.ToggleSelection(0);
            context.ToggleSelection(5);

            context.PruneSelection(2);

            Assert.AreEqual(1, context.Selection.Count);
            Assert.AreEqual(0, context.PrimaryIndex);
        }

        [Test]
        public void AnEmptySelectionHasNoPrimary()
        {
            Assert.AreEqual(-1, new TweenTimelineContext().PrimaryIndex);
        }

        // --- Snap feedback ---

        [Test]
        public void SnappingToANeighbourReportsWhereItLatched()
        {
            // This is what positions the guide line.
            var context = new TweenTimelineContext { SnapEnabled = true, PixelsPerSecond = 200f };
            context.RebuildSnapTargets(Animation(Step(0f, 0.37f)), -1);

            context.Snap(0.368f);

            Assert.IsTrue(context.LastSnappedTime.HasValue);
            Assert.AreEqual(0.37f, context.LastSnappedTime.Value, Tolerance);
        }

        [Test]
        public void FallingBackToTheGridReportsNoLatch()
        {
            // The grid catches almost every drag, so a guide for it would be a line permanently
            // on screen saying nothing.
            var context = new TweenTimelineContext { SnapEnabled = true, PixelsPerSecond = 200f };
            context.SnapTargets.Clear();

            context.Snap(0.063f);

            Assert.IsFalse(context.LastSnappedTime.HasValue);
        }

        [Test]
        public void SnapDisabledReportsNoLatch()
        {
            var context = new TweenTimelineContext { SnapEnabled = false };
            context.Snap(0.5f);

            Assert.IsFalse(context.LastSnappedTime.HasValue);
        }
    }
}
