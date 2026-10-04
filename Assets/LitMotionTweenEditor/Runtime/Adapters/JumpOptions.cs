using System;

namespace LitMotion.TweenEditor
{
    /// <summary>
    /// Options for a jump motion: a lerp between two points with a parabolic arc added on Y.
    /// </summary>
    [Serializable]
    public struct JumpOptions : IEquatable<JumpOptions>, IMotionOptions
    {
        /// <summary>Number of arcs performed over the motion's duration. Values below 1 are treated as 1.</summary>
        public int JumpCount;

        /// <summary>Peak height of the first arc, in the motion's own units.</summary>
        public float JumpPower;

        /// <summary>
        /// Fraction of the previous arc's height each subsequent arc reaches.
        /// 0.5 halves the height each bounce; 1 keeps every arc the same height.
        /// </summary>
        public float Decay;

        public static JumpOptions Default => new()
        {
            JumpCount = 1,
            JumpPower = 1f,
            Decay = 0.5f,
        };

        public readonly bool Equals(JumpOptions other)
        {
            return other.JumpCount == JumpCount
                && other.JumpPower == JumpPower
                && other.Decay == Decay;
        }

        public override readonly bool Equals(object obj) => obj is JumpOptions other && Equals(other);

        public override readonly int GetHashCode() => HashCode.Combine(JumpCount, JumpPower, Decay);
    }
}
