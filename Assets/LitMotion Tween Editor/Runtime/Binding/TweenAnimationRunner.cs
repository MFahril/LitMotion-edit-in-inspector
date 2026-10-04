using System.Collections.Generic;
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
    /// </remarks>
    public static class TweenAnimationRunner
    {
        // Reused across calls so that building an animation allocates no scratch lists.
        // Playback is always driven from the main thread, so a shared buffer is safe.
        static readonly List<MotionHandle> StepHandles = new();

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
        /// The sequence driver handle, or <see cref="MotionHandle.None"/> when the animation
        /// produced no motions at all.
        /// </returns>
        public static MotionHandle Build(TweenAnimation animation, GameObject fallback,
            IMotionScheduler scheduler, List<string> errors = null)
        {
            if (animation?.Steps == null || animation.Steps.Count == 0) return MotionHandle.None;

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

            if (inserted == 0)
            {
                // Nothing to drive. The builder owns a pooled buffer, so it must be released
                // explicitly when we do not call Run.
                sequence.Dispose();
                return MotionHandle.None;
            }

            var loops = animation.Loops;
            var loopType = animation.LoopType;
            var onComplete = animation.OnComplete;

            var driver = sequence.Run(builder =>
            {
                if (loops != 1) builder.WithLoops(loops, loopType);
                if (scheduler != null) builder.WithScheduler(scheduler);
                if (onComplete != null) builder.WithOnComplete(onComplete.Invoke);
            });

            var speed = animation.PlaybackSpeed;
            if (speed > 0f && !Mathf.Approximately(speed, 1f)) driver.PlaybackSpeed = speed;

            return driver;
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
