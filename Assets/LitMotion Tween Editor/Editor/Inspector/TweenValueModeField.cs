using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LitMotion.TweenEditor.Editor
{
    /// <summary>How a step's two endpoints are interpreted.</summary>
    /// <remarks>
    /// The data model stores this as three independent booleans -- <c>FromCurrent</c>,
    /// <c>Relative</c> and <c>FromOffset</c> -- which is eight combinations, of which four are
    /// meaningful and the rest are contradictions an author can tick their way into. This enum
    /// is the four meaningful ones, presented as a single choice.
    /// </remarks>
    internal enum TweenValueMode
    {
        /// <summary>From an authored start value to an authored end value.</summary>
        Absolute = 0,

        /// <summary>From wherever the target is now, to an authored end value.</summary>
        FromCurrent = 1,

        /// <summary>From wherever the target is now, by an authored delta.</summary>
        Relative = 2,

        /// <summary>From the target's value plus an authored offset, back to the target's value.</summary>
        FromOffset = 3,
    }

    /// <summary>Maps between the mode enum and the three serialized booleans.</summary>
    internal static class TweenValueModes
    {
        /// <summary>Reads the mode a step's flags describe.</summary>
        /// <remarks>
        /// Order matters: FromOffset wins because the builder checks it first, and Relative
        /// outranks FromCurrent because a relative step is read as "by this much" whether or not
        /// FromCurrent happens to also be set.
        /// </remarks>
        public static TweenValueMode Of(bool fromCurrent, bool relative, bool fromOffset)
        {
            if (fromOffset) return TweenValueMode.FromOffset;
            if (relative) return TweenValueMode.Relative;
            return fromCurrent ? TweenValueMode.FromCurrent : TweenValueMode.Absolute;
        }

        /// <summary>Reads the mode a step is in.</summary>
        public static TweenValueMode Of(TweenStep step)
        {
            return step == null
                ? TweenValueMode.Absolute
                : Of(step.FromCurrent, step.Relative, step.FromOffset);
        }

        /// <summary>The flag combination a mode means.</summary>
        public static void Flags(TweenValueMode mode, out bool fromCurrent, out bool relative,
            out bool fromOffset)
        {
            switch (mode)
            {
                case TweenValueMode.FromCurrent:
                    fromCurrent = true;
                    relative = false;
                    fromOffset = false;
                    return;

                case TweenValueMode.Relative:
                    // Relative without FromCurrent is legal in the data but almost never meant:
                    // it offsets an authored start the author cannot see. Normalize to the
                    // reading everyone expects, "move by this much from where it is".
                    fromCurrent = true;
                    relative = true;
                    fromOffset = false;
                    return;

                case TweenValueMode.FromOffset:
                    fromCurrent = true;
                    relative = false;
                    fromOffset = true;
                    return;

                default:
                    fromCurrent = false;
                    relative = false;
                    fromOffset = false;
                    return;
            }
        }

        /// <summary>True when the start value is authored rather than read off the target.</summary>
        public static bool ShowsStartValue(TweenValueMode mode)
        {
            return mode is TweenValueMode.Absolute or TweenValueMode.FromOffset;
        }

        /// <summary>True when the end value is authored.</summary>
        public static bool ShowsEndValue(TweenValueMode mode)
        {
            return mode != TweenValueMode.FromOffset;
        }

        /// <summary>Label for the start field in this mode.</summary>
        public static string StartLabel(TweenValueMode mode)
        {
            return mode == TweenValueMode.FromOffset ? "Offset" : "From";
        }

        /// <summary>Label for the end field in this mode.</summary>
        public static string EndLabel(TweenValueMode mode, bool endIsStrength)
        {
            if (endIsStrength) return "Strength";
            return mode == TweenValueMode.Relative ? "By" : "To";
        }

        /// <summary>One-line explanation of what the mode will do, for the inspector.</summary>
        public static string Describe(TweenValueMode mode, string channelName)
        {
            var channel = string.IsNullOrEmpty(channelName) ? "the value" : channelName;

            switch (mode)
            {
                case TweenValueMode.FromCurrent:
                    return "Starts at the target's current " + channel + ".";
                case TweenValueMode.Relative:
                    return "Starts where the target is and moves by the amount below.";
                case TweenValueMode.FromOffset:
                    return "Starts offset from the target and lands back on it. Reusable across objects.";
                default:
                    return "Both ends are authored, so the target snaps to the start value.";
            }
        }
    }

    /// <summary>
    /// The four value modes as one segmented control, writing the three serialized booleans.
    /// </summary>
    internal sealed class TweenValueModeField : VisualElement
    {
        static readonly TweenValueMode[] Modes =
        {
            TweenValueMode.Absolute,
            TweenValueMode.FromCurrent,
            TweenValueMode.Relative,
            TweenValueMode.FromOffset,
        };

        static readonly string[] Labels = { "Absolute", "Current", "Relative", "Offset" };

        readonly Button[] buttons = new Button[Modes.Length];

        SerializedProperty stepProperty;

        /// <summary>Raised after the mode changes.</summary>
        public event Action<TweenValueMode> ModeChanged;

        public TweenValueModeField()
        {
            style.flexDirection = FlexDirection.Row;
            style.marginTop = 2f;
            style.marginBottom = 2f;

            var label = new Label("Mode")
            {
                style = { fontSize = 11f, minWidth = 52f, unityTextAlign = TextAnchor.MiddleLeft },
            };
            Add(label);

            for (var i = 0; i < Modes.Length; i++)
            {
                var mode = Modes[i];
                var button = new Button(() => Select(mode))
                {
                    text = Labels[i],
                    tooltip = TweenValueModes.Describe(mode, null),
                    style =
                    {
                        flexGrow = 1f,
                        fontSize = 10f,
                        marginLeft = 0f,
                        marginRight = 0f,
                        paddingLeft = 2f,
                        paddingRight = 2f,
                    },
                };

                buttons[i] = button;
                Add(button);
            }
        }

        /// <summary>Points the control at a step.</summary>
        public void Bind(SerializedProperty property)
        {
            stepProperty = property;
            Refresh();
        }

        /// <summary>Re-reads the current mode and restyles the segments.</summary>
        public void Refresh()
        {
            var current = CurrentMode();

            for (var i = 0; i < buttons.Length; i++)
            {
                var active = Modes[i] == current;
                buttons[i].style.backgroundColor = active
                    ? TweenTimelineStyles.SelectionOutline
                    : StyleKeyword.Null;
                buttons[i].style.unityFontStyleAndWeight = active ? FontStyle.Bold : FontStyle.Normal;
            }
        }

        TweenValueMode CurrentMode()
        {
            var fromCurrent = stepProperty?.FindPropertyRelative("FromCurrent");
            var relative = stepProperty?.FindPropertyRelative("Relative");
            var fromOffset = stepProperty?.FindPropertyRelative("FromOffset");

            if (fromCurrent == null || relative == null || fromOffset == null)
            {
                return TweenValueMode.Absolute;
            }

            return TweenValueModes.Of(fromCurrent.boolValue, relative.boolValue, fromOffset.boolValue);
        }

        void Select(TweenValueMode mode)
        {
            if (stepProperty == null) return;

            TweenValueModes.Flags(mode, out var fromCurrent, out var relative, out var fromOffset);

            stepProperty.FindPropertyRelative("FromCurrent").boolValue = fromCurrent;
            stepProperty.FindPropertyRelative("Relative").boolValue = relative;
            stepProperty.FindPropertyRelative("FromOffset").boolValue = fromOffset;
            stepProperty.serializedObject.ApplyModifiedProperties();

            Refresh();
            ModeChanged?.Invoke(mode);
        }
    }
}
