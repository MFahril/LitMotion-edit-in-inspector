using UnityEngine;

namespace LitMotion.TweenEditor.Editor
{
    /// <summary>
    /// Sensible ranges for the numeric step fields, so they can be sliders instead of raw boxes.
    /// </summary>
    /// <remarks>
    /// A slider does two jobs a number field cannot: it says what the useful range is, and it
    /// lets a value be felt rather than guessed. Shake frequency is the clearest case -- 10 and
    /// 40 are both legal, read identically as text, and look nothing alike.
    ///
    /// These are soft ranges for the UI only. Nothing in the runtime clamps to them, and the
    /// builder keeps its own hard guards (<c>Mathf.Max(1, Frequency)</c> and friends), so a
    /// value set from code or from an older asset still plays.
    /// </remarks>
    internal static class TweenFieldRanges
    {
        /// <summary>Oscillations over a punch or shake.</summary>
        public const int FrequencyMin = 1;
        public const int FrequencyMax = 60;

        /// <summary>How hard the oscillation decays. 1 fully damps by the end.</summary>
        public const float DampingMin = 0f;
        public const float DampingMax = 1f;

        /// <summary>Arcs in a jump.</summary>
        public const int JumpCountMin = 1;
        public const int JumpCountMax = 10;

        /// <summary>Peak height of a jump arc.</summary>
        public const float JumpPowerMin = 0f;

        /// <summary>Top of the jump height range, in world units.</summary>
        public const float JumpPowerMaxWorld = 4f;

        /// <summary>
        /// Top of the jump height range for a target whose distances are scaled by
        /// <paramref name="distanceScale"/>: 4 world units, or 400 pixels on a UI element.
        /// </summary>
        /// <remarks>
        /// One fixed range cannot serve both: 0-400 makes a 3D hop of 0.5 a sliver at the left
        /// end of the track, and 0-4 makes a UI hop impossible to drag to. The range also always
        /// stretches to include <paramref name="current"/>, because the slider clamps, and a
        /// value from before this range existed must not be cut down just by being looked at.
        /// </remarks>
        public static float JumpPowerMax(float distanceScale, float current = 0f)
        {
            return Mathf.Max(JumpPowerMaxWorld * Mathf.Max(1f, distanceScale), current);
        }

        /// <summary>Per-character delay for a TMP wave.</summary>
        public const float StaggerMin = 0f;
        public const float StaggerMax = 0.25f;

        /// <summary>Animation playback rate.</summary>
        public const float PlaybackSpeedMin = 0.1f;
        public const float PlaybackSpeedMax = 4f;

        /// <summary>Clamps a value to a range, for the tests and for pasted data.</summary>
        public static float Clamp(float value, float min, float max)
        {
            return Mathf.Clamp(value, min, max);
        }

        /// <summary>True when a value sits inside its slider's range.</summary>
        public static bool IsInRange(float value, float min, float max)
        {
            return value >= min && value <= max;
        }
    }
}
