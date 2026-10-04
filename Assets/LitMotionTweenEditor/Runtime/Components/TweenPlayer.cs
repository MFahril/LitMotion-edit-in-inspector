using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace LitMotion.TweenEditor
{
    /// <summary>
    /// Holds named tween animations authored in the inspector and plays them by name.
    /// </summary>
    /// <remarks>
    /// Animations are addressed by string id so that built-in names
    /// (see <see cref="TweenAnimationId"/>) and project-specific ones work the same way.
    ///
    /// Every play is on a <i>target</i>: the GameObject that steps without an explicit Target
    /// animate. It is this player's own GameObject unless one is passed, which is how one player
    /// can drive pooled or spawned objects. Override blending, stopping and pausing all work per
    /// id; the overloads taking a target narrow them to that object.
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
            public GameObject Target;
            public MotionHandle Handle;
            public TweenValueSnapshot Snapshot;
            public TweenKillBehavior KillBehavior;

            // The handle's own speed, put back on resume. Pausing is a speed of zero, which is
            // how LitMotion pauses, so the animation's PlaybackSpeed has to be remembered.
            public bool Paused;
            public float ResumeSpeed;
        }

        /// <summary>The state behind one <see cref="PlayAsync(string, CancellationToken)"/>.</summary>
        sealed class AsyncPlay
        {
            public readonly AwaitableCompletionSource<bool> Source = new();
            public readonly Action OnComplete;
            public readonly Action OnCancel;
            public TweenPlayer Player;
            public MotionHandle Handle;
            public CancellationTokenRegistration Registration;

            public AsyncPlay()
            {
                OnComplete = () => Finish(true);
                OnCancel = () => Finish(false);
            }

            void Finish(bool completed)
            {
                Registration.Dispose();
                Source.TrySetResult(completed);
            }

            public void CancelFromToken()
            {
                if (Player != null) Player.StopHandle(Handle);
                Finish(false);
            }
        }

        readonly List<RunningAnimation> running = new();
        readonly List<string> buildErrors = new();

        // Finished entries and snapshots, reused by the next play instead of reallocated.
        readonly Stack<RunningAnimation> spareEntries = new();
        readonly Stack<TweenValueSnapshot> spareSnapshots = new();

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

        // --- Lookup ---

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

        TweenAnimation FindOrWarn(string id)
        {
            var animation = Find(id);
            if (animation == null && logBindingWarnings)
            {
                Debug.LogWarning($"[TweenPlayer] No animation named '{id}' on '{name}'.", this);
            }

            return animation;
        }

        // --- Playing ---

        /// <summary>
        /// Plays the animation with the given id on this player's GameObject.
        /// </summary>
        /// <returns>
        /// The animation's driver handle, or <see cref="MotionHandle.None"/> when the animation
        /// does not exist or produced no motions. The handle can be awaited directly.
        /// </returns>
        public MotionHandle Play(string id)
        {
            var animation = FindOrWarn(id);
            return animation == null ? MotionHandle.None : PlayCore(animation, null, null, null);
        }

        /// <summary>
        /// Plays the animation with the given id on <paramref name="target"/> instead of this
        /// player's own GameObject.
        /// </summary>
        /// <remarks>
        /// Only steps without an explicit Target follow <paramref name="target"/>; a step that
        /// names its own object still animates that object. Override blending replaces a play of
        /// the same id on the same target only, so one player can run the same animation on many
        /// pooled objects at once.
        /// </remarks>
        public MotionHandle Play(string id, GameObject target)
        {
            var animation = FindOrWarn(id);
            return animation == null ? MotionHandle.None : PlayCore(animation, target, null, null);
        }

        /// <summary>
        /// Plays the animation with the given id and calls <paramref name="onComplete"/> when this
        /// play finishes.
        /// </summary>
        /// <remarks>
        /// Not called when the play is stopped or cancelled, matching the animation's own
        /// <c>OnComplete</c> event, which still fires as well.
        /// </remarks>
        public MotionHandle Play(string id, Action onComplete)
        {
            var animation = FindOrWarn(id);
            return animation == null ? MotionHandle.None : PlayCore(animation, null, onComplete, null);
        }

        /// <summary>Plays an animation directly, bypassing id lookup.</summary>
        public MotionHandle Play(TweenAnimation animation)
        {
            return animation == null ? MotionHandle.None : PlayCore(animation, null, null, null);
        }

        /// <summary>Plays an animation directly on <paramref name="target"/>.</summary>
        public MotionHandle Play(TweenAnimation animation, GameObject target)
        {
            return animation == null ? MotionHandle.None : PlayCore(animation, target, null, null);
        }

        /// <summary>
        /// Plays the animation with the given id and completes when it ends.
        /// </summary>
        /// <param name="id">Animation to play.</param>
        /// <param name="cancellationToken">
        /// Stops the play when cancelled, applying its kill behavior. Cancel it from the main
        /// thread, as with any Unity object.
        /// </param>
        /// <returns>
        /// True when the animation finished, or was completed; false when it was stopped,
        /// cancelled, replaced by another play of the same id, or never started. It never throws
        /// for those, so <c>if (await player.PlayAsync("Hide")) Close();</c> reads the way it
        /// should.
        /// </returns>
        public Awaitable<bool> PlayAsync(string id, CancellationToken cancellationToken = default)
        {
            return PlayAsync(id, null, cancellationToken);
        }

        /// <inheritdoc cref="PlayAsync(string, CancellationToken)"/>
        /// <param name="target">Object to play on instead of this player's own GameObject.</param>
        public Awaitable<bool> PlayAsync(string id, GameObject target, CancellationToken cancellationToken = default)
        {
            var play = new AsyncPlay();

            var animation = FindOrWarn(id);
            if (animation == null || cancellationToken.IsCancellationRequested)
            {
                play.Source.SetResult(false);
                return play.Source.Awaitable;
            }

            play.Handle = PlayCore(animation, target, play.OnComplete, play.OnCancel);
            if (!play.Handle.IsActive())
            {
                play.Source.TrySetResult(false);
                return play.Source.Awaitable;
            }

            if (cancellationToken.CanBeCanceled)
            {
                play.Player = this;
                play.Registration = cancellationToken.Register(play.CancelFromToken);
            }

            return play.Source.Awaitable;
        }

        MotionHandle PlayCore(TweenAnimation animation, GameObject target, Action onComplete, Action onCancel)
        {
            if (target == null) target = gameObject;

            PruneFinished();

            // Override replaces whatever is already running under this id on this target;
            // Additive layers on top.
            if (animation.BlendMode == TweenBlendMode.Override) StopMatching(animation.Id, target);

            // Only Rewind ever puts the captured values back, so the other kill behaviors skip
            // the capture: it resolves every step and reads every channel it touches. Captured
            // before building, because building reads the live values that FromCurrent steps
            // depend on and Rewind must put back.
            TweenValueSnapshot snapshot = null;
            if (animation.KillBehavior == TweenKillBehavior.Rewind)
            {
                snapshot = spareSnapshots.Count > 0 ? spareSnapshots.Pop() : new TweenValueSnapshot();
                snapshot.CaptureAnimation(animation, target);
            }

            buildErrors.Clear();
            var scheduler = TweenAnimationRunner.GetRuntimeScheduler(animation);
            var handle = TweenAnimationRunner.Build(animation, target, scheduler, buildErrors, onComplete, onCancel);

            if (logBindingWarnings && buildErrors.Count > 0)
            {
                for (var i = 0; i < buildErrors.Count; i++)
                {
                    Debug.LogWarning($"[TweenPlayer] {buildErrors[i]}", this);
                }
            }

            if (!handle.IsActive())
            {
                RecycleSnapshot(snapshot);
                return MotionHandle.None;
            }

            var entry = spareEntries.Count > 0 ? spareEntries.Pop() : new RunningAnimation();
            entry.Id = animation.Id;
            entry.Target = target;
            entry.Handle = handle;
            entry.Snapshot = snapshot;
            entry.KillBehavior = animation.KillBehavior;
            entry.Paused = false;
            running.Add(entry);

            return handle;
        }

        /// <summary>Stops the animation if running, then plays it from the start.</summary>
        public MotionHandle Restart(string id)
        {
            StopInternal(id);
            return Play(id);
        }

        // --- State ---

        /// <summary>
        /// True when an animation with this id is running on any target. Still true while paused.
        /// </summary>
        public bool IsPlaying(string id)
        {
            PruneFinished();

            for (var i = 0; i < running.Count; i++)
            {
                if (running[i].Id == id) return true;
            }

            return false;
        }

        /// <summary>True when an animation with this id is running on <paramref name="target"/>.</summary>
        public bool IsPlaying(string id, GameObject target)
        {
            PruneFinished();
            if (target == null) target = gameObject;

            for (var i = 0; i < running.Count; i++)
            {
                if (running[i].Id == id && running[i].Target == target) return true;
            }

            return false;
        }

        /// <summary>True when anything this player started is still running.</summary>
        public bool IsPlayingAny()
        {
            PruneFinished();
            return running.Count > 0;
        }

        /// <summary>True when an animation with this id is running and paused.</summary>
        public bool IsPaused(string id)
        {
            PruneFinished();

            for (var i = 0; i < running.Count; i++)
            {
                if (running[i].Id == id && running[i].Paused) return true;
            }

            return false;
        }

        // --- Stopping ---

        /// <summary>
        /// Stops the animation with the given id on every target, applying its kill behavior.
        /// </summary>
        public void Stop(string id) => StopInternal(id);

        /// <summary>Stops the animation with the given id on <paramref name="target"/> only.</summary>
        public void Stop(string id, GameObject target)
        {
            StopMatching(id, target == null ? gameObject : target);
        }

        /// <summary>Stops every animation this player started.</summary>
        public void StopAll()
        {
            for (var i = running.Count - 1; i >= 0; i--)
            {
                if (i >= running.Count)
                {
                    // A callback from the previous teardown stopped other plays.
                    i = running.Count;
                    continue;
                }

                var entry = Detach(i);
                Teardown(entry);
                Recycle(entry);
            }
        }

        /// <summary>
        /// Jumps the animation with the given id straight to its end value and releases it.
        /// </summary>
        public void Complete(string id)
        {
            for (var i = running.Count - 1; i >= 0; i--)
            {
                if (i >= running.Count)
                {
                    i = running.Count;
                    continue;
                }

                if (running[i].Id != id) continue;

                var entry = Detach(i);
                if (entry.Handle.IsActive()) entry.Handle.TryComplete();
                Recycle(entry);
            }
        }

        // --- Pausing ---

        /// <summary>Pauses the animation with the given id on every target.</summary>
        /// <remarks>
        /// A paused animation keeps its place and its claim on the values it writes, and still
        /// counts as playing. Stopping a paused animation applies its kill behavior as usual.
        /// </remarks>
        public void Pause(string id)
        {
            for (var i = 0; i < running.Count; i++)
            {
                if (running[i].Id == id) PauseEntry(running[i]);
            }
        }

        /// <summary>Resumes the animation with the given id at the speed it was paused at.</summary>
        public void Resume(string id)
        {
            for (var i = 0; i < running.Count; i++)
            {
                if (running[i].Id == id) ResumeEntry(running[i]);
            }
        }

        /// <summary>Pauses everything this player started.</summary>
        public void PauseAll()
        {
            for (var i = 0; i < running.Count; i++) PauseEntry(running[i]);
        }

        /// <summary>Resumes everything this player paused.</summary>
        public void ResumeAll()
        {
            for (var i = 0; i < running.Count; i++) ResumeEntry(running[i]);
        }

        static void PauseEntry(RunningAnimation entry)
        {
            if (entry.Paused || !entry.Handle.IsActive()) return;

            entry.ResumeSpeed = entry.Handle.PlaybackSpeed;
            entry.Handle.PlaybackSpeed = 0f;
            entry.Paused = true;
        }

        static void ResumeEntry(RunningAnimation entry)
        {
            if (!entry.Paused) return;

            if (entry.Handle.IsActive()) entry.Handle.PlaybackSpeed = entry.ResumeSpeed;
            entry.Paused = false;
        }

        // --- Bookkeeping ---
        //
        // Stopping or completing a play runs callbacks synchronously: the animation's OnComplete,
        // a per-play callback, an awaiting caller's continuation. Any of them may play or stop
        // on this player again. So each entry is taken out of the list before it is torn down,
        // and the loops tolerate the list changing between iterations.

        void StopInternal(string id) => StopWhere(id, null, true);

        void StopMatching(string id, GameObject target) => StopWhere(id, target, false);

        void StopWhere(string id, GameObject target, bool anyTarget)
        {
            for (var i = running.Count - 1; i >= 0; i--)
            {
                if (i >= running.Count)
                {
                    i = running.Count;
                    continue;
                }

                var candidate = running[i];
                if (candidate.Id != id || (!anyTarget && candidate.Target != target)) continue;

                var entry = Detach(i);
                Teardown(entry);
                Recycle(entry);
            }
        }

        /// <summary>Stops the one play behind <paramref name="handle"/>, if it is still ours.</summary>
        void StopHandle(MotionHandle handle)
        {
            for (var i = running.Count - 1; i >= 0; i--)
            {
                if (running[i].Handle != handle) continue;

                var entry = Detach(i);
                Teardown(entry);
                Recycle(entry);
                return;
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
                if (!running[i].Handle.IsActive()) Recycle(Detach(i));
            }
        }

        /// <summary>Takes a running entry out of the list, before anything can call back.</summary>
        RunningAnimation Detach(int index)
        {
            var entry = running[index];
            running.RemoveAt(index);
            return entry;
        }

        /// <summary>Keeps a finished entry, and its snapshot, for reuse.</summary>
        void Recycle(RunningAnimation entry)
        {
            RecycleSnapshot(entry.Snapshot);
            entry.Id = null;
            entry.Target = null;
            entry.Handle = MotionHandle.None;
            entry.Snapshot = null;
            entry.Paused = false;
            spareEntries.Push(entry);
        }

        void RecycleSnapshot(TweenValueSnapshot snapshot)
        {
            if (snapshot == null) return;

            snapshot.Clear();
            spareSnapshots.Push(snapshot);
        }
    }
}
