using System;
using System.Collections.Generic;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace LitMotion.TweenEditor.Editor
{
    /// <summary>
    /// Transport controls under the timeline: play, stop, frame-step, a typed time, loop, speed,
    /// auto-follow, and the binding status readout.
    /// </summary>
    internal sealed class TweenPreviewBar : VisualElement
    {
        static readonly float[] Speeds = { 0.1f, 0.25f, 0.5f, 1f, 2f, 4f };

        readonly Button playButton;
        readonly Button stopButton;
        readonly FloatField timeField;
        readonly Label durationLabel;
        readonly Toggle loopToggle;
        readonly DropdownField speedField;
        readonly Toggle autoToggle;
        readonly Button statusButton;

        bool playing;

        public event Action PlayToggled;
        public event Action StopRequested;
        public event Action<int> FrameStepRequested;
        public event Action<float> TimeEntered;
        public event Action<bool> LoopChanged;
        public event Action<float> SpeedChanged;
        public event Action<bool> AutoChanged;
        public event Action StatusClicked;

        public TweenPreviewBar()
        {
            style.flexDirection = FlexDirection.Row;
            style.flexWrap = Wrap.Wrap;
            style.alignItems = Align.Center;
            style.marginBottom = 2f;

            playButton = TweenUi.IconButton("PlayButton", "Play", "Play or pause the preview  (Space)",
                () => PlayToggled?.Invoke());
            Add(playButton);

            stopButton = new Button(() => StopRequested?.Invoke())
            {
                text = "Stop",
                tooltip = "Stop the preview and put every value back exactly as it was",
                style = { height = 20f, marginLeft = 1f, marginRight = 1f },
            };
            Add(stopButton);

            Add(TweenUi.IconButton("Animation.PrevKey", "<", "Back one frame (1/60 s)",
                () => FrameStepRequested?.Invoke(-1)));
            Add(TweenUi.IconButton("Animation.NextKey", ">", "Forward one frame (1/60 s)",
                () => FrameStepRequested?.Invoke(1)));

            timeField = new FloatField
            {
                isDelayed = true,
                tooltip = "Type a time in seconds and press Enter to scrub there",
                style = { width = 46f, marginLeft = 4f },
            };
            timeField.RegisterValueChangedCallback(evt => TimeEntered?.Invoke(Mathf.Max(0f, evt.newValue)));
            Add(timeField);

            durationLabel = TweenUi.Dim("/ 0.00s");
            durationLabel.style.marginRight = 6f;
            Add(durationLabel);

            loopToggle = new Toggle
            {
                tooltip = "Repeat the preview when it reaches the end. Preview only: the animation's own Loops are unchanged.",
                style = { marginRight = 0f },
            };
            loopToggle.RegisterValueChangedCallback(evt => LoopChanged?.Invoke(evt.newValue));
            Add(loopToggle);
            Add(Caption("Loop"));

            var choices = new List<string>();
            for (var i = 0; i < Speeds.Length; i++) choices.Add(FormatSpeed(Speeds[i]));

            speedField = new DropdownField(choices, 3)
            {
                tooltip = "Preview speed, on top of the animation's own Playback Speed. Preview only.",
                style = { width = 52f, marginLeft = 2f, marginRight = 4f },
            };
            speedField.RegisterValueChangedCallback(evt =>
            {
                var index = choices.IndexOf(evt.newValue);
                if (index >= 0) SpeedChanged?.Invoke(Speeds[index]);
            });
            Add(speedField);

            autoToggle = new Toggle
            {
                tooltip = "While previewing, follow the selected clip and show edits as you make them",
                style = { marginRight = 0f },
            };
            autoToggle.RegisterValueChangedCallback(evt => AutoChanged?.Invoke(evt.newValue));
            Add(autoToggle);
            Add(Caption("Auto"));

            Add(new VisualElement { style = { flexGrow = 1f } });

            statusButton = new Button(() => StatusClicked?.Invoke())
            {
                tooltip = "Whether every step finds something to animate. Click for details and fixes.",
                style =
                {
                    height = 20f,
                    fontSize = 10f,
                    marginLeft = 4f,
                    backgroundColor = new StyleColor(Color.clear),
                },
            };
            TweenTimelineStyles.SetBorder(statusButton, 0f, Color.clear);
            Add(statusButton);
        }

        static Label Caption(string text)
        {
            var label = TweenUi.Dim(text);
            label.style.marginRight = 4f;
            return label;
        }

        static string FormatSpeed(float speed)
        {
            return speed.ToString(speed < 1f ? "0.##" : "0") + "×";
        }

        /// <summary>Reflects the preview's state.</summary>
        public void Sync(bool active, bool isPlaying, float time, float duration)
        {
            if (playing != isPlaying)
            {
                playing = isPlaying;
                TweenUi.SetIcon(playButton, isPlaying ? "PauseButton" : "PlayButton", isPlaying ? "Pause" : "Play");
            }

            stopButton.SetEnabled(active);

            // Leave a field the author is typing in alone.
            if (timeField.focusController?.focusedElement != timeField)
            {
                timeField.SetValueWithoutNotify((float)Math.Round(time, 3));
            }

            durationLabel.text = "/ " + duration.ToString("0.00") + "s";
        }

        public void SetPlayEnabled(bool enabled) => playButton.SetEnabled(enabled);

        public void SetLoop(bool value) => loopToggle.SetValueWithoutNotify(value);

        public void SetAuto(bool value) => autoToggle.SetValueWithoutNotify(value);

        /// <summary>Shows the binding summary, coloured by whether anything is failing.</summary>
        public void SetStatus(TweenBindingReport report)
        {
            if (report == null)
            {
                statusButton.style.display = DisplayStyle.None;
                return;
            }

            statusButton.style.display = DisplayStyle.Flex;
            statusButton.text = report.Summary + (report.Issues.Count > 0 ? "  ▾" : string.Empty);
            statusButton.style.color = report.Failing > 0
                ? TweenTimelineStyles.Warning
                : report.HasTarget && report.Binding > 0 ? TweenTimelineStyles.Ok : TweenTimelineStyles.RulerText;
        }
    }
}
