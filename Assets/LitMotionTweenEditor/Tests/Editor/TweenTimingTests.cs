using NUnit.Framework;

namespace LitMotion.TweenEditor.Tests
{
    /// <summary>
    /// Covers the timeline arithmetic the inspector and the sequence builder both rely on.
    /// </summary>
    public sealed class TweenTimingTests
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
                Delay = 0f,
            };
        }

        [Test]
        public void TotalDurationIsJustTheDurationForASingleLoop()
        {
            Assert.AreEqual(1f, Step(0f, 1f).TotalDuration, Tolerance);
        }

        [Test]
        public void EndTimeIsStartPlusTotalDuration()
        {
            // The case called out in the plan: a step at 0.5s lasting 1.0s ends at 1.5s.
            Assert.AreEqual(1.5f, Step(0.5f, 1f).EndTime, Tolerance);
        }

        [Test]
        public void LoopsMultiplyTheDuration()
        {
            var step = Step(0f, 0.5f);
            step.Loops = 4;

            Assert.AreEqual(2f, step.TotalDuration, Tolerance);
        }

        [Test]
        public void AFirstLoopDelayIsCountedOnce()
        {
            var step = Step(0f, 1f);
            step.Loops = 3;
            step.Delay = 0.5f;
            step.DelayType = DelayType.FirstLoop;

            Assert.AreEqual(3.5f, step.TotalDuration, Tolerance);
        }

        [Test]
        public void AnEveryLoopDelayIsCountedPerLoop()
        {
            var step = Step(0f, 1f);
            step.Loops = 3;
            step.Delay = 0.5f;
            step.DelayType = DelayType.EveryLoop;

            Assert.AreEqual(4.5f, step.TotalDuration, Tolerance);
        }

        [Test]
        public void InfiniteLoopsReportAnInfiniteDuration()
        {
            var step = Step(0f, 1f);
            step.Loops = -1;

            Assert.IsTrue(float.IsPositiveInfinity(step.TotalDuration));
        }

        [Test]
        public void DisabledStepsContributeNoDuration()
        {
            var step = Step(2f, 5f);
            step.Enabled = false;

            Assert.AreEqual(0f, step.TotalDuration, Tolerance);
        }

        [Test]
        public void AnimationDurationIsTheLatestStepEnd()
        {
            var animation = new TweenAnimation();
            animation.Steps.Clear();
            animation.Steps.Add(Step(0f, 1f));
            animation.Steps.Add(Step(0.5f, 2f));   // ends at 2.5
            animation.Steps.Add(Step(0.25f, 0.5f));

            Assert.AreEqual(2.5f, animation.Duration, Tolerance);
        }

        [Test]
        public void AnimationDurationIgnoresDisabledSteps()
        {
            var animation = new TweenAnimation();
            animation.Steps.Clear();
            animation.Steps.Add(Step(0f, 1f));

            var disabled = Step(10f, 10f);
            disabled.Enabled = false;
            animation.Steps.Add(disabled);

            Assert.AreEqual(1f, animation.Duration, Tolerance);
        }

        [Test]
        public void AnimationDurationIsInfiniteIfAnyStepLoopsForever()
        {
            var animation = new TweenAnimation();
            animation.Steps.Clear();

            var infinite = Step(0f, 1f);
            infinite.Loops = -1;
            animation.Steps.Add(infinite);

            Assert.IsTrue(float.IsPositiveInfinity(animation.Duration));
        }

        [Test]
        public void EnabledStepCountSkipsDisabledAndNullSteps()
        {
            var animation = new TweenAnimation();
            animation.Steps.Clear();
            animation.Steps.Add(Step(0f, 1f));
            animation.Steps.Add(null);

            var disabled = Step(0f, 1f);
            disabled.Enabled = false;
            animation.Steps.Add(disabled);

            Assert.AreEqual(1, animation.EnabledStepCount);
        }

        [Test]
        public void AnEmptyAnimationHasZeroDuration()
        {
            var animation = new TweenAnimation();
            animation.Steps.Clear();

            Assert.AreEqual(0f, animation.Duration, Tolerance);
            Assert.AreEqual(0, animation.EnabledStepCount);
        }

        [Test]
        public void DisplayNameFallsBackToTheTypeName()
        {
            var step = Step(0f, 1f);
            Assert.AreEqual("Move", step.DisplayName);

            step.Label = "Slide in";
            Assert.AreEqual("Slide in", step.DisplayName);

            step.Label = "   ";
            Assert.AreEqual("Move", step.DisplayName, "whitespace is not a label");
        }

        [Test]
        public void CloneIsIndependentOfItsSource()
        {
            var step = Step(1f, 2f);
            step.Label = "original";

            var clone = step.Clone();
            clone.Label = "copy";
            clone.StartTime = 9f;

            Assert.AreEqual("original", step.Label);
            Assert.AreEqual(1f, step.StartTime, Tolerance);
            Assert.AreNotSame(step.CustomCurve, clone.CustomCurve, "the curve must be deep-copied");
        }

        [Test]
        public void CloningAnAnimationDeepCopiesItsSteps()
        {
            var animation = new TweenAnimation { Id = "Show" };
            animation.Steps.Clear();
            animation.Steps.Add(Step(0f, 1f));

            var clone = animation.Clone();
            clone.Steps[0].Duration = 99f;

            Assert.AreEqual(1f, animation.Steps[0].Duration, Tolerance);
            Assert.AreNotSame(animation.Steps, clone.Steps);
        }

        [Test]
        public void InfiniteStepLoopsAreFlaggedAsUnsupported()
        {
            var step = Step(0f, 1f);
            Assert.IsFalse(TweenStepBuilder.HasUnsupportedInfiniteLoop(step));

            step.Loops = -1;
            Assert.IsTrue(TweenStepBuilder.HasUnsupportedInfiniteLoop(step));
        }
    }
}
