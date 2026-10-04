using System;
using System.Collections.Generic;
using UnityEngine;

namespace LitMotion.TweenEditor.Editor
{
    /// <summary>
    /// The timeline's current scale and snapping behaviour, handed to clips so they can convert
    /// between pixels and seconds without holding a reference back to the view.
    /// </summary>
    internal sealed class TweenTimelineContext
    {
        public static readonly TweenTimelineContext Default = new();

        public float PixelsPerSecond = TweenTimelineStyles.DefaultPixelsPerSecond;
        public bool SnapEnabled = true;

        /// <summary>
        /// Supplies the GameObject steps without a target resolve against, so each clip can say
        /// whether it binds. Null when the host has nothing to offer.
        /// </summary>
        public Func<GameObject> TargetProvider;

        /// <summary>The current fallback GameObject, or null.</summary>
        public GameObject Fallback => TargetProvider?.Invoke();

        /// <summary>Playhead position in seconds, so clip edges can snap to it.</summary>
        public float PlayheadTime;

        /// <summary>
        /// Candidate times collected from other clips' edges. Rebuilt by the view whenever the
        /// step list changes.
        /// </summary>
        public readonly List<float> SnapTargets = new();

        /// <summary>Index of the step currently being dragged, excluded from edge snapping.</summary>
        public int DraggingStepIndex = -1;

        /// <summary>
        /// Indices of the selected steps. The last entry is the primary one, which is the clip
        /// a drag follows and the one the inspector shows.
        /// </summary>
        public readonly List<int> Selection = new();

        /// <summary>
        /// Where the last <see cref="Snap"/> call actually latched on, or null when it fell back
        /// to the grid.
        /// </summary>
        /// <remarks>
        /// Drives the guide line. Only meaningful snaps are reported: the fixed grid catches
        /// almost every drag, so drawing a guide for it would mean a line permanently on screen
        /// saying nothing.
        /// </remarks>
        public float? LastSnappedTime;

        /// <summary>The primary selected step, or -1.</summary>
        public int PrimaryIndex => Selection.Count == 0 ? -1 : Selection[Selection.Count - 1];

        /// <summary>True when this step is part of the selection.</summary>
        public bool IsSelected(int index) => Selection.Contains(index);

        /// <summary>Replaces the selection with a single step.</summary>
        public void SelectOnly(int index)
        {
            Selection.Clear();
            if (index >= 0) Selection.Add(index);
        }

        /// <summary>
        /// Adds a step to the selection, or removes it if already there, keeping it primary.
        /// </summary>
        public void ToggleSelection(int index)
        {
            if (index < 0) return;

            if (!Selection.Remove(index)) Selection.Add(index);
        }

        /// <summary>
        /// Makes an already-selected step the primary one without disturbing the selection.
        /// </summary>
        /// <remarks>
        /// Clicking one clip of a multi-selection has to keep the group -- otherwise it could
        /// never be dragged as one -- but the clip that was clicked is the one the inspector
        /// should now be showing.
        /// </remarks>
        public void MakePrimary(int index)
        {
            if (index < 0 || !Selection.Remove(index)) return;

            Selection.Add(index);
        }

        /// <summary>Drops any selected index that no longer exists.</summary>
        public void PruneSelection(int stepCount)
        {
            for (var i = Selection.Count - 1; i >= 0; i--)
            {
                if (Selection[i] < 0 || Selection[i] >= stepCount) Selection.RemoveAt(i);
            }
        }

        /// <summary>
        /// Snaps a time to the grid, to a neighbouring clip edge or to the playhead.
        /// </summary>
        /// <remarks>
        /// Neighbour and playhead snapping are measured in pixels, not seconds, so the magnet
        /// feels the same at every zoom level. They take priority over the fixed grid because
        /// lining up with another clip is almost always the intent.
        /// </remarks>
        public float Snap(float seconds, int excludeStepIndex = -1)
        {
            seconds = Mathf.Max(0f, seconds);
            LastSnappedTime = null;

            if (!SnapEnabled) return seconds;

            var pixelsPerSecond = Mathf.Max(1f, PixelsPerSecond);
            var thresholdSeconds = TweenTimelineStyles.SnapPixelThreshold / pixelsPerSecond;

            var best = float.MaxValue;
            var bestTarget = seconds;

            for (var i = 0; i < SnapTargets.Count; i++)
            {
                var distance = Mathf.Abs(SnapTargets[i] - seconds);
                if (distance < best && distance <= thresholdSeconds)
                {
                    best = distance;
                    bestTarget = SnapTargets[i];
                }
            }

            var playheadDistance = Mathf.Abs(PlayheadTime - seconds);
            if (playheadDistance < best && playheadDistance <= thresholdSeconds)
            {
                best = playheadDistance;
                bestTarget = PlayheadTime;
            }

            if (best < float.MaxValue)
            {
                var snapped = Mathf.Max(0f, bestTarget);
                LastSnappedTime = snapped;
                return snapped;
            }

            // Fall back to the fixed grid.
            return Mathf.Max(0f, Mathf.Round(seconds / TweenTimelineStyles.SnapInterval)
                                 * TweenTimelineStyles.SnapInterval);
        }

        /// <summary>Rebuilds the edge snap targets from every step except the one being dragged.</summary>
        public void RebuildSnapTargets(TweenAnimation animation, int excludeStepIndex)
        {
            SnapTargets.Clear();
            SnapTargets.Add(0f);

            if (animation?.Steps == null) return;

            for (var i = 0; i < animation.Steps.Count; i++)
            {
                if (i == excludeStepIndex) continue;

                var step = animation.Steps[i];
                if (step == null || !step.Enabled) continue;

                SnapTargets.Add(step.StartTime);

                var end = step.StartTime + Mathf.Max(0f, step.Duration);
                if (!float.IsInfinity(end)) SnapTargets.Add(end);
            }
        }
    }
}
