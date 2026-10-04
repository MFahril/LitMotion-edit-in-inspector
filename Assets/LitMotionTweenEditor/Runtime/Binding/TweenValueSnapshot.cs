using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LitMotion.TweenEditor
{
    /// <summary>
    /// Captures the live value of every channel an animation touches, so they can be put back.
    /// </summary>
    /// <remarks>
    /// This underpins two separate features. At runtime it backs
    /// <see cref="TweenKillBehavior.Rewind"/>. In the editor it is what guarantees a preview
    /// leaves the scene exactly as it found it -- without this, scrubbing a tween would
    /// permanently overwrite authored values.
    ///
    /// Entries are deduplicated per (target, channel) pair, so a Move step and a Punch step
    /// both writing local position capture and restore that position once. Capture order is
    /// preserved and restore replays it, which keeps dependent channels consistent.
    ///
    /// Targets are keyed by object reference rather than instance id: on Unity 6000.5+
    /// <c>Object.GetInstanceID()</c> is a compile error, and the reference is both cheaper and
    /// version-independent.
    /// </remarks>
    public sealed class TweenValueSnapshot
    {
        readonly struct Entry
        {
            public readonly Object Target;
            public readonly TweenChannelKey Key;
            public readonly Vector4 Value;
            public readonly TweenAxis Axis;
            public readonly TweenChannelContext Context;

            /// <summary>
            /// The original string, for the text channels. Null for every numeric channel.
            /// </summary>
            public readonly string Text;

            public Entry(Object target, TweenChannelKey key, Vector4 value, TweenAxis axis,
                TweenChannelContext context, string text = null)
            {
                Target = target;
                Key = key;
                Value = value;
                Axis = axis;
                Context = context;
                Text = text;
            }
        }

        readonly List<Entry> entries = new();
        readonly HashSet<(Object, TweenChannelKey, int, ITweenExtensionChannel)> captured = new();

        /// <summary>
        /// What makes a capture distinct. The key alone is not enough for the parameterized
        /// channels: two material properties on one material share a key and differ only by
        /// property id, and two extension channels on one object differ only by which channel.
        /// Without these in the key, the second capture was dropped and that value was never
        /// restored after a preview.
        /// </summary>
        static (Object, TweenChannelKey, int, ITweenExtensionChannel) Identity(Object target, TweenChannelKey key,
            in TweenChannelContext context)
        {
            var isMaterial = key is TweenChannelKey.MaterialFloat
                or TweenChannelKey.MaterialColor
                or TweenChannelKey.MaterialVector;

            return (target, key, isMaterial ? context.PropertyId : 0,
                key == TweenChannelKey.Extension ? context.Extension : null);
        }

        /// <summary>Number of distinct channels captured.</summary>
        public int Count => entries.Count;

        /// <summary>True when nothing has been captured.</summary>
        public bool IsEmpty => entries.Count == 0;

        /// <summary>
        /// Captures every channel the animation's enabled steps write.
        /// </summary>
        /// <param name="animation">Animation whose steps are inspected.</param>
        /// <param name="fallback">GameObject used for steps with no explicit target.</param>
        public void CaptureAnimation(TweenAnimation animation, GameObject fallback)
        {
            if (animation?.Steps == null) return;

            for (var i = 0; i < animation.Steps.Count; i++)
            {
                CaptureStep(animation.Steps[i], fallback);
            }
        }

        /// <summary>
        /// Captures the channel a single step writes. No-op for disabled or unbindable steps.
        /// </summary>
        public void CaptureStep(TweenStep step, GameObject fallback)
        {
            if (step == null || !step.Enabled) return;
            if (TweenBindingResolver.IsNonBinding(step.Type)) return;

            var target = TweenBindingResolver.Resolve(step, fallback, out _);
            if (target == null) return;

            var key = TweenChannelAccessor.GetChannelKey(step, target);
            if (key == TweenChannelKey.None) return;

            var context = TweenChannelContext.For(step);

            // An Anchors step set to Both writes two channels, so capture both.
            if (step.Type == TweenType.Anchors && step.AnchorTarget == TweenAnchorTarget.Both)
            {
                Capture(target, TweenChannelKey.AnchorMin, TweenAxis.All, context);
                Capture(target, TweenChannelKey.AnchorMax, TweenAxis.All, context);
                return;
            }

            Capture(target, key, TweenChannelAccessor.GetEffectiveAxis(step, key), context);
        }

        void Capture(Object target, TweenChannelKey key, TweenAxis axis, TweenChannelContext context)
        {
            var identity = Identity(target, key, context);
            if (!captured.Add(identity)) return;

            // Text channels replace the whole string, so the string is the thing to put back.
            if (TweenChannelAccessor.IsTextChannel(key))
            {
                if (TweenChannelAccessor.TryReadString(target, out var text))
                {
                    entries.Add(new Entry(target, key, Vector4.zero, axis, context, text));
                }
                else
                {
                    captured.Remove(identity);
                }

                return;
            }

            // Per-character TMP state lives inside LitMotion's animator and cannot be read, so
            // the entry exists only to trigger a mesh rebuild on restore.
            if (key == TweenChannelKey.TmpCharacter)
            {
                entries.Add(new Entry(target, key, Vector4.zero, axis, context));
                return;
            }

            if (TweenChannelAccessor.TryRead(key, target, context, out var value))
            {
                entries.Add(new Entry(target, key, value, axis, context));
            }
            else
            {
                // Could not read it, so we must not claim we can restore it.
                captured.Remove(identity);
            }
        }

        /// <summary>
        /// Writes every captured value back to its target, in capture order.
        /// </summary>
        /// <remarks>
        /// Restores the full channel rather than only the axes the step animated: a preview must
        /// undo everything it wrote, and the captured value is by definition the correct state
        /// for every component. Targets destroyed since capture are skipped.
        /// </remarks>
        public void Restore()
        {
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry.Target == null) continue;

                if (entry.Text != null)
                {
                    TweenChannelAccessor.TryWriteString(entry.Target, entry.Text);
                    continue;
                }

                if (entry.Key == TweenChannelKey.TmpCharacter)
                {
                    TweenChannelAccessor.RefreshText(entry.Target);
                    continue;
                }

                TweenChannelAccessor.TryWrite(entry.Key, entry.Target, entry.Value, TweenAxis.All, entry.Context);
            }
        }

        /// <summary>Restores every captured value and then forgets them.</summary>
        public void RestoreAndClear()
        {
            Restore();
            Clear();
        }

        /// <summary>Discards all captured values without restoring them.</summary>
        public void Clear()
        {
            entries.Clear();
            captured.Clear();
        }

        /// <summary>
        /// The distinct objects this snapshot would write to on <see cref="Restore"/>.
        /// </summary>
        /// <remarks>
        /// The editor preview uses this to record undo state and to decide which scenes it must
        /// leave unmarked.
        /// </remarks>
        public void GetTargets(List<Object> results)
        {
            if (results == null) return;
            results.Clear();

            for (var i = 0; i < entries.Count; i++)
            {
                var target = entries[i].Target;
                if (target != null && !results.Contains(target)) results.Add(target);
            }
        }
    }
}
