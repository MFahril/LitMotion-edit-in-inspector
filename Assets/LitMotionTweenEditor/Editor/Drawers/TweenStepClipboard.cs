using System;
using UnityEditor;
using UnityEngine;

namespace LitMotion.TweenEditor.Editor
{
    /// <summary>
    /// Copies steps and animations through the system clipboard as JSON.
    /// </summary>
    /// <remarks>
    /// The system clipboard rather than a static field, so a step can be pasted into a different
    /// scene, a different prefab, or another Unity project entirely -- which is the whole point
    /// of copy/paste for this tool.
    ///
    /// Payloads are tagged with a marker key and a version. A pasted blob is validated before
    /// being applied, so an unrelated clipboard (a copied file path, some JSON from a browser)
    /// is ignored rather than half-applied.
    ///
    /// <see cref="JsonUtility"/> does not serialize <c>UnityEvent</c> listeners or object
    /// references across a clipboard round trip. Target and event wiring therefore do not travel
    /// with a copied step; within one animation, duplicate preserves both because it goes through
    /// Unity's own array duplication instead.
    /// </remarks>
    internal static class TweenStepClipboard
    {
        const string StepMarker = "__LMTE_Step_v1";
        const string AnimationMarker = "__LMTE_Animation_v1";

        [Serializable]
        sealed class StepPayload
        {
            public string marker = StepMarker;
            public TweenStep step;
        }

        [Serializable]
        sealed class AnimationPayload
        {
            public string marker = AnimationMarker;
            public TweenAnimation animation;
        }

        /// <summary>True when the clipboard holds a step this tool wrote.</summary>
        public static bool HasStep => Peek(StepMarker);

        /// <summary>True when the clipboard holds an animation this tool wrote.</summary>
        public static bool HasAnimation => Peek(AnimationMarker);

        /// <summary>Writes a step to the clipboard.</summary>
        public static void CopyStep(TweenStep step)
        {
            if (step == null) return;

            var payload = new StepPayload { step = step };
            EditorGUIUtility.systemCopyBuffer = JsonUtility.ToJson(payload);
        }

        /// <summary>Writes a whole animation to the clipboard.</summary>
        public static void CopyAnimation(TweenAnimation animation)
        {
            if (animation == null) return;

            var payload = new AnimationPayload { animation = animation };
            EditorGUIUtility.systemCopyBuffer = JsonUtility.ToJson(payload);
        }

        /// <summary>
        /// Overwrites <paramref name="target"/> with the clipboard's step.
        /// </summary>
        /// <returns>True when a valid step was applied.</returns>
        public static bool PasteInto(TweenStep target)
        {
            if (target == null) return false;

            var payload = Read<StepPayload>(StepMarker);
            if (payload?.step == null) return false;

            // Overwrite in place rather than swapping the reference: the caller holds this
            // instance inside a serialized List and a replacement would not survive.
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(payload.step), target);

            // A curve arrives as data, not a shared reference, but guard against a null from a
            // hand-edited payload.
            target.CustomCurve ??= AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
            target.OnCallback ??= new UnityEngine.Events.UnityEvent();
            target.OnProgress ??= new TweenProgressEvent();
            return true;
        }

        /// <summary>
        /// Overwrites <paramref name="target"/> with the clipboard's animation.
        /// </summary>
        /// <returns>True when a valid animation was applied.</returns>
        public static bool PasteInto(TweenAnimation target)
        {
            if (target == null) return false;

            var payload = Read<AnimationPayload>(AnimationMarker);
            if (payload?.animation == null) return false;

            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(payload.animation), target);

            target.Steps ??= new System.Collections.Generic.List<TweenStep>();
            target.OnComplete ??= new UnityEngine.Events.UnityEvent();

            for (var i = 0; i < target.Steps.Count; i++)
            {
                var step = target.Steps[i];
                if (step == null) continue;

                step.CustomCurve ??= AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
                step.OnCallback ??= new UnityEngine.Events.UnityEvent();
                step.OnProgress ??= new TweenProgressEvent();
            }

            return true;
        }

        static bool Peek(string marker)
        {
            var text = EditorGUIUtility.systemCopyBuffer;
            return !string.IsNullOrEmpty(text) && text.Contains(marker);
        }

        static T Read<T>(string marker) where T : class
        {
            var text = EditorGUIUtility.systemCopyBuffer;
            if (string.IsNullOrEmpty(text) || !text.Contains(marker)) return null;

            try
            {
                return JsonUtility.FromJson<T>(text);
            }
            catch (Exception)
            {
                // Malformed or foreign JSON: treat the clipboard as empty rather than throwing
                // out of a toolbar button.
                return null;
            }
        }
    }
}
