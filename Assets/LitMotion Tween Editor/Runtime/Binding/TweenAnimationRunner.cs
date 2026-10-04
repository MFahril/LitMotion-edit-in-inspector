using System;
using System.Collections.Generic;
using LitMotion.Adapters;
using UnityEngine;

namespace LitMotion.TweenEditor
{
    /// <summary>
    /// Assembles a <see cref="TweenAnimation"/> into a single LitMotion sequence.
    /// </summary>
    /// <remarks>
    /// The returned handle is the sequence's driver: completing, cancelling or setting
    /// <c>Time</c> on it controls every step at once. That single scrubbable handle is what lets
    /// the editor preview use this exact code path rather than an editor-only reimplementation.
    ///
    /// An animation whose only step can carry the animation's own settings skips the sequence
    /// and returns that step's motion as the driver instead. LitMotion updates sequence children
    /// one at a time on the main thread, outside its Burst job, so a sequence of one costs about
    /// four times as much per frame as the motion alone. The preview still runs whatever this
    /// returns, so the fast path is the production path in both places.
    /// </remarks>
    public static class TweenAnimationRunner
    {
        // Reused across calls so that building an animation allocates no scratch lists.
        // Playback is always driven from the main thread, so a shared buffer is safe.
        static readonly List<MotionHandle> StepHandles = new();

        // The sequence settings for the Build call in progress, read by ConfigureDriver. Run
        // invokes its configuration synchronously, so these are only ever read inside the call
        // that set them, and a cached delegate replaces a capturing closure per Build.
        static int pendingLoops;
        static LoopType pendingLoopType;
        static IMotionScheduler pendingScheduler;
        static Action pendingOnComplete;

        static readonly Action<MotionBuilder<double, NoOptions, DoubleMotionAdapter>> ConfigureDriver = builder =>
        {
            if (pendingLoops != 1) builder.WithLoops(pendingLoops, pendingLoopType);
            if (pendingScheduler != null) builder.WithScheduler(pendingScheduler);
            if (pendingOnComplete != null) builder.WithOnComplete(pendingOnComplete);
        };

        /// <summary>
        /// Lets tests force every animation through a sequence, to compare it with the fast path.
        /// </summary>
        internal static bool FastPathEnabled = true;

        /// <summary>
        /// Builds and schedules <paramref name="animation"/>.
        /// </summary>
        /// <param name="animation">Animation to build.</param>
        /// <param name="fallback">GameObject used for steps with no explicit target.</param>
        /// <param name="scheduler">
        /// Scheduler for every created motion, or null to let LitMotion decide. Pass null at
        /// runtime; the editor preview passes its own manual scheduler so it owns the clock.
        /// </param>
        /// <param name="errors">Optional list that receives per-step resolution failures.</param>
        /// <returns>
        /// The driver handle, or <see cref="MotionHandle.None"/> when the animation produced no
        /// motions at all.
        /// </returns>
        public static MotionHandle Build(TweenAnimation animation, GameObject fallback,
            IMotionScheduler scheduler, List<string> errors = null)
        {
            if (animation?.Steps == null || animation.Steps.Count == 0) return MotionHandle.None;

            var driver = FastPathEnabled && CanRunAlone(animation, out var sole)
                ? BuildAlone(animation, sole, fallback, scheduler, errors)
                : BuildSequence(animation, fallback, scheduler, errors);

            if (!driver.IsActive()) return MotionHandle.None;

            var speed = animation.PlaybackSpeed;
            if (speed > 0f && !Mathf.Approximately(speed, 1f)) driver.PlaybackSpeed = speed;

            return driver;
        }

        static MotionHandle BuildSequence(TweenAnimation animation, GameObject fallback,
            IMotionScheduler scheduler, List<string> errors)
        {
            var sequence = LSequence.Create();
            var inserted = 0;

            for (var i = 0; i < animation.Steps.Count; i++)
            {
                var step = animation.Steps[i];
                if (step == null || !step.Enabled) continue;

                StepHandles.Clear();
                var built = TweenStepBuilder.Build(step, fallback, scheduler, StepHandles, out var error);

                if (error != null) errors?.Add(error);
                if (built == 0) continue;

                for (var h = 0; h < StepHandles.Count; h++)
                {
                    // Insert rather than Append: a step's StartTime is an absolute offset from
                    // the start of the animation, which is what the timeline UI edits.
                    sequence.Insert(Mathf.Max(0f, step.StartTime), StepHandles[h]);
                    inserted++;
                }
            }

            StepHandles.Clear();

            if (inserted == 0)
            {
                // Nothing to drive. The builder owns a pooled buffer, so it must be released
                // explicitly when we do not call Run.
                sequence.Dispose();
                return MotionHandle.None;
            }

            pendingLoops = animation.Loops;
            pendingLoopType = animation.LoopType;
            pendingScheduler = scheduler;
            pendingOnComplete = animation.CompleteInvoker;

            try
            {
                return sequence.Run(ConfigureDriver);
            }
            finally
            {
                // Not holding on to a scheduler or an event past the call that used them.
                pendingScheduler = null;
                pendingOnComplete = null;
            }
        }

        static MotionHandle BuildAlone(TweenAnimation animation, TweenStep step, GameObject fallback,
            IMotionScheduler scheduler, List<string> errors)
        {
            StepHandles.Clear();
            TweenStepBuilder.BuildAlone(step, fallback, scheduler, StepHandles, out var error,
                animation.Loops, animation.LoopType, animation.CompleteInvoker);

            if (error != null) errors?.Add(error);

            var driver = StepHandles.Count == 1 ? StepHandles[0] : MotionHandle.None;
            StepHandles.Clear();
            return driver;
        }

        /// <summary>
        /// True when <paramref name="animation"/> has exactly one enabled step, and that step's
        /// own motion can carry the animation's settings without a sequence around it.
        /// </summary>
        /// <remarks>
        /// The step must start at zero, build exactly one motion, and not use its own completion
        /// callback. Animation-level looping folds in only when the step neither loops nor
        /// delays by itself -- a sequence replays a child's delay on every pass, which one motion
        /// with <see cref="DelayType.FirstLoop"/> would not -- and only for the loop types that
        /// mean the same thing on both.
        /// </remarks>
        internal static bool CanRunAlone(TweenAnimation animation, out TweenStep sole)
        {
            sole = null;
            if (animation?.Steps == null) return false;

            for (var i = 0; i < animation.Steps.Count; i++)
            {
                var step = animation.Steps[i];
                if (step == null || !step.Enabled) continue;
                if (sole != null) return false;
                sole = step;
            }

            if (sole == null || sole.StartTime > 0f) return false;

            switch (sole.Type)
            {
                // Completes into its own callback, which the animation's OnComplete would replace.
                case TweenType.Callback:
                // Build more than one motion.
                case TweenType.TMPCharacter:
                case TweenType.Anchors when sole.AnchorTarget == TweenAnchorTarget.Both:
                    return false;
            }

            if (animation.Loops != 1)
            {
                if (TweenStepBuilder.EffectiveLoops(sole) != 1 || sole.Delay > 0f) return false;

                // Restart and Yoyo replay the step's time, which one motion reproduces exactly.
                // The others mean something else on a sequence's linear driver than on an
                // eased motion: Incremental on the driver runs children past their end rather
                // than offsetting them, and Flip on the driver reverses time, not the output.
                if (animation.LoopType is not (LoopType.Restart or LoopType.Yoyo)) return false;
            }

            return true;
        }

        /// <summary>
        /// Picks the scheduler for runtime playback.
        /// </summary>
        /// <remarks>
        /// Returns null for the common case so LitMotion applies its own default, which is also
        /// what routes edit-mode playback through its editor dispatcher automatically.
        /// </remarks>
        public static IMotionScheduler GetRuntimeScheduler(TweenAnimation animation)
        {
            if (animation == null) return null;
            return animation.IgnoreTimeScale ? MotionScheduler.UpdateIgnoreTimeScale : null;
        }
    }
}
