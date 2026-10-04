using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LitMotion.TweenEditor.Editor
{
    /// <summary>
    /// The axis mask as toggle chips, one per component the channel actually has.
    /// </summary>
    /// <remarks>
    /// A flags enum renders as a dropdown of combinations ("XY", "XYZ", "Mixed..."), which
    /// makes a two-click job out of "leave Y alone". Three chips say the same thing in one
    /// click and show the current state without being opened.
    ///
    /// Only the components the channel uses are shown: a Vector2 channel has no Z to mask, and
    /// a float or colour channel ignores masking entirely, so the control hides itself.
    /// </remarks>
    internal sealed class TweenAxisField : VisualElement
    {
        static readonly TweenAxis[] Flags = { TweenAxis.X, TweenAxis.Y, TweenAxis.Z, TweenAxis.W };

        readonly Button[] chips = new Button[Flags.Length];
        readonly Label hint;

        SerializedProperty axisProperty;
        int componentCount;

        /// <summary>Raised after the mask changes.</summary>
        public event Action Changed;

        public TweenAxisField()
        {
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;
            style.marginTop = 2f;
            style.marginBottom = 2f;

            Add(new Label("Axes")
            {
                tooltip = "Components this step writes. Unchecked components keep their live value.",
                style = { fontSize = 11f, minWidth = 52f, unityTextAlign = TextAnchor.MiddleLeft },
            });

            for (var i = 0; i < Flags.Length; i++)
            {
                var flag = Flags[i];
                var chip = new Button(() => Toggle(flag))
                {
                    text = flag.ToString(),
                    tooltip = "Animate " + flag + ". Off: " + flag + " keeps its live value while the rest animate.",
                    style =
                    {
                        width = 26f,
                        fontSize = 10f,
                        marginLeft = 0f,
                        marginRight = 2f,
                        paddingLeft = 0f,
                        paddingRight = 0f,
                    },
                };

                chips[i] = chip;
                Add(chip);
            }

            hint = new Label
            {
                pickingMode = PickingMode.Ignore,
                style = { fontSize = 9f, color = TweenTimelineStyles.RulerText, marginLeft = 4f },
            };
            Add(hint);
        }

        /// <summary>Points the control at a step's axis mask, for a channel of the given shape.</summary>
        public void Bind(SerializedProperty property, TweenValueShape shape, Func<int, string> componentLabel)
        {
            axisProperty = property?.FindPropertyRelative("Axis");

            componentCount = shape switch
            {
                TweenValueShape.Vector2 => 2,
                TweenValueShape.Vector3 => 3,
                _ => 0,
            };

            for (var i = 0; i < chips.Length; i++)
            {
                var visible = i < componentCount;
                chips[i].style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

                if (visible && componentLabel != null)
                {
                    var text = componentLabel(i);
                    chips[i].text = string.IsNullOrEmpty(text) ? Flags[i].ToString() : text;
                }

                chips[i].tooltip = "Animate " + chips[i].text + ". Off: " + chips[i].text
                                   + " keeps its live value while the rest animate.";
            }

            Refresh();
        }

        /// <summary>True when this channel has components worth masking.</summary>
        public bool IsApplicable => componentCount > 0 && axisProperty != null;

        /// <summary>Re-reads the mask and restyles the chips.</summary>
        public void Refresh()
        {
            if (axisProperty == null) return;

            var mask = (TweenAxis)axisProperty.intValue;
            var any = false;

            for (var i = 0; i < componentCount; i++)
            {
                var on = (mask & Flags[i]) != 0;
                any |= on;

                chips[i].style.backgroundColor = on
                    ? TweenTimelineStyles.SelectionOutline
                    : StyleKeyword.Null;
                chips[i].style.unityFontStyleAndWeight = on ? FontStyle.Bold : FontStyle.Normal;
            }

            // An empty mask writes nothing, so the builder substitutes XYZ. Say so rather than
            // letting the step look disabled.
            hint.text = any ? "" : "none selected, all axes will animate";
        }

        void Toggle(TweenAxis flag)
        {
            if (axisProperty == null) return;

            var mask = (TweenAxis)axisProperty.intValue;
            mask = (mask & flag) != 0 ? mask & ~flag : mask | flag;

            axisProperty.intValue = (int)mask;
            axisProperty.serializedObject.ApplyModifiedProperties();

            Refresh();
            Changed?.Invoke();
        }
    }
}
