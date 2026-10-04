using System.Collections.Generic;
using UnityEngine;

namespace LitMotion.TweenEditor
{
    /// <summary>
    /// Holds named tween animations authored in the inspector and plays them by name.
    /// </summary>
    /// <remarks>
    /// Animations are addressed by string id so that built-in names
    /// (see <see cref="TweenAnimationId"/>) and project-specific ones work the same way.
    /// </remarks>
    [AddComponentMenu("LitMotion/Tween Player")]
    [DisallowMultipleComponent]
    public sealed class TweenPlayer : MonoBehaviour
    {
        [Tooltip("Animations defined on this player. Each is played by its Id.")]
        [SerializeField] List<TweenAnimation> animations = new();

        [Tooltip("Shared animation assets this player can also play, addressed by the same ids. " +
                 "An inline animation with the same id wins.")]
        [SerializeField] List<TweenAnimationAsset> animationAssets = new();

        [Space]
        [Tooltip("Animation played automatically when this component is enabled. Leave empty for none.")]
        [SerializeField] string playOnEnableId = string.Empty;

        [Tooltip("Log a warning when a step cannot find a component to animate.")]
        [SerializeField] bool logBindingWarnings = true;

        /// <summary>A scheduled animation and the state needed to tear it down.</summary>
        sealed class RunningAnimation
        {
            public string Id;
            public MotionHandle Handle;
            public TweenValueSnapshot Snapshot;
            public TweenKillBehavior KillBehavior;
        }

        readonly List<RunningAnimation> running = new();
        readonly List<string> buildErrors = new();

        /// <summary>The animations defined inline on this player.</summary>
        public IReadOnlyList<TweenAnimation> Animations => animations;

        /// <summary>The shared animation assets this player can play.</summary>
        public IReadOnlyList<TweenAnimationAsset> AnimationAssets => animationAssets;

        void OnEnable()
        {
            if (!string.IsNullOrEmpty(playOnEnableId)) Play(playOnEnableId);
        }

        void OnDisable()
        {
            // Leaving motions alive past disable would write to a hidden object every frame.
            StopAll();
        }

        void OnDestroy()
        {
            StopAll();
        }

        /// <summary>Finds an animation by id, or null when there is no such animation.</summary>
        /// <remarks>
        /// Inline animations are searched before assets, so a player can override a shared
        /// preset for one object without editing the asset or detaching it.
        /// </remarks>
        public TweenAnimation Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;

            for (var i = 0; i < animations.Count; i++)
            {
                var animation = animations[i];
                if (animation != null && animation.Id == id) return animation;
            }

            for (var i = 0; i < animationAssets.Count; i++)
            {
                var asset = animationAssets[i];
                if (asset != null && asset.Animation != null && asset.Id == id) return asset.Animation;
            }

            return null;
        }

        /// <summary>True when an animation with this id is currently playing.</summary>
        public bool IsPlaying(string id)
        {
            PruneFinished();

            for (var i = 0; i < running.Count; i++)
            {
                if (running[i].Id == id) return true;
            }

            return false;
        }

        /// <summary>
        /// Plays the animation with the given id.
        /// </summary>
        /// <returns>
        /// The sequence driver handle, or <see cref="MotionHandle.None"/> when the animation does
        /// not exist or produced no motions.
        /// </returns>
        public MotionHandle Play(string id)
        {
            var animation = Find(id);
            if (animation == null)
            {
                if (logBindingWarnings)
                {
                    Debug.LogWarning($"[TweenPlayer] No animation named '{id}' on '{name}'.", this);
                }

                return MotionHandle.None;
            }

            return Play(animation);
        }

        /// <summary>Plays an animation directly, bypassing id lookup.</summary>
        public MotionHandle Play(TweenAnimation animation)
        {
            if (animation == null) return MotionHandle.None;

            PruneFinished();

            // Override replaces whatever is already running under this id; Additive layers on top.
            if (animation.BlendMode == TweenBlendMode.Override) StopInternal(animation.Id);

            // Captured before building, because building reads the live values that FromCurrent
            // steps depend on and Rewind must put back.
            var snapshot = new TweenValueSnapshot();
            snapshot.CaptureAnimation(animation, gameObject);

            buildErrors.Clear();
            var scheduler = TweenAnimationRunner.GetRuntimeScheduler(animation);
            var handle = TweenAnimationRunner.Build(animation, gameObject, scheduler, buildErrors);

            if (logBindingWarnings && buildErrors.Count > 0)
            {
                for (var i = 0; i < buildErrors.Count; i++)
                {
                    Debug.LogWarning($"[TweenPlayer] {buildErrors[i]}", this);
                }
            }

            if (!handle.IsActive()) return MotionHandle.None;

            running.Add(new RunningAnimation
            {
                Id = animation.Id,
                Handle = handle,
                Snapshot = snapshot,
                KillBehavior = animation.KillBehavior,
            });

            return handle;
        }

        /// <summary>
        /// Stops the animation with the given id, applying its configured kill behavior.
        /// </summary>
        public void Stop(string id) => StopInternal(id);

        /// <summary>Stops every animation this player started.</summary>
        public void StopAll()
        {
            for (var i = running.Count - 1; i >= 0; i--)
            {
                Teardown(running[i]);
                running.RemoveAt(i);
            }
        }

        /// <summary>
        /// Jumps the animation with the given id straight to its end value and releases it.
        /// </summary>
        public void Complete(string id)
        {
            for (var i = running.Count - 1; i >= 0; i--)
            {
                var entry = running[i];
                if (entry.Id != id) continue;

                if (entry.Handle.IsActive()) entry.Handle.TryComplete();
                running.RemoveAt(i);
            }
        }

        /// <summary>Stops the animation if running, then plays it from the start.</summary>
        public MotionHandle Restart(string id)
        {
            StopInternal(id);
            return Play(id);
        }

        void StopInternal(string id)
        {
            for (var i = running.Count - 1; i >= 0; i--)
            {
                var entry = running[i];
                if (entry.Id != id) continue;

                Teardown(entry);
                running.RemoveAt(i);
            }
        }

        void Teardown(RunningAnimation entry)
        {
            if (entry.Handle.IsActive())
            {
                switch (entry.KillBehavior)
                {
                    case TweenKillBehavior.Complete:
                        entry.Handle.TryComplete();
                        break;
                    default:
                        entry.Handle.TryCancel();
                        break;
                }
            }

            // Rewind restores after cancelling, so the restore is not immediately overwritten
            // by a final motion update.
            if (entry.KillBehavior == TweenKillBehavior.Rewind) entry.Snapshot?.Restore();
        }

        /// <summary>Drops entries whose motions have finished on their own.</summary>
        void PruneFinished()
        {
            for (var i = running.Count - 1; i >= 0; i--)
            {
                if (!running[i].Handle.IsActive()) running.RemoveAt(i);
            }
        }
    }
}
