using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LitMotion.TweenEditor.Editor
{
    /// <summary>A step's position and length on the timeline.</summary>
    internal readonly struct TweenClipTiming
    {
        public TweenClipTiming(int index, float startTime, float duration)
        {
            Index = index;
            StartTime = startTime;
            Duration = duration;
        }

        /// <summary>Index of the step in the animation's Steps list.</summary>
        public int Index { get; }

        public float StartTime { get; }
        public float Duration { get; }
    }

    /// <summary>
    /// The timeline's editing commands, separated into what to do and the doing of it.
    /// </summary>
    /// <remarks>
    /// Each command computes a plan -- a list of new timings -- before anything is written. Two
    /// reasons. The arithmetic becomes testable without a window, a pointer or a
    /// <c>SerializedProperty</c>; and a group operation can be validated as a whole before any
    /// of it lands, which is what lets a nudge clamp the whole selection together instead of
    /// flattening the clips that hit zero first.
    ///
    /// Applying a plan is always one undo entry, because one gesture should be one Ctrl+Z.
    /// </remarks>
    internal static class TweenTimelineCommands
    {
        /// <summary>
        /// Shortest clip a split is allowed to produce. Below this a clip is unclickable and a
        /// zero-length one would silently stop animating.
        /// </summary>
        public const float MinDuration = 0.02f;

        // --- Queries ---

        /// <summary>True when a step can be cut in two at this time.</summary>
        public static bool CanSplit(TweenStep step, float time)
        {
            if (step == null || step.Type == TweenType.Callback) return false;

            var duration = step.Duration;
            if (duration < MinDuration * 2f) return false;

            return time >= step.StartTime + MinDuration
                   && time <= step.StartTime + duration - MinDuration;
        }

        /// <summary>
        /// Works out the two halves a split produces: the original keeps the head, the copy
        /// takes the tail.
        /// </summary>
        public static bool TrySplit(TweenStep step, float time, out float firstDuration,
            out float secondStart, out float secondDuration)
        {
            firstDuration = 0f;
            secondStart = 0f;
            secondDuration = 0f;

            if (!CanSplit(step, time)) return false;

            firstDuration = time - step.StartTime;
            secondStart = time;
            secondDuration = step.StartTime + step.Duration - time;

            return true;
        }

        // --- Plans ---

        /// <summary>
        /// Moves every selected clip by the same amount, clamped so the whole group stops at
        /// zero together rather than piling up against it.
        /// </summary>
        public static List<TweenClipTiming> Nudge(TweenAnimation animation, IReadOnlyList<int> selection,
            float deltaSeconds)
        {
            var plan = new List<TweenClipTiming>();
            if (animation?.Steps == null || selection == null) return plan;

            // The clip closest to zero decides how far left the group can go. Without this a
            // nudge would quietly collapse the relative timing the author arranged.
            var limit = float.MaxValue;
            for (var i = 0; i < selection.Count; i++)
            {
                var step = At(animation, selection[i]);
                if (step == null) continue;

                limit = Mathf.Min(limit, step.StartTime);
            }

            if (limit == float.MaxValue) return plan;

            var delta = Mathf.Max(deltaSeconds, -limit);

            for (var i = 0; i < selection.Count; i++)
            {
                var step = At(animation, selection[i]);
                if (step == null) continue;

                plan.Add(new TweenClipTiming(selection[i], step.StartTime + delta, step.Duration));
            }

            return plan;
        }

        /// <summary>Starts every selected clip at the same time.</summary>
        public static List<TweenClipTiming> AlignStarts(TweenAnimation animation,
            IReadOnlyList<int> selection, float? time = null)
        {
            var plan = new List<TweenClipTiming>();
            if (animation?.Steps == null || selection == null) return plan;

            var target = time ?? Earliest(animation, selection);

            for (var i = 0; i < selection.Count; i++)
            {
                var step = At(animation, selection[i]);
                if (step == null) continue;

                plan.Add(new TweenClipTiming(selection[i], Mathf.Max(0f, target), step.Duration));
            }

            return plan;
        }

        /// <summary>
        /// Ends every selected clip at the same time, moving them rather than resizing them.
        /// </summary>
        public static List<TweenClipTiming> AlignEnds(TweenAnimation animation,
            IReadOnlyList<int> selection, float? time = null)
        {
            var plan = new List<TweenClipTiming>();
            if (animation?.Steps == null || selection == null) return plan;

            var target = time ?? Latest(animation, selection);

            for (var i = 0; i < selection.Count; i++)
            {
                var step = At(animation, selection[i]);
                if (step == null) continue;

                plan.Add(new TweenClipTiming(selection[i],
                    Mathf.Max(0f, target - step.Duration), step.Duration));
            }

            return plan;
        }

        /// <summary>
        /// Spaces the selected clips evenly between the first and last of them, which stay put.
        /// </summary>
        /// <remarks>
        /// Needs at least three clips: with two there is nothing between them to space.
        /// </remarks>
        public static List<TweenClipTiming> Distribute(TweenAnimation animation,
            IReadOnlyList<int> selection)
        {
            var plan = new List<TweenClipTiming>();
            if (animation?.Steps == null || selection == null || selection.Count < 3) return plan;

            var ordered = new List<int>(selection);
            ordered.Sort((a, b) =>
            {
                var left = At(animation, a);
                var right = At(animation, b);
                var leftStart = left?.StartTime ?? 0f;
                var rightStart = right?.StartTime ?? 0f;
                return leftStart.CompareTo(rightStart);
            });

            var first = At(animation, ordered[0]);
            var last = At(animation, ordered[ordered.Count - 1]);
            if (first == null || last == null) return plan;

            var span = last.StartTime - first.StartTime;
            var gap = span / (ordered.Count - 1);

            for (var i = 0; i < ordered.Count; i++)
            {
                var step = At(animation, ordered[i]);
                if (step == null) continue;

                plan.Add(new TweenClipTiming(ordered[i], first.StartTime + gap * i, step.Duration));
            }

            return plan;
        }

        /// <summary>Moves one clip so it starts at a given time, keeping its length.</summary>
        public static List<TweenClipTiming> SetStart(TweenAnimation animation, int index, float time)
        {
            var plan = new List<TweenClipTiming>();
            var step = At(animation, index);
            if (step == null) return plan;

            plan.Add(new TweenClipTiming(index, Mathf.Max(0f, time), step.Duration));
            return plan;
        }

        /// <summary>
        /// Trims a clip's head to a time, holding its tail still -- the same rule the drag
        /// handles follow.
        /// </summary>
        public static List<TweenClipTiming> TrimStartTo(TweenAnimation animation, int index, float time)
        {
            var plan = new List<TweenClipTiming>();
            var step = At(animation, index);
            if (step == null || step.Type == TweenType.Callback) return plan;

            var end = step.StartTime + step.Duration;
            var start = Mathf.Clamp(time, 0f, end);

            plan.Add(new TweenClipTiming(index, start, end - start));
            return plan;
        }

        /// <summary>Trims a clip's tail to a time, holding its head still.</summary>
        public static List<TweenClipTiming> TrimEndTo(TweenAnimation animation, int index, float time)
        {
            var plan = new List<TweenClipTiming>();
            var step = At(animation, index);
            if (step == null || step.Type == TweenType.Callback) return plan;

            plan.Add(new TweenClipTiming(index, step.StartTime,
                Mathf.Max(0f, time - step.StartTime)));
            return plan;
        }

        // --- Applying ---

        /// <summary>
        /// Writes a plan through serialized properties, as one undo entry.
        /// </summary>
        /// <returns>True when anything was written.</returns>
        public static bool Apply(SerializedProperty stepsProperty, IReadOnlyList<TweenClipTiming> plan,
            string undoName)
        {
            if (stepsProperty == null || plan == null || plan.Count == 0) return false;

            var serialized = stepsProperty.serializedObject;
            Undo.RegisterCompleteObjectUndo(serialized.targetObject, undoName);

            for (var i = 0; i < plan.Count; i++)
            {
                var timing = plan[i];
                if (timing.Index < 0 || timing.Index >= stepsProperty.arraySize) continue;

                var element = stepsProperty.GetArrayElementAtIndex(timing.Index);
                var start = element.FindPropertyRelative("StartTime");
                var duration = element.FindPropertyRelative("Duration");

                if (start != null) start.floatValue = Mathf.Max(0f, timing.StartTime);
                if (duration != null) duration.floatValue = Mathf.Max(0f, timing.Duration);
            }

            serialized.ApplyModifiedProperties();
            return true;
        }

        // --- Zoom ---

        /// <summary>
        /// The scale that fits an animation into the available width, with a little air at the end.
        /// </summary>
        public static float FitPixelsPerSecond(float durationSeconds, float viewportWidth)
        {
            if (durationSeconds <= 0f || float.IsInfinity(durationSeconds) || viewportWidth <= 1f)
            {
                return TweenTimelineStyles.DefaultPixelsPerSecond;
            }

            var scale = (viewportWidth - 24f) / durationSeconds;

            return Mathf.Clamp(scale,
                TweenTimelineStyles.MinPixelsPerSecond,
                TweenTimelineStyles.MaxPixelsPerSecond);
        }

        /// <summary>
        /// The scroll offset that keeps the time under the cursor under the cursor after a zoom.
        /// </summary>
        /// <remarks>
        /// Without this, zooming walks the content sideways and the clip being worked on slides
        /// out from under the pointer.
        /// </remarks>
        public static float AnchoredScroll(float timeUnderCursor, float cursorOffsetInViewport,
            float newPixelsPerSecond)
        {
            return Mathf.Max(0f, timeUnderCursor * newPixelsPerSecond - cursorOffsetInViewport);
        }

        /// <summary>Applies one wheel notch to a zoom level, clamped to the timeline's range.</summary>
        public static float ZoomStep(float pixelsPerSecond, float wheelDelta)
        {
            var factor = wheelDelta > 0f ? 0.9f : 1f / 0.9f;

            return Mathf.Clamp(pixelsPerSecond * factor,
                TweenTimelineStyles.MinPixelsPerSecond,
                TweenTimelineStyles.MaxPixelsPerSecond);
        }

        // --- Helpers ---

        static TweenStep At(TweenAnimation animation, int index)
        {
            if (animation?.Steps == null || index < 0 || index >= animation.Steps.Count) return null;
            return animation.Steps[index];
        }

        static float Earliest(TweenAnimation animation, IReadOnlyList<int> selection)
        {
            var earliest = float.MaxValue;
            for (var i = 0; i < selection.Count; i++)
            {
                var step = At(animation, selection[i]);
                if (step != null) earliest = Mathf.Min(earliest, step.StartTime);
            }

            return earliest == float.MaxValue ? 0f : earliest;
        }

        /// <summary>
        /// Copies the selected steps into a new animation, shifted so the earliest starts at
        /// zero, in timeline order.
        /// </summary>
        /// <remarks>
        /// The basis of "Save Selection as Preset". Shifting matters: a pop that happens to sit
        /// at 1.2s in this animation should play immediately when reused somewhere else. The
        /// copies are independent of the source, and carry the source's id with a suffix.
        /// </remarks>
        public static TweenAnimation ExtractSelection(TweenAnimation animation, IReadOnlyList<int> selection)
        {
            var extracted = new TweenAnimation
            {
                Id = (animation?.Id ?? "Animation") + " Selection",
            };
            extracted.Steps.Clear();

            if (animation?.Steps == null || selection == null || selection.Count == 0) return extracted;

            var ordered = new List<int>();
            for (var i = 0; i < selection.Count; i++)
            {
                if (At(animation, selection[i]) != null && !ordered.Contains(selection[i])) ordered.Add(selection[i]);
            }

            ordered.Sort((a, b) =>
            {
                var byTime = animation.Steps[a].StartTime.CompareTo(animation.Steps[b].StartTime);
                return byTime != 0 ? byTime : a.CompareTo(b);
            });

            var origin = Earliest(animation, ordered);

            for (var i = 0; i < ordered.Count; i++)
            {
                var copy = animation.Steps[ordered[i]].Clone();
                copy.StartTime = Mathf.Max(0f, copy.StartTime - origin);
                extracted.Steps.Add(copy);
            }

            return extracted;
        }

        static float Latest(TweenAnimation animation, IReadOnlyList<int> selection)
        {
            var latest = 0f;
            for (var i = 0; i < selection.Count; i++)
            {
                var step = At(animation, selection[i]);
                if (step != null) latest = Mathf.Max(latest, step.StartTime + step.Duration);
            }

            return latest;
        }
    }
}
