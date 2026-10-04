using System.Collections.Generic;
using UnityEngine;

namespace LitMotion.TweenEditor.Editor
{
    /// <summary>One thing worth telling the author about a step.</summary>
    internal sealed class TweenStepIssue
    {
        public TweenStepIssue(int stepIndex, string message, bool blocksBinding, IReadOnlyList<TweenStepFix> fixes)
        {
            StepIndex = stepIndex;
            Message = message;
            BlocksBinding = blocksBinding;
            Fixes = fixes ?? System.Array.Empty<TweenStepFix>();
        }

        /// <summary>Index of the step within its animation.</summary>
        public int StepIndex { get; }

        /// <summary>What is wrong, in a sentence that names the step.</summary>
        public string Message { get; }

        /// <summary>True when the step will build nothing; false for a caveat that still plays.</summary>
        public bool BlocksBinding { get; }

        /// <summary>One-click repairs, possibly none.</summary>
        public IReadOnlyList<TweenStepFix> Fixes { get; }
    }

    /// <summary>
    /// Whether every step in an animation will find something to animate, computed without
    /// building or playing anything.
    /// </summary>
    internal sealed class TweenBindingReport
    {
        readonly List<TweenStepIssue> issues = new();

        /// <summary>Enabled steps that write to something.</summary>
        public int Binding { get; internal set; }

        /// <summary>Binding steps that resolved.</summary>
        public int Ok { get; internal set; }

        /// <summary>Binding steps that will build nothing.</summary>
        public int Failing { get; internal set; }

        /// <summary>False when there was no object to resolve against, so nothing was checked.</summary>
        public bool HasTarget { get; internal set; }

        public IReadOnlyList<TweenStepIssue> Issues => issues;

        internal void Add(TweenStepIssue issue) => issues.Add(issue);

        /// <summary>The issue for one step, or null.</summary>
        public TweenStepIssue For(int stepIndex)
        {
            for (var i = 0; i < issues.Count; i++)
            {
                if (issues[i].StepIndex == stepIndex && issues[i].BlocksBinding) return issues[i];
            }

            return null;
        }

        /// <summary>The preview bar's one-line readout, e.g. "7 OK · 1 failing".</summary>
        public string Summary
        {
            get
            {
                if (!HasTarget) return "No target";
                if (Binding == 0) return "Nothing to bind";
                if (Failing == 0) return Ok == 1 ? "1 OK" : "All " + Ok + " OK";
                return Ok + " OK · " + Failing + " failing";
            }
        }
    }

    /// <summary>
    /// Checks steps against a target the way the builder will, but without building motions.
    /// </summary>
    /// <remarks>
    /// The preview already reports build errors, but only once a preview is running and only as
    /// a wall of text. Checking up front is what lets the timeline badge a broken clip and the
    /// preview bar say "7 OK · 1 failing" before anyone presses play.
    ///
    /// The checks mirror <see cref="TweenStepBuilder"/>: resolution, then a channel for what
    /// resolved, then the material property for MaterialProperty steps. Anything the builder
    /// can still refuse beyond those -- a character index past the end of the text, say -- is
    /// left to the build errors, which stay authoritative.
    /// </remarks>
    internal static class TweenBindingStatus
    {
        /// <summary>
        /// True when the step will find something to write to. Steps that write to nothing
        /// (intervals, callbacks, custom) and disabled steps always pass.
        /// </summary>
        public static bool Check(TweenStep step, GameObject fallback, out string message)
        {
            message = null;
            if (step == null || !step.Enabled) return true;
            if (TweenBindingResolver.IsNonBinding(step.Type)) return true;

            var resolved = TweenBindingResolver.Resolve(step, fallback, out var error);
            if (resolved == null)
            {
                message = error ?? step.DisplayName + ": nothing to animate.";
                return false;
            }

            if (!TweenChannelInfo.Describe(step, resolved).IsResolved)
            {
                message = step.DisplayName + ": " + step.Type + " cannot animate a " + resolved.GetType().Name
                          + ". " + TweenBindingResolver.DescribeRequirement(step.Type);
                return false;
            }

            if (step.Type == TweenType.MaterialProperty) return CheckMaterial(step, fallback, resolved, out message);

            return true;
        }

        static bool CheckMaterial(TweenStep step, GameObject fallback, Object resolved, out string message)
        {
            message = null;

            var material = TweenChannelInfo.ResolveMaterial(step, fallback);
            if (material == null)
            {
                message = step.DisplayName + ": " + resolved.name + " has no material of its own to animate.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(step.PropertyName))
            {
                message = step.DisplayName + ": choose the shader property to animate.";
                return false;
            }

            if (!material.HasProperty(Shader.PropertyToID(step.PropertyName)))
            {
                message = step.DisplayName + ": " + material.name + " has no property called '" + step.PropertyName + "'.";
                return false;
            }

            return true;
        }

        /// <summary>Checks every enabled step of an animation.</summary>
        public static TweenBindingReport Evaluate(TweenAnimation animation, GameObject fallback)
        {
            var report = new TweenBindingReport { HasTarget = fallback != null };
            if (animation?.Steps == null) return report;

            for (var i = 0; i < animation.Steps.Count; i++)
            {
                var step = animation.Steps[i];
                if (step == null || !step.Enabled) continue;

                if (step.Type == TweenType.Custom)
                {
                    // Not a failure -- it plays -- but a preview cannot undo what it wrote, and an
                    // author finds that out the hard way unless it is said here.
                    report.Add(new TweenStepIssue(i, CustomRestoreNote(step), false, null));
                    continue;
                }

                if (TweenBindingResolver.IsNonBinding(step.Type)) continue;

                // A step with its own target does not need the fallback, so check it even when
                // there is no preview object.
                if (fallback == null && step.Target == null) continue;

                report.Binding++;

                if (Check(step, fallback, out var message))
                {
                    report.Ok++;
                    continue;
                }

                report.Failing++;
                report.Add(new TweenStepIssue(i, message, true, TweenStepFixes.For(step, fallback)));
            }

            return report;
        }

        /// <summary>The caveat shown for a Custom step.</summary>
        public static string CustomRestoreNote(TweenStep step)
        {
            return step.DisplayName + ": Custom steps write through an event, so stopping a preview "
                   + "cannot put back what they changed.";
        }
    }
}
