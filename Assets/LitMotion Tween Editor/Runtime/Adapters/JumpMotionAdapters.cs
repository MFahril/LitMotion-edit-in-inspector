using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using LitMotion;
using LitMotion.TweenEditor;

// Every concrete generic job combination must be registered so Burst and IL2CPP generate
// code for it ahead of time. This mirrors LitMotion's own Runtime/Adapters/*.cs.
[assembly: RegisterGenericJobType(typeof(MotionUpdateJob<Vector2, JumpOptions, Vector2JumpMotionAdapter>))]
[assembly: RegisterGenericJobType(typeof(MotionUpdateJob<Vector3, JumpOptions, Vector3JumpMotionAdapter>))]

namespace LitMotion.TweenEditor
{
    /// <summary>
    /// Shared arc math for the jump adapters.
    /// </summary>
    internal static class JumpHelper
    {
        /// <summary>
        /// Height of the arc above the straight-line path at eased progress <paramref name="t"/>.
        /// </summary>
        /// <remarks>
        /// Returns exactly 0 at t=0 and t=1, so a jump always lands precisely on its end value
        /// regardless of jump count or decay.
        /// </remarks>
        public static float EvaluateArc(in float t, in int jumpCount, in float jumpPower, in float decay)
        {
            var count = math.max(1, jumpCount);

            // Position within the arc sequence: [0, count].
            var scaled = math.saturate(t) * count;

            // Which arc we are in. Clamped so that t=1 (scaled=count) stays in the last arc
            // rather than indexing one past the end.
            var index = math.min((int)math.floor(scaled), count - 1);

            // Position within the current arc, [0, 1].
            var local = scaled - index;

            // Parabola peaking at 0.5 with value 1, and exactly 0 at both ends.
            var arc = 4f * local * (1f - local);

            // Each successive arc reaches `decay` times the previous one's height.
            var power = jumpPower * math.pow(math.max(0f, decay), index);

            return arc * power;
        }
    }

    /// <summary>Animates a <see cref="Vector2"/> along a straight path with an arc added on Y.</summary>
    public readonly struct Vector2JumpMotionAdapter : IMotionAdapter<Vector2, JumpOptions>
    {
        public Vector2 Evaluate(ref Vector2 startValue, ref Vector2 endValue, ref JumpOptions options,
            in MotionEvaluationContext context)
        {
            var result = Vector2.LerpUnclamped(startValue, endValue, context.Progress);
            result.y += JumpHelper.EvaluateArc(context.Progress, options.JumpCount, options.JumpPower, options.Decay);
            return result;
        }
    }

    /// <summary>Animates a <see cref="Vector3"/> along a straight path with an arc added on Y.</summary>
    public readonly struct Vector3JumpMotionAdapter : IMotionAdapter<Vector3, JumpOptions>
    {
        public Vector3 Evaluate(ref Vector3 startValue, ref Vector3 endValue, ref JumpOptions options,
            in MotionEvaluationContext context)
        {
            var result = Vector3.LerpUnclamped(startValue, endValue, context.Progress);
            result.y += JumpHelper.EvaluateArc(context.Progress, options.JumpCount, options.JumpPower, options.Decay);
            return result;
        }
    }

    /// <summary>
    /// Entry points for creating jump motions, mirroring <see cref="LMotion.Punch"/>.
    /// </summary>
    /// <remarks>
    /// The arc is driven by the same eased progress as the positional lerp, so
    /// <see cref="Ease.Linear"/> produces the most natural-looking arc. The step builder
    /// defaults jump steps to Linear for that reason.
    /// </remarks>
    public static class LJump
    {
        public static MotionBuilder<Vector2, JumpOptions, Vector2JumpMotionAdapter> Create(
            Vector2 from, Vector2 to, float duration)
        {
            return LMotion.Create<Vector2, JumpOptions, Vector2JumpMotionAdapter>(from, to, duration)
                .WithOptions(JumpOptions.Default);
        }

        public static MotionBuilder<Vector3, JumpOptions, Vector3JumpMotionAdapter> Create(
            Vector3 from, Vector3 to, float duration)
        {
            return LMotion.Create<Vector3, JumpOptions, Vector3JumpMotionAdapter>(from, to, duration)
                .WithOptions(JumpOptions.Default);
        }
    }
}
