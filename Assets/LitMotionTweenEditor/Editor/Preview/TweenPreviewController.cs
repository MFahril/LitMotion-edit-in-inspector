using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LitMotion.TweenEditor.Editor
{
    /// <summary>
    /// Plays and scrubs a <see cref="TweenAnimation"/> in the editor without entering play mode.
    /// </summary>
    /// <remarks>
    /// This deliberately runs the production code path: it asks
    /// <see cref="TweenAnimationRunner"/> for the same <c>LSequence</c> the runtime builds and
    /// scrubs the resulting driver handle. Nothing about easing, looping or value interpolation
    /// is reimplemented here, so a preview cannot drift from what actually ships.
    ///
    /// Two details make that safe in the editor:
    /// <list type="bullet">
    /// <item>Motions are scheduled on a <b>private</b> <see cref="ManualMotionDispatcher"/>, never
    /// <c>ManualMotionDispatcher.Default</c>. A private instance is never advanced by anything
    /// else, so the preview owns the clock outright and cannot disturb a user's own motions.</item>
    /// <item>The driver is <c>Preserve()</c>d. Without that, scrubbing past the end would
    /// complete the sequence and release the handle, and the next scrub would throw.</item>
    /// </list>
    ///
    /// Teardown restores every written value and must happen on every exit path, including the
    /// ones that are easy to forget: assembly reload, entering play mode, and the inspector
    /// being destroyed. <see cref="Dispose"/> is wired to all of them.
    /// </remarks>
    public sealed class TweenPreviewController : IDisposable
    {
        readonly ManualMotionDispatcher dispatcher = new();
        readonly TweenValueSnapshot snapshot = new();
        readonly TweenSceneDirtyGuard dirtyGuard = new();
        readonly List<string> buildErrors = new();

        MotionHandle driver;
        TweenAnimation animation;
        GameObject fallback;

        double lastEditorTime;
        float time;
        bool playing;
        bool hooked;

        /// <summary>Raised whenever the playhead moves, so the inspector can repaint.</summary>
        public event Action Changed;

        /// <summary>True while a preview is set up and scrubbable.</summary>
        public bool IsActive => driver.IsActive();

        /// <summary>True while the preview is advancing on its own.</summary>
        public bool IsPlaying => playing && IsActive;

        /// <summary>The animation being previewed, or null.</summary>
        public TweenAnimation Animation => animation;

        /// <summary>Per-step binding failures reported when the preview was built.</summary>
        public IReadOnlyList<string> BuildErrors => buildErrors;

        /// <summary>
        /// Wrap back to the start when playback reaches the end, instead of stopping there.
        /// </summary>
        /// <remarks>
        /// Preview-only: it does not touch the animation's own Loops, so judging a one-shot
        /// animation on repeat never changes what ships.
        /// </remarks>
        public bool Loop { get; set; }

        /// <summary>
        /// Preview-only playback rate, applied on top of the animation's own PlaybackSpeed so a
        /// fast animation can be watched slowly without editing it.
        /// </summary>
        public float SpeedMultiplier
        {
            get => speedMultiplier;
            set => speedMultiplier = Mathf.Clamp(value, 0.05f, 8f);
        }

        float speedMultiplier = 1f;

        /// <summary>Length of one frame-step, in seconds.</summary>
        public const float FrameStepSeconds = 1f / 60f;

        /// <summary>
        /// Scrubbable length of the preview, in seconds.
        /// </summary>
        /// <remarks>
        /// This is the driver's <i>total</i> duration -- one loop multiplied by the animation's
        /// loop count -- not a single loop. Scrubbing across the whole thing is what lets
        /// LitMotion's own loop handling play out, so <see cref="LoopType.Yoyo"/> actually
        /// reverses on the second pass instead of snapping back to the start.
        ///
        /// An infinitely looping animation has no finite total, so it falls back to a single
        /// loop and <see cref="OnEditorUpdate"/> wraps the playhead instead.
        /// </remarks>
        public float Duration
        {
            get
            {
                if (driver.IsActive())
                {
                    var total = driver.TotalDuration;
                    if (!double.IsInfinity(total) && total > 0d) return (float)total;
                }

                var single = animation?.Duration ?? 0f;
                return float.IsPositiveInfinity(single) ? 0f : single;
            }
        }

        /// <summary>Length of a single loop, used when wrapping an infinite preview.</summary>
        float SingleLoopDuration
        {
            get
            {
                var single = animation?.Duration ?? 0f;
                return float.IsPositiveInfinity(single) ? 0f : single;
            }
        }

        /// <summary>
        /// Playhead position in seconds. Setting it scrubs the animation immediately.
        /// </summary>
        public float Time
        {
            get => time;
            set => Scrub(value);
        }

        /// <summary>Playhead position as a 0-1 fraction of <see cref="Duration"/>.</summary>
        public float NormalizedTime
        {
            get
            {
                var duration = Duration;
                return duration > 0f ? Mathf.Clamp01(time / duration) : 0f;
            }
        }

        /// <summary>
        /// Tears down any existing preview and builds a new one for <paramref name="target"/>.
        /// </summary>
        /// <param name="target">Animation to preview.</param>
        /// <param name="owner">GameObject used for steps with no explicit target.</param>
        /// <returns>True when at least one motion was built.</returns>
        public bool Begin(TweenAnimation target, GameObject owner)
        {
            Stop();

            if (target == null || owner == null) return false;
            if (target.EnabledStepCount == 0) return false;

            animation = target;
            fallback = owner;

            // Captured before building: building reads the live values that FromCurrent steps
            // start from, and these are the values teardown must put back.
            dirtyGuard.Capture();
            snapshot.CaptureAnimation(target, owner);

            buildErrors.Clear();
            driver = TweenAnimationRunner.Build(target, owner, dispatcher.Scheduler, buildErrors);

            if (!driver.IsActive())
            {
                // Nothing got built, so there is nothing to restore or un-dirty.
                snapshot.Clear();
                dirtyGuard.Clear();
                animation = null;
                fallback = null;
                return false;
            }

            // Keeps the handle alive when the playhead reaches the end, so scrubbing back out
            // of the end of the animation still works.
            driver.Preserve();

            time = 0f;
            Scrub(0f);
            Hook();
            return true;
        }

        /// <summary>Starts advancing the playhead in real time.</summary>
        public void Play()
        {
            if (!IsActive) return;

            // Restart from the beginning when the playhead is already parked at the end.
            if (Duration > 0f && time >= Duration) time = 0f;

            playing = true;
            lastEditorTime = EditorApplication.timeSinceStartup;
            Hook();
        }

        /// <summary>Stops advancing but keeps the preview scrubbable.</summary>
        public void Pause() => playing = false;

        /// <summary>Moves the playhead to <paramref name="seconds"/> and applies that pose.</summary>
        public void Scrub(float seconds)
        {
            if (!IsActive) return;

            var duration = Duration;
            time = duration > 0f ? Mathf.Clamp(seconds, 0f, duration) : 0f;

            // The driver handle's Time setter re-evaluates the whole sequence and writes every
            // bound value immediately, which is exactly the scrub primitive we need.
            driver.Time = time;
            Changed?.Invoke();
        }

        /// <summary>Moves the playhead to a 0-1 fraction of the duration.</summary>
        public void ScrubNormalized(float normalized) => Scrub(Mathf.Clamp01(normalized) * Duration);

        /// <summary>
        /// Pauses and moves one frame forward or back, for inspecting a pose frame by frame.
        /// </summary>
        /// <param name="frames">Frames to move; negative steps backward.</param>
        public void StepFrames(int frames)
        {
            if (!IsActive) return;

            playing = false;
            Scrub(time + frames * FrameStepSeconds);
        }

        /// <summary>
        /// Cancels the preview and restores every value it wrote.
        /// </summary>
        public void Stop()
        {
            playing = false;
            Unhook();

            if (driver.IsActive()) driver.Cancel();
            driver = MotionHandle.None;

            // Restore after cancelling, so a final motion update cannot overwrite the restored
            // values on the way out.
            snapshot.RestoreAndClear();
            dirtyGuard.Restore();

            animation = null;
            fallback = null;
            time = 0f;
            buildErrors.Clear();
        }

        /// <summary>
        /// Rebuilds the preview in place, preserving the playhead position.
        /// </summary>
        /// <remarks>
        /// Called when a step is edited while previewing, so the change is visible without the
        /// playhead jumping back to zero.
        /// </remarks>
        public void Rebuild()
        {
            if (animation == null || fallback == null) return;

            var resumeAt = time;
            var wasPlaying = playing;
            var target = animation;
            var owner = fallback;

            Stop();

            if (!Begin(target, owner)) return;

            Scrub(resumeAt);
            if (wasPlaying) Play();
        }

        void Hook()
        {
            if (hooked) return;
            hooked = true;

            EditorApplication.update += OnEditorUpdate;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.quitting += OnQuitting;
        }

        void Unhook()
        {
            if (!hooked) return;
            hooked = false;

            EditorApplication.update -= OnEditorUpdate;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.quitting -= OnQuitting;
        }

        void OnEditorUpdate()
        {
            if (!IsActive)
            {
                // The handle died underneath us, most likely because the target was destroyed.
                Stop();
                return;
            }

            // The target object being deleted mid-preview would otherwise leave us writing to
            // a dead reference every frame.
            if (fallback == null)
            {
                Stop();
                return;
            }

            if (!playing) return;

            var now = EditorApplication.timeSinceStartup;
            var delta = now - lastEditorTime;
            lastEditorTime = now;

            if (delta <= 0d) return;

            var duration = Duration;
            if (duration <= 0f)
            {
                playing = false;
                return;
            }

            // PlaybackSpeed is applied here rather than relying on the driver's own
            // PlaybackSpeed: the private dispatcher is never advanced, so the driver only ever
            // moves through Scrub's absolute Time assignment. There is no double-application.
            var speed = Mathf.Max(0.01f, animation.PlaybackSpeed) * speedMultiplier;
            var next = Advance(time, (float)delta * speed, duration, SingleLoopDuration,
                animation.Loops < 0, Loop, out var keepPlaying);

            playing = keepPlaying;
            Scrub(next);
        }

        /// <summary>
        /// Where the playhead lands after <paramref name="step"/> seconds of playback.
        /// </summary>
        /// <param name="time">Current playhead.</param>
        /// <param name="step">Seconds to advance, already scaled by every speed.</param>
        /// <param name="duration">Scrubbable length: one loop when infinite, all loops otherwise.</param>
        /// <param name="singleLoop">Length of one loop of the animation.</param>
        /// <param name="infinite">The animation itself loops forever.</param>
        /// <param name="loopPreview">The preview's own loop toggle is on.</param>
        /// <param name="keepPlaying">False when playback should stop at the returned time.</param>
        /// <remarks>
        /// Static and free of editor state so the wrapping rules are testable: an infinite
        /// animation wraps by its single loop, a looping preview wraps by the whole duration,
        /// and anything else parks on the last frame and stops.
        /// </remarks>
        internal static float Advance(float time, float step, float duration, float singleLoop,
            bool infinite, bool loopPreview, out bool keepPlaying)
        {
            keepPlaying = true;
            var next = time + Mathf.Max(0f, step);

            if (next < duration) return next;

            if (infinite)
            {
                // Infinite: Duration is one loop, so wrap by hand to keep previewing.
                return singleLoop > 0f ? next % singleLoop : 0f;
            }

            if (loopPreview && duration > 0f)
            {
                return next % duration;
            }

            // Finite: Duration already spans every loop, so reaching it means done.
            keepPlaying = false;
            return duration;
        }

        void OnBeforeAssemblyReload() => Stop();

        void OnQuitting() => Stop();

        void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            // Entering play mode reloads the scene, so the preview must be undone first or the
            // animated pose would be what gets serialized into the play-mode scene.
            if (change is PlayModeStateChange.ExitingEditMode or PlayModeStateChange.ExitingPlayMode)
            {
                Stop();
            }
        }

        /// <summary>Stops the preview and releases its hooks.</summary>
        public void Dispose() => Stop();
    }
}
