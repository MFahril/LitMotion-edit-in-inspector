using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace LitMotion.TweenEditor.Editor
{
    /// <summary>
    /// Time ruler across the top of the timeline. Clicking or dragging it scrubs.
    /// </summary>
    /// <remarks>
    /// Tick spacing is chosen from the current zoom so labels never collide: the interval steps
    /// through a 1 / 2 / 5 sequence per decade, which is the standard way to keep a ruler legible
    /// across several orders of magnitude.
    /// </remarks>
    internal sealed class TweenTimelineRuler : VisualElement
    {
        static readonly float[] Steps = { 0.01f, 0.025f, 0.05f, 0.1f, 0.25f, 0.5f, 1f, 2.5f, 5f, 10f, 30f, 60f };

        readonly Func<TweenTimelineContext> contextProvider;
        bool scrubbing;

        TweenAnimation animation;

        /// <summary>Raised with a time in seconds when the user scrubs.</summary>
        public event Action<float> Scrubbed;

        public TweenTimelineRuler(Func<TweenTimelineContext> provider)
        {
            contextProvider = provider;

            style.height = TweenTimelineStyles.RulerHeight;
            style.backgroundColor = TweenTimelineStyles.Background;
            style.borderBottomWidth = 1f;
            style.borderBottomColor = TweenTimelineStyles.Border;

            generateVisualContent += OnGenerateVisualContent;

            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<PointerCaptureOutEvent>(_ => scrubbing = false);
        }

        /// <summary>
        /// Points the ruler at an animation, so callback markers can be drawn above the ticks.
        /// </summary>
        public void Bind(TweenAnimation target)
        {
            animation = target;
            MarkDirtyRepaint();
        }

        /// <summary>The tick interval that keeps labels readable at the given zoom.</summary>
        public static float ChooseTickInterval(float pixelsPerSecond)
        {
            const float minPixelsBetweenLabels = 56f;

            for (var i = 0; i < Steps.Length; i++)
            {
                if (Steps[i] * pixelsPerSecond >= minPixelsBetweenLabels) return Steps[i];
            }

            return Steps[Steps.Length - 1];
        }

        void OnGenerateVisualContent(MeshGenerationContext context)
        {
            var rect = contentRect;
            if (rect.width <= 1f) return;

            var timeline = contextProvider?.Invoke() ?? TweenTimelineContext.Default;
            var pixelsPerSecond = Mathf.Max(1f, timeline.PixelsPerSecond);
            var interval = ChooseTickInterval(pixelsPerSecond);

            var painter = context.painter2D;
            painter.strokeColor = TweenTimelineStyles.GridLineMajor;
            painter.lineWidth = 1f;

            var count = Mathf.CeilToInt(rect.width / (interval * pixelsPerSecond)) + 1;
            for (var i = 0; i < count; i++)
            {
                var seconds = i * interval;
                var x = Mathf.Round(seconds * pixelsPerSecond) + 0.5f;
                if (x > rect.width) break;

                painter.BeginPath();
                painter.MoveTo(new Vector2(x, rect.height * 0.55f));
                painter.LineTo(new Vector2(x, rect.height));
                painter.Stroke();
            }

            DrawCallbackMarkers(painter, rect, pixelsPerSecond);
        }

        /// <summary>
        /// Draws a pip on the ruler for every callback step.
        /// </summary>
        /// <remarks>
        /// A callback is a zero-length clip, so on a busy timeline it is a sliver that is easy
        /// to lose. Marking it on the ruler puts the moment something fires where the eye
        /// already is when reading time.
        /// </remarks>
        void DrawCallbackMarkers(Painter2D painter, Rect rect, float pixelsPerSecond)
        {
            if (animation?.Steps == null) return;

            painter.fillColor = TweenTimelineStyles.ClipColor(TweenType.Callback);

            for (var i = 0; i < animation.Steps.Count; i++)
            {
                var step = animation.Steps[i];
                if (step == null || !step.Enabled || step.Type != TweenType.Callback) continue;

                var x = step.StartTime * pixelsPerSecond;
                if (x < 0f || x > rect.width) continue;

                painter.BeginPath();
                painter.MoveTo(new Vector2(x, rect.height * 0.5f));
                painter.LineTo(new Vector2(x - 4f, rect.height * 0.5f - 6f));
                painter.LineTo(new Vector2(x + 4f, rect.height * 0.5f - 6f));
                painter.ClosePath();
                painter.Fill();
            }
        }

        /// <summary>
        /// Rebuilds the tick labels. Kept as real Labels rather than painted text because
        /// Painter2D cannot draw text.
        /// </summary>
        public void RefreshLabels()
        {
            Clear();

            var timeline = contextProvider?.Invoke() ?? TweenTimelineContext.Default;
            var pixelsPerSecond = Mathf.Max(1f, timeline.PixelsPerSecond);
            var interval = ChooseTickInterval(pixelsPerSecond);

            var width = resolvedStyle.width;
            if (float.IsNaN(width) || width <= 1f) width = 600f;

            var count = Mathf.CeilToInt(width / (interval * pixelsPerSecond)) + 1;
            for (var i = 0; i < count; i++)
            {
                var seconds = i * interval;
                var x = seconds * pixelsPerSecond;
                if (x > width) break;

                Add(new Label(FormatTime(seconds))
                {
                    pickingMode = PickingMode.Ignore,
                    style =
                    {
                        position = Position.Absolute,
                        left = x + 3f,
                        top = 1f,
                        fontSize = 9f,
                        color = TweenTimelineStyles.RulerText,
                    },
                });
            }

            MarkDirtyRepaint();
        }

        static string FormatTime(float seconds)
        {
            if (seconds >= 10f) return seconds.ToString("0.#") + "s";
            if (seconds >= 1f) return seconds.ToString("0.##") + "s";
            return seconds.ToString("0.###") + "s";
        }

        void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0) return;

            scrubbing = true;
            this.CapturePointer(evt.pointerId);
            EmitScrub(evt.localPosition.x);
            evt.StopPropagation();
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            if (!scrubbing) return;

            EmitScrub(evt.localPosition.x);
            evt.StopPropagation();
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            if (!scrubbing) return;

            scrubbing = false;
            this.ReleasePointer(evt.pointerId);
            evt.StopPropagation();
        }

        void EmitScrub(float localX)
        {
            var timeline = contextProvider?.Invoke() ?? TweenTimelineContext.Default;
            Scrubbed?.Invoke(Mathf.Max(0f, localX / Mathf.Max(1f, timeline.PixelsPerSecond)));
        }
    }
}
