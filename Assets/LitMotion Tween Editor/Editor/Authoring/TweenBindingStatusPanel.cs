using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace LitMotion.TweenEditor.Editor
{
    /// <summary>
    /// The binding problems behind the preview bar's summary, each with a way to find the step
    /// and, where there is one, a way to fix it.
    /// </summary>
    /// <remarks>
    /// Inline under the preview bar rather than a floating popover: it stays open while the
    /// author fixes things, and each fix re-checks and updates the list in place.
    /// </remarks>
    internal sealed class TweenBindingStatusPanel : VisualElement
    {
        bool expanded;

        /// <summary>Raised when the author asks to jump to a step.</summary>
        public event Action<int> StepRequested;

        /// <summary>Raised after a fix ran, so the owner can re-check and rebuild.</summary>
        public event Action FixApplied;

        public TweenBindingStatusPanel()
        {
            style.display = DisplayStyle.None;
        }

        /// <summary>Opens or closes the panel.</summary>
        public void Toggle()
        {
            expanded = !expanded;
            style.display = expanded && childCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>Rebuilds the rows from a fresh report.</summary>
        public void Show(TweenBindingReport report)
        {
            Clear();

            if (report == null || report.Issues.Count == 0)
            {
                style.display = DisplayStyle.None;
                return;
            }

            var box = TweenUi.Box();
            Add(box);

            for (var i = 0; i < report.Issues.Count; i++)
            {
                box.Add(BuildRow(report.Issues[i]));
            }

            style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
        }

        VisualElement BuildRow(TweenStepIssue issue)
        {
            var row = new VisualElement { style = { marginBottom = 4f } };

            var line = TweenUi.Row();
            line.style.alignItems = Align.FlexStart;

            var icon = issue.BlocksBinding ? TweenTimelineStyles.WarningIcon : TweenTimelineStyles.Tool("console.infoicon.sml");
            if (icon != null)
            {
                line.Add(new Image
                {
                    image = icon,
                    style = { width = 14f, height = 14f, flexShrink = 0f, marginRight = 4f },
                });
            }

            var message = new Label(issue.Message)
            {
                style = { whiteSpace = WhiteSpace.Normal, flexShrink = 1f, flexGrow = 1f, fontSize = 10f },
            };
            line.Add(message);
            row.Add(line);

            var actions = TweenUi.Row(true);
            actions.style.marginLeft = 18f;

            var index = issue.StepIndex;
            actions.Add(new Button(() => StepRequested?.Invoke(index))
            {
                text = "Select Step",
                tooltip = "Select this step on the timeline",
                style = { fontSize = 10f, height = 18f },
            });

            for (var f = 0; f < issue.Fixes.Count; f++)
            {
                var fix = issue.Fixes[f];
                actions.Add(new Button(() =>
                {
                    fix.Apply?.Invoke();
                    FixApplied?.Invoke();
                })
                {
                    text = fix.Label,
                    tooltip = fix.Tooltip,
                    style = { fontSize = 10f, height = 18f, unityFontStyleAndWeight = FontStyle.Bold },
                });
            }

            row.Add(actions);
            return row;
        }
    }
}
