using System;
using System.Collections.Generic;
using LitMotion.Extensions;
using Unity.Collections;
using UnityEngine;
using Object = UnityEngine.Object;
#if LMTE_SUPPORT_TMP
using TMPro;
#endif
#if LMTE_SUPPORT_UGUI
using UnityEngine.UI;
#endif

namespace LitMotion.TweenEditor
{
    /// <summary>
    /// Turns a serialized <see cref="TweenStep"/> into live LitMotion handles.
    /// </summary>
    /// <remarks>
    /// This is the single place that knows how each <see cref="TweenType"/> maps onto LitMotion.
    /// Both runtime playback and editor preview go through it, which is what makes a preview
    /// faithful: there is no second, editor-only interpretation of a step to drift out of sync.
    /// </remarks>
    public static class TweenStepBuilder
    {
        // Animation-level settings folded into the step's own motion while BuildAlone runs, so
        // that a one-step animation needs no sequence. Read by Configure; main thread only, as
        // with the runner's shared handle buffer.
        static bool alone;
        static int aloneLoops;
        static LoopType aloneLoopType;
        static Action aloneOnComplete;
        static Action aloneOnCancel;

        /// <summary>
        /// Builds a step that stands in for its whole animation: the animation's loops and
        /// completion callback are applied to the step's own motion.
        /// </summary>
        /// <remarks>
        /// Only valid for a step <see cref="TweenAnimationRunner"/> has checked can run alone,
        /// which is what guarantees one motion and no clash with the step's own loops.
        /// </remarks>
        internal static int BuildAlone(TweenStep step, GameObject fallback, IMotionScheduler scheduler,
            List<MotionHandle> results, out string error, int loops, LoopType loopType, Action onComplete,
            Action onCancel)
        {
            alone = true;
            aloneLoops = loops;
            aloneLoopType = loopType;
            aloneOnComplete = onComplete;
            aloneOnCancel = onCancel;

            try
            {
                return Build(step, fallback, scheduler, results, out error);
            }
            finally
            {
                alone = false;
                aloneOnComplete = null;
                aloneOnCancel = null;
            }
        }

        /// <summary>
        /// The loop count a step's motion is actually built with. Infinite step loops are
        /// clamped to one pass, because a sequence cannot schedule a child that never ends.
        /// </summary>
        internal static int EffectiveLoops(TweenStep step)
        {
            return step.Loops < 0 ? 1 : step.Loops;
        }

        /// <summary>
        /// Builds the motions for one step and appends them to <paramref name="results"/>.
        /// </summary>
        /// <param name="step">Step to build.</param>
        /// <param name="fallback">GameObject used when the step has no explicit target.</param>
        /// <param name="scheduler">
        /// Scheduler for the created motions, or null to let LitMotion choose. Null is correct at
        /// runtime; the editor preview passes its own manual scheduler.
        /// </param>
        /// <param name="results">Receives the created handles.</param>
        /// <param name="error">A human-readable reason when nothing could be built.</param>
        /// <returns>How many handles were appended.</returns>
        public static int Build(TweenStep step, GameObject fallback, IMotionScheduler scheduler,
            List<MotionHandle> results, out string error)
        {
            error = null;
            if (step == null || !step.Enabled || results == null) return 0;

            // Timeline primitives bind to nothing but still occupy time.
            if (TweenBindingResolver.IsNonBinding(step.Type))
            {
                return BuildPrimitive(step, scheduler, results);
            }

            var target = TweenBindingResolver.Resolve(step, fallback, out error);
            if (target == null) return 0;

            var key = TweenChannelAccessor.GetChannelKey(step, target);
            if (key == TweenChannelKey.None)
            {
                error = DescribeMissingChannel(step, target);
                return 0;
            }

            // Two types do not fit the shared vector channel path: a scramble interpolates a
            // string, and a per-character step drives TMP's vertex data through LitMotion's own
            // animator. Both are built directly instead.
            if (step.Type == TweenType.TextScramble)
            {
                return BuildScramble(step, target, scheduler, results, out error);
            }

            if (step.Type == TweenType.TMPCharacter)
            {
                return BuildTmpCharacter(step, target, scheduler, results, out error);
            }

            // Built once and handed down: it costs a shader property lookup and a registry
            // lookup, and every stage below needs it.
            var context = TweenChannelContext.For(step);

            if (step.Type == TweenType.MaterialProperty)
            {
                if (!TryValidateMaterial(step, target, context, out var material, out error)) return 0;

                // Resolved once here rather than on every frame's write. The writer, the
                // endpoint read and the snapshot all address the material from now on.
                target = material;
            }

            // An Anchors step set to Both drives two independent properties.
            if (step.Type == TweenType.Anchors && step.AnchorTarget == TweenAnchorTarget.Both)
            {
                var written = BuildChannel(step, target, TweenChannelKey.AnchorMin, context, scheduler, results, out error);
                written += BuildChannel(step, target, TweenChannelKey.AnchorMax, context, scheduler, results, out var maxError);
                error ??= maxError;
                return written;
            }

            return BuildChannel(step, target, key, context, scheduler, results, out error);
        }

        static int BuildChannel(TweenStep step, Object target, TweenChannelKey key, in TweenChannelContext context,
            IMotionScheduler scheduler, List<MotionHandle> results, out string error)
        {
            error = null;

            var axis = TweenChannelAccessor.GetEffectiveAxis(step, key, context);
            var kind = TweenChannelAccessor.GetValueKind(key, context);
            var isColor = TweenChannelAccessor.IsColorChannel(key, context);

            if (!ResolveEndpoints(step, target, key, context, isColor, out var from, out var to, out error))
            {
                return 0;
            }

            var writer = new TweenChannelWriter
            {
                Key = key,
                Target = target,
                Axis = axis,
                Context = context,
            };

            if (key == TweenChannelKey.TextNumber)
            {
                // The format is checked here, once, rather than failing on every frame's write.
                writer.Counter = TweenCounterText.Create(context.Format);
                if (writer.Counter == null)
                {
                    error = step.DisplayName + ": '" + context.Format + "' is not a valid format string.";
                    return 0;
                }
            }

            switch (step.Type)
            {
                case TweenType.Punch:
                    return BuildVibration(step, kind, from, to, writer, scheduler, results, true, out error);

                case TweenType.Shake:
                    return BuildVibration(step, kind, from, to, writer, scheduler, results, false, out error);

                case TweenType.Jump:
                    return BuildJump(step, kind, from, to, writer, scheduler, results, out error);

                default:
                    return BuildInterpolation(step, kind, from, to, writer, scheduler, results);
            }
        }

        /// <summary>
        /// Works out the start and end values, honoring FromCurrent and Relative.
        /// </summary>
        static bool ResolveEndpoints(TweenStep step, Object target, TweenChannelKey key,
            in TweenChannelContext context, bool isColor, out Vector4 from, out Vector4 to, out string error)
        {
            error = null;
            from = isColor ? (Vector4)step.FromColor : step.From;
            to = isColor ? (Vector4)step.ToColor : step.To;

            // A step that was another type a moment ago can arrive with FromCurrent still set
            // on a channel that cannot be read. Ignoring it beats refusing to build.
            var readable = TweenChannelAccessor.SupportsRead(key);

            // FromOffset needs the live value as its destination, so it reads it whether or not
            // FromCurrent is also set.
            if (readable && (step.FromCurrent || step.FromOffset))
            {
                if (!TweenChannelAccessor.TryRead(key, target, context, out from))
                {
                    error = step.DisplayName + ": could not read the current value of " + key + ".";
                    return false;
                }
            }

            if (step.FromOffset && readable)
            {
                // Land on wherever the object already is, starting from an offset of that.
                to = from;

                var offset = isColor ? (Vector4)step.FromColor : step.From;
                if (step.UniformScale && key == TweenChannelKey.LocalScale)
                {
                    offset = new Vector4(offset.x, offset.x, offset.x, offset.x);
                }

                from += offset;
                return true;
            }

            // Punch and Shake treat the end value as a strength, not a destination, so a
            // relative offset would be meaningless for them.
            var treatsEndAsStrength = step.Type is TweenType.Punch or TweenType.Shake;

            if (step.Relative && !treatsEndAsStrength) to = from + to;

            // Scale authored as a single uniform value broadcasts across all axes.
            if (step.UniformScale && key == TweenChannelKey.LocalScale)
            {
                if (!step.FromCurrent) from = new Vector4(from.x, from.x, from.x, from.x);
                to = new Vector4(to.x, to.x, to.x, to.x);
            }

            return true;
        }

        static int BuildInterpolation(TweenStep step, TweenValueKind kind, Vector4 from, Vector4 to,
            TweenChannelWriter writer, IMotionScheduler scheduler, List<MotionHandle> results)
        {
            switch (kind)
            {
                case TweenValueKind.Float:
                    results.Add(Configure(LMotion.Create(from.x, to.x, step.Duration), step, scheduler)
                        .Bind(writer, static (v, w) => w.WriteFloat(v)));
                    return 1;

                case TweenValueKind.Vector2:
                    results.Add(Configure(LMotion.Create((Vector2)from, (Vector2)to, step.Duration), step, scheduler)
                        .Bind(writer, static (v, w) => w.WriteVector2(v)));
                    return 1;

                case TweenValueKind.Vector4:
                    results.Add(Configure(LMotion.Create(from, to, step.Duration), step, scheduler)
                        .Bind(writer, static (v, w) => w.WriteVector4(v)));
                    return 1;

                default:
                    results.Add(Configure(LMotion.Create((Vector3)from, (Vector3)to, step.Duration), step, scheduler)
                        .Bind(writer, static (v, w) => w.WriteVector3(v)));
                    return 1;
            }
        }

        /// <summary>
        /// Builds a Punch or Shake. LitMotion models both as "base value plus a decaying
        /// oscillation", so the end value is read as a strength rather than a destination.
        /// </summary>
        static int BuildVibration(TweenStep step, TweenValueKind kind, Vector4 from, Vector4 strength,
            TweenChannelWriter writer, IMotionScheduler scheduler, List<MotionHandle> results,
            bool punch, out string error)
        {
            error = null;

            // LitMotion ships punch and shake adapters for float, Vector2 and Vector3 only.
            if (kind == TweenValueKind.Vector4)
            {
                error = step.DisplayName + ": " + step.Type + " does not support colour channels.";
                return 0;
            }

            var frequency = Mathf.Max(1, step.Frequency);
            var damping = Mathf.Max(0f, step.DampingRatio);

            if (punch)
            {
                switch (kind)
                {
                    case TweenValueKind.Float:
                        results.Add(Configure(LMotion.Punch.Create(from.x, strength.x, step.Duration), step, scheduler)
                            .WithFrequency(frequency).WithDampingRatio(damping)
                            .Bind(writer, static (v, w) => w.WriteFloat(v)));
                        return 1;
                    case TweenValueKind.Vector2:
                        results.Add(Configure(LMotion.Punch.Create((Vector2)from, (Vector2)strength, step.Duration), step, scheduler)
                            .WithFrequency(frequency).WithDampingRatio(damping)
                            .Bind(writer, static (v, w) => w.WriteVector2(v)));
                        return 1;
                    default:
                        results.Add(Configure(LMotion.Punch.Create((Vector3)from, (Vector3)strength, step.Duration), step, scheduler)
                            .WithFrequency(frequency).WithDampingRatio(damping)
                            .Bind(writer, static (v, w) => w.WriteVector3(v)));
                        return 1;
                }
            }

            switch (kind)
            {
                case TweenValueKind.Float:
                    results.Add(Configure(LMotion.Shake.Create(from.x, strength.x, step.Duration), step, scheduler)
                        .WithFrequency(frequency).WithDampingRatio(damping).WithRandomSeed(step.RandomSeed)
                        .Bind(writer, static (v, w) => w.WriteFloat(v)));
                    return 1;
                case TweenValueKind.Vector2:
                    results.Add(Configure(LMotion.Shake.Create((Vector2)from, (Vector2)strength, step.Duration), step, scheduler)
                        .WithFrequency(frequency).WithDampingRatio(damping).WithRandomSeed(step.RandomSeed)
                        .Bind(writer, static (v, w) => w.WriteVector2(v)));
                    return 1;
                default:
                    results.Add(Configure(LMotion.Shake.Create((Vector3)from, (Vector3)strength, step.Duration), step, scheduler)
                        .WithFrequency(frequency).WithDampingRatio(damping).WithRandomSeed(step.RandomSeed)
                        .Bind(writer, static (v, w) => w.WriteVector3(v)));
                    return 1;
            }
        }

        static int BuildJump(TweenStep step, TweenValueKind kind, Vector4 from, Vector4 to,
            TweenChannelWriter writer, IMotionScheduler scheduler, List<MotionHandle> results,
            out string error)
        {
            error = null;

            var options = new JumpOptions
            {
                JumpCount = Mathf.Max(1, step.JumpCount),
                JumpPower = step.JumpPower,
                Decay = JumpOptions.Default.Decay,
            };

            switch (kind)
            {
                case TweenValueKind.Vector2:
                    results.Add(Configure(LJump.Create((Vector2)from, (Vector2)to, step.Duration), step, scheduler)
                        .WithOptions(options)
                        .Bind(writer, static (v, w) => w.WriteVector2(v)));
                    return 1;

                case TweenValueKind.Vector3:
                    results.Add(Configure(LJump.Create((Vector3)from, (Vector3)to, step.Duration), step, scheduler)
                        .WithOptions(options)
                        .Bind(writer, static (v, w) => w.WriteVector3(v)));
                    return 1;

                default:
                    error = step.DisplayName + ": Jump needs a 2D or 3D channel, not " + kind + ".";
                    return 0;
            }
        }

        /// <summary>
        /// Builds a TextScramble: LitMotion interpolates the string itself, filling the
        /// not-yet-revealed tail with scramble characters.
        /// </summary>
        static int BuildScramble(TweenStep step, Object target, IMotionScheduler scheduler,
            List<MotionHandle> results, out string error)
        {
            error = null;

            var destination = step.TargetText ?? string.Empty;
            var source = string.Empty;

            // FromCurrent means "grow out of whatever the component already says", which is the
            // normal way to scramble an existing label into a new one.
            if (step.FromCurrent && TweenChannelAccessor.TryReadString(target, out var current))
            {
                source = current ?? string.Empty;
            }

            if (!TryFixedString(source, out var from) || !TryFixedString(destination, out var to))
            {
                error = step.DisplayName + ": TextScramble handles up to 500 bytes of text.";
                return 0;
            }

            var builder = LMotion.String.Create512Bytes(from, to, step.Duration)
                .WithRichText(step.RichText);

            if (step.ScrambleMode == ScrambleMode.Custom)
            {
                if (string.IsNullOrEmpty(step.ScrambleChars))
                {
                    error = step.DisplayName + ": a Custom scramble mode needs scramble characters.";
                    return 0;
                }

                if (!TryFixedString64(step.ScrambleChars, out var chars))
                {
                    error = step.DisplayName + ": scramble characters must fit in 60 bytes.";
                    return 0;
                }

                builder = builder.WithScrambleChars(chars);
            }
            else if (step.ScrambleMode != ScrambleMode.None)
            {
                builder = builder.WithScrambleChars(step.ScrambleMode);
            }

            if (step.RandomSeed != 0u) builder = builder.WithRandomSeed(step.RandomSeed);

            builder = Configure(builder, step, scheduler);

#if LMTE_SUPPORT_TMP
            if (target is TMP_Text tmp)
            {
                results.Add(builder.BindToText(tmp));
                return 1;
            }
#endif
#if LMTE_SUPPORT_UGUI
            if (target is Text text)
            {
                results.Add(builder.BindToText(text));
                return 1;
            }
#endif

            error = step.DisplayName + ": TextScramble needs a TMP_Text or a UI Text.";
            return 0;
        }

        /// <summary>
        /// Builds a per-character TMP step, optionally one motion per character so a single
        /// step reads as a wave across the text.
        /// </summary>
        /// <remarks>
        /// These bind through LitMotion's own <c>BindToTMPChar*</c> extensions rather than the
        /// shared channel table, because per-character state lives in LitMotion's internal TMP
        /// animator and there is no component property to read or write.
        /// </remarks>
        static int BuildTmpCharacter(TweenStep step, Object target, IMotionScheduler scheduler,
            List<MotionHandle> results, out string error)
        {
            error = null;

#if LMTE_SUPPORT_TMP
            if (target is not TMP_Text text)
            {
                error = step.DisplayName + ": TMPCharacter needs a TMP_Text.";
                return 0;
            }

            var count = TweenChannelAccessor.CountUnits(text, TweenTextUnit.Characters);
            if (count <= 0)
            {
                error = step.DisplayName + ": " + text.name + " has no characters to animate.";
                return 0;
            }

            // A negative index means every character, each one offset a little further in time.
            var all = step.CharacterIndex < 0;
            if (!all && step.CharacterIndex >= count)
            {
                error = step.DisplayName + ": character " + step.CharacterIndex + " is past the end of "
                        + text.name + ", which has " + count + ".";
                return 0;
            }

            var first = all ? 0 : step.CharacterIndex;
            var last = all ? count - 1 : step.CharacterIndex;
            var stagger = all ? Mathf.Max(0f, step.Stagger) : 0f;
            var written = 0;

            for (var index = first; index <= last; index++)
            {
                var delay = step.Delay + stagger * (index - first);
                written += BuildTmpCharacterMotion(step, text, index, delay, scheduler, results);
            }

            return written;
#else
            error = step.DisplayName + ": TMPCharacter needs TextMeshPro, which is not installed.";
            return 0;
#endif
        }

#if LMTE_SUPPORT_TMP
        static int BuildTmpCharacterMotion(TweenStep step, TMP_Text text, int index, float delay,
            IMotionScheduler scheduler, List<MotionHandle> results)
        {
            switch (step.TMPCharChannel)
            {
                case TweenTMPCharChannel.Scale:
                    results.Add(Stagger(LMotion.Create((Vector3)step.From, (Vector3)step.To, step.Duration),
                            step, scheduler, delay)
                        .BindToTMPCharScale(text, index));
                    return 1;

                case TweenTMPCharChannel.Rotation:
                    results.Add(Stagger(LMotion.Create((Vector3)step.From, (Vector3)step.To, step.Duration),
                            step, scheduler, delay)
                        .BindToTMPCharEulerAngles(text, index));
                    return 1;

                case TweenTMPCharChannel.Color:
                    results.Add(Stagger(LMotion.Create(step.FromColor, step.ToColor, step.Duration),
                            step, scheduler, delay)
                        .BindToTMPCharColor(text, index));
                    return 1;

                case TweenTMPCharChannel.Alpha:
                    results.Add(Stagger(LMotion.Create(step.From.x, step.To.x, step.Duration),
                            step, scheduler, delay)
                        .BindToTMPCharColorA(text, index));
                    return 1;

                default:
                    results.Add(Stagger(LMotion.Create((Vector3)step.From, (Vector3)step.To, step.Duration),
                            step, scheduler, delay)
                        .BindToTMPCharPosition(text, index));
                    return 1;
            }
        }

        /// <summary>
        /// Applies the shared step settings and then overrides the delay, which is what spaces
        /// the per-character motions out into a wave.
        /// </summary>
        static MotionBuilder<TValue, TOptions, TAdapter> Stagger<TValue, TOptions, TAdapter>(
            MotionBuilder<TValue, TOptions, TAdapter> builder, TweenStep step, IMotionScheduler scheduler,
            float delay)
            where TValue : unmanaged
            where TOptions : unmanaged, IMotionOptions
            where TAdapter : unmanaged, IMotionAdapter<TValue, TOptions>
        {
            builder = Configure(builder, step, scheduler);
            return delay > 0f ? builder.WithDelay(delay, step.DelayType) : builder;
        }
#endif

        /// <summary>
        /// Checks that a MaterialProperty step has somewhere to write before the generic path
        /// reports the failure as an unreadable value.
        /// </summary>
        /// <remarks>
        /// Also hands back the material that will actually be written. At runtime that is the
        /// target's own instance, created here once if the target was still on a shared one.
        /// </remarks>
        static bool TryValidateMaterial(TweenStep step, Object target, in TweenChannelContext context,
            out Material material, out string error)
        {
            error = null;
            material = null;

            if (string.IsNullOrWhiteSpace(step.PropertyName))
            {
                error = step.DisplayName + ": set the shader property name this step should animate.";
                return false;
            }

            material = TweenChannelAccessor.ResolveMaterialForWrite(target);
            if (material == null)
            {
                error = step.DisplayName + ": " + target.name
                        + " has no material to animate. Assign one, or target a Material asset directly.";
                return false;
            }

            if (!material.HasProperty(context.PropertyId))
            {
                error = step.DisplayName + ": " + material.name + " has no property called '"
                        + step.PropertyName + "'.";
                return false;
            }

            return true;
        }

        static bool TryFixedString(string value, out FixedString512Bytes result)
        {
            result = default;
            if (string.IsNullOrEmpty(value)) return true;

            return result.CopyFrom(value) == CopyError.None;
        }

        static bool TryFixedString64(string value, out FixedString64Bytes result)
        {
            result = default;
            if (string.IsNullOrEmpty(value)) return true;

            return result.CopyFrom(value) == CopyError.None;
        }

        /// <summary>
        /// Builds the non-binding step types. Each still creates a real motion so that it
        /// occupies its slot on the sequence timeline.
        /// </summary>
        static int BuildPrimitive(TweenStep step, IMotionScheduler scheduler, List<MotionHandle> results)
        {
            switch (step.Type)
            {
                case TweenType.Interval:
                    // Reserves time. Bound to a no-op because LitMotion has no unbound-but-timed
                    // motion, and RunWithoutBinding would not join a sequence.
                    results.Add(Configure(LMotion.Create(0f, 1f, step.Duration), step, scheduler)
                        .Bind(static _ => { }));
                    return 1;

                case TweenType.Callback:
                {
                    var callback = step.OnCallback;
                    if (callback == null) return 0;

                    // A zero-duration motion fires its completion the moment the playhead
                    // reaches it, which is exactly the semantics of a timeline marker.
                    results.Add(Configure(LMotion.Create(0f, 1f, 0f), step, scheduler)
                        .WithOnComplete(callback.Invoke)
                        .Bind(static _ => { }));
                    return 1;
                }

                case TweenType.Custom:
                {
                    var progress = step.OnProgress;
                    if (progress == null) return 0;

                    results.Add(Configure(LMotion.Create(0f, 1f, step.Duration), step, scheduler)
                        .Bind(progress, static (v, e) => e.Invoke(v)));
                    return 1;
                }

                default:
                    return 0;
            }
        }

        /// <summary>
        /// Applies the settings shared by every step type: easing, delay, looping and scheduler.
        /// </summary>
        static MotionBuilder<TValue, TOptions, TAdapter> Configure<TValue, TOptions, TAdapter>(
            MotionBuilder<TValue, TOptions, TAdapter> builder, TweenStep step, IMotionScheduler scheduler)
            where TValue : unmanaged
            where TOptions : unmanaged, IMotionOptions
            where TAdapter : unmanaged, IMotionAdapter<TValue, TOptions>
        {
            if (step.Ease == Ease.CustomAnimationCurve && step.CustomCurve != null)
            {
                builder = builder.WithEase(step.CustomCurve);
            }
            else if (step.Ease != Ease.CustomAnimationCurve)
            {
                builder = builder.WithEase(step.Ease);
            }

            if (step.Delay > 0f) builder = builder.WithDelay(step.Delay, step.DelayType);

            // A child motion that loops forever would give its parent sequence an infinite
            // duration, which no sequence can schedule. Animation-level Loops is the supported
            // way to repeat indefinitely.
            var loops = EffectiveLoops(step);
            if (loops != 1) builder = builder.WithLoops(loops, step.LoopType);

            if (alone)
            {
                // Standing in for the animation's sequence. The runner only takes this path when
                // the step does not loop by itself, so the two loop settings never compete.
                if (aloneLoops != 1) builder = builder.WithLoops(aloneLoops, aloneLoopType);
                if (aloneOnComplete != null) builder = builder.WithOnComplete(aloneOnComplete);
                if (aloneOnCancel != null) builder = builder.WithOnCancel(aloneOnCancel);
            }

            if (scheduler != null) builder = builder.WithScheduler(scheduler);

            return builder;
        }

        /// <summary>
        /// True when the step asks for infinite looping, which is silently clamped inside a
        /// sequence. The inspector surfaces this so the clamp is never a surprise.
        /// </summary>
        public static bool HasUnsupportedInfiniteLoop(TweenStep step)
        {
            return step != null && step.Enabled && step.Loops < 0;
        }

        /// <summary>
        /// Explains why a resolved target yielded no channel.
        /// </summary>
        /// <remarks>
        /// Reached when resolution found a component but that component has no channel for the
        /// step's type -- a Fade pointed at a plain Renderer, for instance. Naming the component
        /// type is what makes that actionable.
        /// </remarks>
        static string DescribeMissingChannel(TweenStep step, Object target)
        {
            var label = string.IsNullOrWhiteSpace(step.Label) ? string.Empty : step.Label + ": ";

            if (step.Type == TweenType.Extension)
            {
                return label + "no extension channel is registered as '" + step.ExtensionId + "'.";
            }

            return label + step.Type + " cannot animate a " + target.GetType().Name + ".";
        }
    }
}
