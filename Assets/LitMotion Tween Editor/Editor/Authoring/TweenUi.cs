using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace LitMotion.TweenEditor.Editor
{
    /// <summary>Small element builders shared by the authoring view's bars.</summary>
    internal static class TweenUi
    {
        /// <summary>
        /// A compact button showing a built-in icon, falling back to text when this editor
        /// version does not have the icon.
        /// </summary>
        public static Button IconButton(string iconName, string fallbackText, string tooltip, Action action)
        {
            var button = new Button(action)
            {
                tooltip = tooltip,
                style =
                {
                    marginLeft = 1f,
                    marginRight = 1f,
                    paddingLeft = 3f,
                    paddingRight = 3f,
                    minWidth = 22f,
                    height = 20f,
                    flexShrink = 0f,
                },
            };

            SetIcon(button, iconName, fallbackText);
            return button;
        }

        /// <summary>Swaps the icon on a button made by <see cref="IconButton"/>.</summary>
        public static void SetIcon(Button button, string iconName, string fallbackText)
        {
            var icon = string.IsNullOrEmpty(iconName) ? null : TweenTimelineStyles.Tool(iconName);

            button.Clear();
            button.text = string.Empty;

            if (icon == null)
            {
                button.text = fallbackText;
                return;
            }

            button.Add(new Image
            {
                image = icon,
                pickingMode = PickingMode.Ignore,
                scaleMode = ScaleMode.ScaleToFit,
                style = { width = 14f, height = 14f, alignSelf = Align.Center },
            });
        }

        /// <summary>A small grey label.</summary>
        public static Label Dim(string text, float fontSize = 10f)
        {
            return new Label(text)
            {
                style =
                {
                    fontSize = fontSize,
                    color = TweenTimelineStyles.RulerText,
                    unityTextAlign = TextAnchor.MiddleLeft,
                    whiteSpace = WhiteSpace.Normal,
                },
            };
        }

        /// <summary>A horizontal row that centres its children.</summary>
        public static VisualElement Row(bool wrap = false)
        {
            return new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    flexWrap = wrap ? Wrap.Wrap : Wrap.NoWrap,
                },
            };
        }

        /// <summary>A bordered box, for empty states and panels.</summary>
        public static VisualElement Box()
        {
            var box = new VisualElement
            {
                style =
                {
                    paddingLeft = 8f,
                    paddingRight = 8f,
                    paddingTop = 6f,
                    paddingBottom = 6f,
                    marginTop = 2f,
                    marginBottom = 4f,
                    backgroundColor = TweenTimelineStyles.LaneBackground,
                },
            };

            TweenTimelineStyles.SetBorder(box, 1f, TweenTimelineStyles.Border);
            TweenTimelineStyles.SetRadius(box, 3f);
            return box;
        }
    }
}
