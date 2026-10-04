using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace LitMotion.TweenEditor.Tests
{
    /// <summary>
    /// Covers the one-step fast path, which returns a step's own motion as the driver instead
    /// of wrapping it in a sequence.
    /// </summary>
    /// <remarks>
    /// The preview runs whatever the runner returns, so the fast path is only acceptable if it
    /// is indistinguishable from the sequence it replaces. Each scenario builds the same
    /// animation twice, once each way, on two identical objects with their own clocks, and
    /// compares them by scrubbing and by playing in real time.
    /// </remarks>
    public sealed class TweenFastPathTests
    {
        const float Tolerance = 1e-5f;
        const double Frame = 1.0 / 60.0;

        GameObject fast;
        GameObject slow;
        ManualMotionDispatcher fastClock;
        ManualMotionDispatcher slowClock;
        MotionHandle fastDriver;
        MotionHandle slowDriver;

        public enum Scenario
        {
            LinearMove,
            OvershootWithDelayAndYoyoStepLoops,
            DelayOnEveryStepLoop,
            AnimationYoyoLoops,
            AnimationRestartLoopsOnAFade,
            Punch,
            SeededShake,
            Jump,
            CustomProgress,
        }

        [SetUp]
        public void SetUp()
        {
            fast = new GameObject("fast", typeof(CanvasGroup));
            slow = new GameObject("slow", typeof(CanvasGroup));
            fastClock = new ManualMotionDispatcher();
            slowClock = new ManualMotionDispatcher();
        }

        [TearDown]
        public void TearDown()
        {
            if (fastDriver.IsActive()) fastDriver.Cancel();
            if (slowDriver.IsActive()) slowDriver.Cancel();
            fastDriver = slowDriver = MotionHandle.None;
            TweenAnimationRunner.FastPathEnabled = true;

            Object.DestroyImmediate(fast);
            Object.DestroyImmediate(slow);
        }

        static TweenStep Step(TweenType type)
        {
            return new TweenStep
            {
                Type = type,
                Enabled = true,
                Duration = 1f,
                Ease = Ease.Linear,
                FromCurrent = false,
                From = Vector4.zero,
                To = new Vector4(4f, 2f, 1f, 0f),
            };
        }

        static TweenAnimation Animation(params TweenStep[] steps)
        {
            var animation = new TweenAnimation { Id = "Test" };
            animation.Steps.Clear();
            animation.Steps.AddRange(steps);
            return animation;
        }

        static TweenAnimation Make(Scenario scenario, List<float> progress = null)
        {
            switch (scenario)
            {
                case Scenario.OvershootWithDelayAndYoyoStepLoops:
                {
                    var step = Step(TweenType.Move);
                    step.Ease = Ease.OutBack;
                    step.Delay = 0.3f;
                    step.Loops = 3;
                    step.LoopType = LoopType.Yoyo;
                    return Animation(step);
                }

                case Scenario.DelayOnEveryStepLoop:
                {
                    var step = Step(TweenType.Scale);
                    step.Ease = Ease.InOutSine;
                    step.Delay = 0.2f;
                    step.DelayType = DelayType.EveryLoop;
                    step.Loops = 2;
                    return Animation(step);
                }

                case Scenario.AnimationYoyoLoops:
                {
                    var step = Step(TweenType.Move);
                    step.Ease = Ease.OutElastic;
                    var animation = Animation(step);
                    animation.Loops = 3;
                    animation.LoopType = LoopType.Yoyo;
                    return animation;
                }

                case Scenario.AnimationRestartLoopsOnAFade:
                {
                    var step = Step(TweenType.Fade);
                    step.From = new Vector4(1f, 0f, 0f, 0f);
                    step.To = Vector4.zero;
                    step.Ease = Ease.InQuad;
                    var animation = Animation(step);
                    animation.Loops = 2;
                    return animation;
                }

                case Scenario.Punch:
                    return Animation(Step(TweenType.Punch));

                case Scenario.SeededShake:
                {
                    var step = Step(TweenType.Shake);
                    step.RandomSeed = 42u;
                    return Animation(step);
                }

                case Scenario.Jump:
                {
                    var step = Step(TweenType.Jump);
                    step.JumpCount = 2;
                    step.JumpPower = 3f;
                    return Animation(step);
                }

                case Scenario.CustomProgress:
                {
                    var step = Step(TweenType.Custom);
                    step.Ease = Ease.OutCubic;
                    if (progress != null) step.OnProgress.AddListener(progress.Add);
                    return Animation(step);
                }

                default:
                    return Animation(Step(TweenType.Move));
            }
        }

        void BuildBoth(Scenario scenario, List<float> fastProgress = null, List<float> slowProgress = null)
        {
            var fastAnimation = Make(scenario, fastProgress);
            Assert.IsTrue(TweenAnimationRunner.CanRunAlone(fastAnimation, out _),
                scenario + " was expected to take the fast path");

            var errors = new List<string>();

            TweenAnimationRunner.FastPathEnabled = true;
            fastDriver = TweenAnimationRunner.Build(fastAnimation, fast, fastClock.Scheduler, errors);

            TweenAnimationRunner.FastPathEnabled = false;
            slowDriver = TweenAnimationRunner.Build(Make(scenario, slowProgress), slow, slowClock.Scheduler, errors);
            TweenAnimationRunner.FastPathEnabled = true;

            Assert.IsEmpty(errors, string.Join("\n", errors));
            Assert.IsTrue(fastDriver.IsActive() && slowDriver.IsActive());

            fastDriver.Preserve();
            slowDriver.Preserve();
        }

        void AssertSameState(string when)
        {
            var a = fast.transform;
            var b = slow.transform;

            Assert.That(Vector3.Distance(a.localPosition, b.localPosition), NUnit.Framework.Is.LessThan(Tolerance),
                "position " + when + ": " + a.localPosition.ToString("F6") + " vs " + b.localPosition.ToString("F6"));
            Assert.That(Vector3.Distance(a.localScale, b.localScale), NUnit.Framework.Is.LessThan(Tolerance),
                "scale " + when);
            Assert.AreEqual(slow.GetComponent<CanvasGroup>().alpha, fast.GetComponent<CanvasGroup>().alpha,
                Tolerance, "alpha " + when);
        }

        [Test]
        public void ScrubbingMatchesTheSequence([Values] Scenario scenario)
        {
            BuildBoth(scenario);

            Assert.AreEqual(slowDriver.TotalDuration, fastDriver.TotalDuration, 1e-6,
                "the preview's scrub range would differ");

            var total = (float)slowDriver.TotalDuration;
            for (var t = 0f; t <= total + 0.25f; t += 0.0625f)
            {
                fastDriver.Time = t;
                slowDriver.Time = t;
                AssertSameState("at t=" + t);
            }

            // Backwards too: the preview scrubs both ways.
            for (var t = total; t >= 0f; t -= 0.1f)
            {
                fastDriver.Time = t;
                slowDriver.Time = t;
                AssertSameState("scrubbing back at t=" + t);
            }
        }

        /// <summary>
        /// Every scenario whose value is a smooth function of time.
        /// </summary>
        /// <remarks>
        /// A seeded shake is left out. Its noise is a hash of the motion's exact time, and a
        /// sequence hands its children <c>duration * (float)progress</c> rather than the raw time,
        /// so the two paths key the hash on times one rounding step apart and draw different
        /// noise. Scrubbing to the same time agrees exactly (see above), and a seed still
        /// reproduces itself on either path (see below).
        /// </remarks>
        static Scenario[] SmoothScenarios =
        {
            Scenario.LinearMove,
            Scenario.OvershootWithDelayAndYoyoStepLoops,
            Scenario.DelayOnEveryStepLoop,
            Scenario.AnimationYoyoLoops,
            Scenario.AnimationRestartLoopsOnAFade,
            Scenario.Punch,
            Scenario.Jump,
            Scenario.CustomProgress,
        };

        [Test]
        public void ASeededShakeReproducesItselfOnTheFastPath()
        {
            var animation = Make(Scenario.SeededShake);
            Assert.IsTrue(TweenAnimationRunner.CanRunAlone(animation, out _));

            fastDriver = TweenAnimationRunner.Build(animation, fast, fastClock.Scheduler);
            slowDriver = TweenAnimationRunner.Build(Make(Scenario.SeededShake), slow, slowClock.Scheduler);

            var moved = false;
            for (var frame = 0; frame < 90; frame++)
            {
                fastClock.Update(Frame);
                slowClock.Update(Frame);
                AssertSameState("on frame " + frame);
                moved |= fast.transform.localPosition != Vector3.zero;
            }

            Assert.IsTrue(moved, "the shake never moved, so the comparison proved nothing");
        }

        [Test]
        public void PlayingInRealTimeMatchesTheSequence([ValueSource(nameof(SmoothScenarios))] Scenario scenario)
        {
            BuildBoth(scenario);

            // Applied by the runner after building, on whichever handle it returns.
            fastDriver.PlaybackSpeed = 1.5f;
            slowDriver.PlaybackSpeed = 1.5f;

            for (var frame = 0; frame < 300; frame++)
            {
                fastClock.Update(Frame);
                slowClock.Update(Frame);
                AssertSameState("on frame " + frame);
            }
        }

        [Test]
        public void ACustomStepReportsTheSameProgress()
        {
            var fastProgress = new List<float>();
            var slowProgress = new List<float>();
            BuildBoth(Scenario.CustomProgress, fastProgress, slowProgress);

            for (var t = 0f; t <= 1.1f; t += 0.1f)
            {
                fastDriver.Time = t;
                slowDriver.Time = t;
            }

            Assert.AreEqual(slowProgress.Count, fastProgress.Count);
            for (var i = 0; i < slowProgress.Count; i++)
            {
                Assert.AreEqual(slowProgress[i], fastProgress[i], Tolerance, "sample " + i);
            }
        }

        [Test]
        public void OnCompleteFiresOnceOnEitherPath([Values(1, 2)] int loops)
        {
            var fastCount = 0;
            var slowCount = 0;

            var fastAnimation = Make(Scenario.LinearMove);
            fastAnimation.Loops = loops;
            fastAnimation.OnComplete.AddListener(() => fastCount++);

            var slowAnimation = Make(Scenario.LinearMove);
            slowAnimation.Loops = loops;
            slowAnimation.OnComplete.AddListener(() => slowCount++);

            fastDriver = TweenAnimationRunner.Build(fastAnimation, fast, fastClock.Scheduler);
            TweenAnimationRunner.FastPathEnabled = false;
            slowDriver = TweenAnimationRunner.Build(slowAnimation, slow, slowClock.Scheduler);
            TweenAnimationRunner.FastPathEnabled = true;

            for (var frame = 0; frame < 60 * (loops + 1); frame++)
            {
                fastClock.Update(Frame);
                slowClock.Update(Frame);
            }

            Assert.AreEqual(1, slowCount, "sequence");
            Assert.AreEqual(1, fastCount, "fast path");
        }

        [Test]
        public void TheCompletionDelegateIsMadeOncePerEvent()
        {
            var animation = Make(Scenario.LinearMove);
            var first = animation.CompleteInvoker;

            Assert.AreSame(first, animation.CompleteInvoker, "a delegate per play is the garbage this removes");

            // OnComplete is a public field; replacing it must not keep invoking the old event.
            animation.OnComplete = new UnityEngine.Events.UnityEvent();
            Assert.AreNotSame(first, animation.CompleteInvoker);

            var clone = animation.Clone();
            Assert.AreNotSame(animation.CompleteInvoker, clone.CompleteInvoker,
                "a clone has its own event, so it needs its own delegate");
        }

        // --- What stays on the sequence ---

        [Test]
        public void StepsThatCannotStandAloneKeepTheSequence()
        {
            void Expect(bool expected, TweenAnimation animation, string why)
            {
                Assert.AreEqual(expected, TweenAnimationRunner.CanRunAlone(animation, out _), why);
            }

            Expect(true, Animation(Step(TweenType.Move)), "a plain step");

            var disabled = Step(TweenType.Scale);
            disabled.Enabled = false;
            Expect(true, Animation(Step(TweenType.Move), disabled), "a disabled step does not count");

            Expect(false, Animation(Step(TweenType.Move), Step(TweenType.Scale)), "two steps");

            var late = Step(TweenType.Move);
            late.StartTime = 0.5f;
            Expect(false, Animation(late), "a step that starts later");

            Expect(false, Animation(Step(TweenType.Callback)), "a callback completes into its own event");
            Expect(false, Animation(Step(TweenType.TMPCharacter)), "per-character builds many motions");

            var anchors = Step(TweenType.Anchors);
            anchors.AnchorTarget = TweenAnchorTarget.Both;
            Expect(false, Animation(anchors), "both anchors build two motions");

            var stepLoops = Step(TweenType.Move);
            stepLoops.Loops = 2;
            var both = Animation(stepLoops);
            both.Loops = 2;
            Expect(false, both, "step loops and animation loops cannot share one motion");

            var delayed = Step(TweenType.Move);
            delayed.Delay = 0.2f;
            var delayedLooping = Animation(delayed);
            delayedLooping.Loops = 2;
            Expect(false, delayedLooping, "a sequence replays the delay on every pass");

            var infinite = Animation(Step(TweenType.Move));
            infinite.Loops = -1;
            Expect(true, infinite, "an endless animation is fine when the step itself does not loop");

            foreach (var loopType in new[] { LoopType.Flip, LoopType.Incremental })
            {
                var looping = Animation(Step(TweenType.Move));
                looping.Loops = 2;
                looping.LoopType = loopType;
                Expect(false, looping, loopType + " means something else on the sequence's driver");
            }
        }

        [Test]
        public void ALoopingStepWithoutAnimationLoopsStillTakesTheFastPath()
        {
            var step = Step(TweenType.Move);
            step.Loops = 3;
            step.Delay = 0.1f;
            Assert.IsTrue(TweenAnimationRunner.CanRunAlone(Animation(step), out _));
        }
    }
}
