using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace LitMotion.TweenEditor.Editor
{
    /// <summary>
    /// Every timeline shortcut, in one table, so the cheat-sheet cannot drift from the keys.
    /// </summary>
    internal static class TweenKeymap
    {
        /// <summary>One shortcut.</summary>
        public readonly struct Entry
        {
            public Entry(string keys, string action)
            {
                Keys = keys;
                Action = action;
            }

            public string Keys { get; }
            public string Action { get; }
        }

        /// <summary>Keyboard shortcuts, active while the timeline has focus.</summary>
        public static readonly IReadOnlyList<Entry> Keys = new[]
        {
            new Entry("Space", "Play or pause the preview"),
            new Entry("← / →", "Nudge the selection one grid step (Shift: ten)"),
            new Entry("Ctrl+D", "Duplicate"),
            new Entry("Del / Backspace", "Delete"),
            new Entry("Ctrl+C / Ctrl+V", "Copy, and paste at the playhead"),
            new Entry("Ctrl+A", "Select every visible clip"),
            new Entry("S", "Split at the playhead"),
            new Entry("M", "Mute or unmute"),
            new Entry("F", "Zoom to fit"),
            new Entry("Home / End", "Scrub to the start or the end"),
            new Entry("Alt+↑ / Alt+↓", "Move the lane up or down"),
        };

        /// <summary>Pointer gestures, which are easy to miss because nothing on screen shows them.</summary>
        public static readonly IReadOnlyList<Entry> Gestures = new[]
        {
            new Entry("Drag a clip", "Move it; the edges trim"),
            new Entry("Ctrl/Shift+click", "Add to the selection"),
            new Entry("Drag empty space", "Rubber-band select"),
            new Entry("Wheel", "Zoom, anchored at the cursor"),
            new Entry("Shift+wheel", "Pan"),
            new Entry("Right-click", "Every command for a clip or the background"),
            new Entry("Click the ease curve", "Open the ease gallery"),
            new Entry("Double-click a chip", "Rename the animation"),
            new Entry("Drop a preset", "On the animation chips, adds it as a new animation"),
        };

        /// <summary>A panel listing both tables, for the window's help toggle and the hint strip.</summary>
        public static VisualElement BuildSheet()
        {
            var sheet = new VisualElement
            {
                style =
                {
                    paddingLeft = 6f,
                    paddingRight = 6f,
                    paddingTop = 4f,
                    paddingBottom = 4f,
                    marginBottom = 4f,
                    backgroundColor = TweenTimelineStyles.LaneBackground,
                },
            };
            TweenTimelineStyles.SetBorder(sheet, 1f, TweenTimelineStyles.Border);
            TweenTimelineStyles.SetRadius(sheet, 3f);

            AddSection(sheet, "Keys (timeline focused)", Keys);
            AddSection(sheet, "Pointer", Gestures);
            return sheet;
        }

        static void AddSection(VisualElement sheet, string title, IReadOnlyList<Entry> entries)
        {
            sheet.Add(new Label(title)
            {
                style = { unityFontStyleAndWeight = FontStyle.Bold, fontSize = 10f, marginTop = 2f, marginBottom = 2f },
            });

            for (var i = 0; i < entries.Count; i++)
            {
                var row = new VisualElement { style = { flexDirection = FlexDirection.Row } };
                row.Add(new Label(entries[i].Keys)
                {
                    style = { width = 120f, flexShrink = 0f, fontSize = 10f, unityFontStyleAndWeight = FontStyle.Bold },
                });
                row.Add(new Label(entries[i].Action)
                {
                    style = { fontSize = 10f, flexShrink = 1f, whiteSpace = WhiteSpace.Normal },
                });
                sheet.Add(row);
            }
        }
    }
}
