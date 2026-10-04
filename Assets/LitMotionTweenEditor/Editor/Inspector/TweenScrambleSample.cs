using Unity.Collections;
using UnityEngine;

namespace LitMotion.TweenEditor.Editor
{
    /// <summary>
    /// What a TextScramble step looks like part-way through, computed by LitMotion itself.
    /// </summary>
    /// <remarks>
    /// "Uppercase" and "Numerals" are words; the filler they produce is what an author is
    /// actually choosing between. Rather than imitating it, this runs the real string motion
    /// on a private dispatcher that nothing else advances, scrubs it, and reads the result --
    /// the same production-path rule the preview follows, so the sample cannot disagree with
    /// what plays.
    /// </remarks>
    internal static class TweenScrambleSample
    {
        /// <summary>The scrambled text at <paramref name="progress"/>, or an explanation.</summary>
        /// <param name="step">A TextScramble step.</param>
        /// <param name="sourceText">Text the scramble grows out of, when it reads the live text.</param>
        /// <param name="progress">0-1 point to sample, before easing.</param>
        public static string At(TweenStep step, string sourceText, float progress = 0.5f)
        {
            if (step == null) return string.Empty;

            var from = default(FixedString512Bytes);
            var to = default(FixedString512Bytes);

            if (!string.IsNullOrEmpty(sourceText) && from.CopyFrom(sourceText) != CopyError.None) return "(text too long)";
            if (!string.IsNullOrEmpty(step.TargetText) && to.CopyFrom(step.TargetText) != CopyError.None) return "(text too long)";

            var dispatcher = new ManualMotionDispatcher();
            var builder = LMotion.String.Create512Bytes(from, to, 1f)
                .WithRichText(step.RichText)
                .WithScheduler(dispatcher.Scheduler);

            if (step.ScrambleMode == ScrambleMode.Custom)
            {
                if (string.IsNullOrEmpty(step.ScrambleChars)) return "(set the characters to scramble with)";

                var chars = default(FixedString64Bytes);
                if (chars.CopyFrom(step.ScrambleChars) != CopyError.None) return "(too many scramble characters)";

                builder = builder.WithScrambleChars(chars);
            }
            else if (step.ScrambleMode != ScrambleMode.None)
            {
                builder = builder.WithScrambleChars(step.ScrambleMode);
            }

            // A fixed seed even when the step has none, so the sample holds still while the
            // author edits other fields instead of flickering on every redraw.
            builder = builder.WithRandomSeed(step.RandomSeed != 0u ? step.RandomSeed : 1u);

            var sample = string.Empty;
            var handle = builder.Bind(value => sample = value.ToString());

            try
            {
                handle.Time = Mathf.Clamp01(progress);
            }
            finally
            {
                if (handle.IsActive()) handle.Cancel();
            }

            return sample;
        }
    }
}
