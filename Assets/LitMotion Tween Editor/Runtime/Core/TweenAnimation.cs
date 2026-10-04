using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace LitMotion.TweenEditor
{
    /// <summary>
    /// A named, ordered collection of <see cref="TweenStep"/>s played as one unit.
    /// </summary>
    [Serializable]
    public sealed class TweenAnimation
    {
        [Tooltip("Name used to play this animation, e.g. \"Show\". Any string is valid.")]
        public string Id = TweenAnimationId.Show;

        [Tooltip("Steps on this animation's timeline. Order in this list is display order only; " +
                 "actual timing comes from each step's StartTime.")]
        public List<TweenStep> Steps = new();

        [Tooltip("Times the whole animation repeats. Negative means loop forever.")]
        public int Loops = 1;

        public LoopType LoopType = LoopType.Restart;

        [Tooltip("Playback rate multiplier. 2 plays twice as fast.")]
        public float PlaybackSpeed = 1f;

        [Tooltip("Ignore Time.timeScale, so this still plays while the game is paused.")]
        public bool IgnoreTimeScale;

        [Tooltip("What happens when this animation is played while already running.")]
        public TweenBlendMode BlendMode = TweenBlendMode.Override;

        [Tooltip("How the running instance is torn down when overridden or stopped.")]
        public TweenKillBehavior KillBehavior = TweenKillBehavior.Cancel;

        [Tooltip("Invoked when the animation finishes on its own. Not invoked when cancelled.")]
        public UnityEvent OnComplete = new();

        /// <summary>
        /// Length of the animation for a single loop, in seconds: the latest end time across
        /// enabled steps. Returns <see cref="float.PositiveInfinity"/> if any step loops forever.
        /// </summary>
        public float Duration
        {
            get
            {
                var max = 0f;
                for (var i = 0; i < Steps.Count; i++)
                {
                    var step = Steps[i];
                    if (step == null || !step.Enabled) continue;

                    var end = step.EndTime;
                    if (float.IsPositiveInfinity(end)) return float.PositiveInfinity;
                    if (end > max) max = end;
                }

                return max;
            }
        }

        /// <summary>Number of steps that will actually be scheduled.</summary>
        public int EnabledStepCount
        {
            get
            {
                var count = 0;
                for (var i = 0; i < Steps.Count; i++)
                {
                    if (Steps[i] is { Enabled: true }) count++;
                }

                return count;
            }
        }

        /// <summary>Creates an independent copy, deep-copying every step.</summary>
        public TweenAnimation Clone()
        {
            var clone = (TweenAnimation)MemberwiseClone();
            clone.Steps = new List<TweenStep>(Steps.Count);
            for (var i = 0; i < Steps.Count; i++)
            {
                clone.Steps.Add(Steps[i]?.Clone());
            }

            clone.OnComplete = new UnityEvent();
            return clone;
        }
    }
}
