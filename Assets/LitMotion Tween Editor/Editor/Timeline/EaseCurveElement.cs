using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace LitMotion.TweenEditor.Editor
{
    /// <summary>
    /// Draws the shape of a step's easing, so the curve can be judged without playing anything.
    /// </summary>
    /// <remarks>
    /// Sampled through <see cref="EaseUtility.Evaluate"/>, the same public function LitMotion's
    /// own motion pipeline uses, so the drawn curve is the curve that will actually play. For
    /// <see cref="Ease.CustomAnimationCurve"/> the step's <see cref="AnimationCurve"/> is sampled
    /// directly instead.
    ///
    /// Overshooting eases (Back, Elastic) leave 0-1, so the vertical range is measured from the
    /// samples rather than assumed, and the 0 and 1 guide lines stay in place to show by how much.
    /// </remarks>
    internal sealed class EaseCurveElement : VisualElement
    {
        const int SampleCount = 96;

        readonly Vector2[] samples = new Vector2[SampleCount];

        Ease ease = Ease.Linear;
        AnimationCurve curve;

        Ease? ghost;
        float? playhead;

        public EaseCurveElement()
        {
            style.height = 48f;
            style.backgroundColor = TweenTimelineStyles.LaneBackground;
            TweenTimelineStyles.SetBorder(this, 1f, TweenTimelineStyles.Border);
            TweenTimelineStyles.SetRadius(this, 2f);

            generateVisualContent += OnGenerateVisualContent;
        }

        /// <summary>Points the graph at a different easing and repaints.</summary>
        public void SetEase(Ease value, AnimationCurve customCurve)
        {
            // Changing easing is a comparison, so the outgoing curve stays on screen behind the
            // new one. Nothing is remembered when the ease has not actually changed, or the
            // ghost would eventually be a copy of the live curve.
            if (value != ease) ghost = ease;

            ease = value;
            curve = customCurve;
            MarkDirtyRepaint();
        }

        /// <summary>Drops the comparison curve.</summary>
        public void ClearGhost()
        {
            ghost = null;
            MarkDirtyRepaint();
        }

        /// <summary>
        /// Puts a dot on the curve at this normalized progress, or removes it when given null.
        /// </summary>
        /// <remarks>
        /// During a preview this is where the step currently is, which is what makes an
        /// overshooting ease legible: the dot visibly goes past 1 and comes back.
        /// </remarks>
        public void SetPlayhead(float? normalized)
        {
            var clamped = normalized.HasValue ? Mathf.Clamp01(normalized.Value) : (float?)null;
            if (Nullable.Equals(clamped, playhead)) return;

            playhead = clamped;
            MarkDirtyRepaint();
        }

        void OnGenerateVisualContent(MeshGenerationContext context)
        {
            var rect = contentRect;
            if (rect.width <= 2f || rect.height <= 2f) return;

            const float padding = 5f;
            var plot = new Rect(
                padding,
                padding,
                Mathf.Max(1f, rect.width - padding * 2f),
                Mathf.Max(1f, rect.height - padding * 2f));

            // Sample first so the vertical range can account for overshoot.
            var min = 0f;
            var max = 1f;
            for (var i = 0; i < SampleCount; i++)
            {
                var t = i / (float)(SampleCount - 1);
                var v = Evaluate(t);
                samples[i] = new Vector2(t, v);
                if (v < min) min = v;
                if (v > max) max = v;
            }

            // The ghost shares the live curve's vertical range, so the two are comparable even
            // when the outgoing ease overshot further than the incoming one.
            if (ghost.HasValue)
            {
                for (var i = 0; i < SampleCount; i++)
                {
                    var v = EaseUtility.Evaluate(i / (float)(SampleCount - 1), ghost.Value);
                    if (v < min) min = v;
                    if (v > max) max = v;
                }
            }

            var range = Mathf.Max(0.0001f, max - min);
            var painter = context.painter2D;

            // Guide lines at v=0 and v=1, so overshoot is visible as a departure from them.
            painter.strokeColor = TweenTimelineStyles.CurveGuide;
            painter.lineWidth = 1f;
            DrawGuide(painter, plot, 0f, min, range);
            DrawGuide(painter, plot, 1f, min, range);

            if (ghost.HasValue) DrawGhost(painter, plot, min, range);

            painter.strokeColor = TweenTimelineStyles.CurveLine;
            painter.lineWidth = 2f;
            painter.BeginPath();

            for (var i = 0; i < SampleCount; i++)
            {
                var p = ToPixel(samples[i], plot, min, range);
                if (i == 0) painter.MoveTo(p);
                else painter.LineTo(p);
            }

            painter.Stroke();

            if (playhead.HasValue) DrawPlayhead(painter, plot, min, range);
        }

        void DrawGhost(Painter2D painter, Rect plot, float min, float range)
        {
            var color = TweenTimelineStyles.CurveLine;
            color.a = 0.28f;

            painter.strokeColor = color;
            painter.lineWidth = 1f;
            painter.BeginPath();

            for (var i = 0; i < SampleCount; i++)
            {
                var t = i / (float)(SampleCount - 1);
                var p = ToPixel(new Vector2(t, EaseUtility.Evaluate(t, ghost.Value)), plot, min, range);

                if (i == 0) painter.MoveTo(p);
                else painter.LineTo(p);
            }

            painter.Stroke();
        }

        void DrawPlayhead(Painter2D painter, Rect plot, float min, float range)
        {
            var t = playhead.Value;
            var p = ToPixel(new Vector2(t, Evaluate(t)), plot, min, range);

            painter.fillColor = TweenTimelineStyles.Playhead;
            painter.BeginPath();
            painter.Arc(p, 3f, 0f, 360f);
            painter.Fill();
        }

        float Evaluate(float t)
        {
            if (ease == Ease.CustomAnimationCurve)
            {
                return curve == null ? t : curve.Evaluate(t);
            }

            return EaseUtility.Evaluate(t, ease);
        }

        static void DrawGuide(Painter2D painter, Rect plot, float value, float min, float range)
        {
            var y = plot.yMax - (value - min) / range * plot.height;
            painter.BeginPath();
            painter.MoveTo(new Vector2(plot.xMin, y));
            painter.LineTo(new Vector2(plot.xMax, y));
            painter.Stroke();
        }

        static Vector2 ToPixel(Vector2 sample, Rect plot, float min, float range)
        {
            return new Vector2(
                plot.xMin + sample.x * plot.width,
                plot.yMax - (sample.y - min) / range * plot.height);
        }
    }
}
