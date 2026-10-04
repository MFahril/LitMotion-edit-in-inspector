using System;
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
    /// Writes a TextCounter's number into its label without building a string every frame.
    /// </summary>
    /// <remarks>
    /// <c>string.Format</c> boxes the float and allocates the result on every write, which is
    /// garbage on every frame of a counting label. Instead the format is split once, at build
    /// time, into the text before the number, the number's own format spec and the text after
    /// it. Each write formats the number with <c>float.TryFormat</c> into a reused buffer.
    ///
    /// TMP takes the buffer directly through <c>SetCharArray</c>, which allocates nothing in a
    /// player. A uGUI <c>Text</c> can only take a string, so it is given one only when the
    /// characters actually differ from what it already shows: an <c>N0</c> counter allocates once
    /// per visible change, not once per frame.
    ///
    /// A format the split cannot express -- more than one placeholder, or an alignment such as
    /// <c>{0,8}</c> -- falls back to <c>string.Format</c>, so every valid format still works.
    /// </remarks>
    internal sealed class TweenCounterText
    {
        readonly string prefix;
        readonly string spec;
        readonly string suffix;

        /// <summary>Set when the format needs <c>string.Format</c> itself.</summary>
        readonly string fallbackFormat;

        char[] buffer = new char[32];
        int length;

        // What this counter last handed to a TMP label, to skip rewriting identical text.
        char[] written = new char[32];
        int writtenLength = -1;

        TweenCounterText(string prefix, string spec, string suffix, string fallbackFormat)
        {
            this.prefix = prefix;
            this.spec = spec;
            this.suffix = suffix;
            this.fallbackFormat = fallbackFormat;
        }

        /// <summary>
        /// Prepares <paramref name="format"/> for repeated writes, or returns null when it is not
        /// a valid format string.
        /// </summary>
        public static TweenCounterText Create(string format)
        {
            if (string.IsNullOrEmpty(format)) format = "{0}";

            try
            {
                string.Format(format, 0f);
            }
            catch (FormatException)
            {
                return null;
            }

            return TrySplit(format, out var prefix, out var spec, out var suffix)
                ? new TweenCounterText(prefix, spec, suffix, null)
                : new TweenCounterText(null, null, null, format);
        }

        /// <summary>The characters the last <see cref="Format"/> produced.</summary>
        internal ReadOnlySpan<char> Formatted => buffer.AsSpan(0, length);

        /// <summary>True when this counter formats through the reusable buffer.</summary>
        internal bool IsBuffered => fallbackFormat == null;

        /// <summary>Writes <paramref name="value"/>, formatted, to a text target.</summary>
        public void Write(Object target, float value)
        {
            if (fallbackFormat != null)
            {
                TweenChannelAccessor.TryWriteString(target, string.Format(fallbackFormat, value));
                return;
            }

            Format(value);

#if LMTE_SUPPORT_TMP
            if (target is TMP_Text tmp)
            {
                if (writtenLength == length && Formatted.SequenceEqual(written.AsSpan(0, length))) return;

                tmp.SetCharArray(buffer, 0, length);
                Remember();
                return;
            }
#endif
#if LMTE_SUPPORT_UGUI
            if (target is Text text)
            {
                // Compared against the label itself, so a string another script wrote is
                // never mistaken for one of ours.
                if (Formatted.SequenceEqual(text.text.AsSpan())) return;

                text.text = new string(buffer, 0, length);
            }
#endif
        }

        /// <summary>Formats <paramref name="value"/> into the buffer. Allocates nothing once warm.</summary>
        internal void Format(float value)
        {
            length = 0;
            Append(prefix);

            Span<char> number = stackalloc char[64];
            if (value.TryFormat(number, out var count, spec))
            {
                Append(number.Slice(0, count));
            }
            else
            {
                // A custom spec long enough to overflow the stack buffer. Rare, so allocating
                // here is cheaper than a larger buffer on every write.
                Append(value.ToString(spec).AsSpan());
            }

            Append(suffix);
        }

        void Append(string text)
        {
            if (!string.IsNullOrEmpty(text)) Append(text.AsSpan());
        }

        void Append(ReadOnlySpan<char> text)
        {
            if (length + text.Length > buffer.Length)
            {
                Array.Resize(ref buffer, Math.Max(buffer.Length * 2, length + text.Length));
            }

            text.CopyTo(buffer.AsSpan(length));
            length += text.Length;
        }

        void Remember()
        {
            if (written.Length < length) written = new char[buffer.Length];
            Array.Copy(buffer, written, length);
            writtenLength = length;
        }

        /// <summary>
        /// Splits a composite format with exactly one <c>{0}</c> or <c>{0:spec}</c> placeholder
        /// into the text around it, unescaping <c>{{</c> and <c>}}</c>.
        /// </summary>
        internal static bool TrySplit(string format, out string prefix, out string spec, out string suffix)
        {
            prefix = spec = suffix = null;

            var open = FindPlaceholder(format, 0);
            if (open < 0) return false;

            var close = format.IndexOf('}', open);
            if (close < 0) return false;

            var body = format.Substring(open + 1, close - open - 1);
            if (body == "0")
            {
                spec = null;
            }
            else if (body.StartsWith("0:", StringComparison.Ordinal) && body.IndexOf('{') < 0)
            {
                spec = body.Substring(2);
            }
            else
            {
                // An alignment, another argument index, or anything else: not ours to handle.
                return false;
            }

            // A brace straight after the closing one would be an escaped brace inside the spec.
            if (close + 1 < format.Length && format[close + 1] == '}' && spec != null) return false;

            if (FindPlaceholder(format, close + 1) >= 0) return false;

            prefix = Unescape(format.Substring(0, open));
            suffix = Unescape(format.Substring(close + 1));
            return true;
        }

        /// <summary>The index of the first unescaped <c>{</c> at or after <paramref name="start"/>.</summary>
        static int FindPlaceholder(string format, int start)
        {
            for (var i = start; i < format.Length; i++)
            {
                if (format[i] != '{') continue;
                if (i + 1 < format.Length && format[i + 1] == '{')
                {
                    i++;
                    continue;
                }

                return i;
            }

            return -1;
        }

        static string Unescape(string text)
        {
            return text.Replace("{{", "{").Replace("}}", "}");
        }
    }
}
