using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace LitMotion.TweenEditor.Editor
{
    /// <summary>
    /// One endpoint of a step, drawn with the widget its channel actually needs.
    /// </summary>
    /// <remarks>
    /// The step model stores every endpoint as a <c>Vector4</c> (or a <c>Color</c>), which is
    /// what lets one flat type carry every tween. Shown raw that is hostile: a Move step asks
    /// "where to where" and gets four numbers, the fourth meaningless. This element asks
    /// <see cref="TweenChannelInfo"/> what the channel really is and draws a float, a Vector2,
    /// a Vector3, a Vector4 or a colour swatch accordingly, relabelling the components to match
    /// (X/Y/Z, R/G/B/A, W/H).
    ///
    /// Floats whose channel is normalized get a 0-1 slider rather than a free number field,
    /// because an alpha of 7 is never what anyone meant.
    ///
    /// Vector2 and Vector3 cannot bind straight to a <c>Vector4</c> property, so those two are
    /// synced by hand: writes preserve the components the widget does not show, and a
    /// <c>TrackPropertyValue</c> pulls external changes (undo, another inspector) back in.
    /// </remarks>
    internal sealed class TweenValueField : VisualElement
    {
        /// <summary>Which end of the step this field edits.</summary>
        public enum Endpoint
        {
            From,
            To,
        }

        readonly VisualElement fieldHost;
        readonly Label unitLabel;
        readonly Button grabButton;
        readonly Button applyButton;

        SerializedProperty stepProperty;
        SerializedProperty valueProperty;
        TweenStep step;
        TweenChannelInfo info;
        Endpoint endpoint;
        bool syncing;

        /// <summary>Supplies the GameObject unresolved steps fall back to.</summary>
        public Func<GameObject> TargetProvider { get; set; }

        /// <summary>Raised after this field changes the value, so the owner can re-preview.</summary>
        public event Action Changed;

        public TweenValueField()
        {
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;

            fieldHost = new VisualElement { style = { flexGrow = 1f, flexShrink = 1f, minWidth = 0f } };
            Add(fieldHost);

            unitLabel = new Label
            {
                pickingMode = PickingMode.Ignore,
                style =
                {
                    fontSize = 9f,
                    color = TweenTimelineStyles.RulerText,
                    marginLeft = 3f,
                    minWidth = 20f,
                    unityTextAlign = TextAnchor.MiddleLeft,
                },
            };
            Add(unitLabel);

            grabButton = IconButton("⊕", "Grab the target's current value into this field", Grab);
            applyButton = IconButton("↗", "Push this value onto the target so you can see it", Apply);
            Add(grabButton);
            Add(applyButton);
        }

        static Button IconButton(string text, string tooltip, Action action)
        {
            return new Button(action)
            {
                text = text,
                tooltip = tooltip,
                style =
                {
                    width = 20f,
                    marginLeft = 1f,
                    marginRight = 0f,
                    paddingLeft = 0f,
                    paddingRight = 0f,
                    fontSize = 11f,
                },
            };
        }

        /// <summary>Points this field at one endpoint of a step and rebuilds its widget.</summary>
        public void Bind(SerializedProperty property, TweenStep target, TweenChannelInfo channel,
            Endpoint which, string label)
        {
            stepProperty = property;
            step = target;
            info = channel;
            endpoint = which;

            var isColor = channel.Shape == TweenValueShape.Color;
            var name = which == Endpoint.From
                ? (isColor ? "FromColor" : "From")
                : (isColor ? "ToColor" : "To");

            valueProperty = property?.FindPropertyRelative(name);

            fieldHost.Clear();
            if (valueProperty == null) return;

            fieldHost.Add(BuildWidget(label));

            unitLabel.text = channel.Unit;
            unitLabel.style.display = string.IsNullOrEmpty(channel.Unit)
                ? DisplayStyle.None
                : DisplayStyle.Flex;

            // Nothing to grab from, and nothing to push onto, when the channel is write-only or
            // has no target: a disabled button that explains itself beats a button that lies.
            var canGrab = channel.SupportsRead;
            grabButton.SetEnabled(canGrab);
            grabButton.tooltip = canGrab
                ? "Grab the target's current value into this field"
                : channel.ChannelName + " cannot be read back, so there is nothing to grab";
            applyButton.SetEnabled(!TweenBindingResolver.IsNonBinding(target?.Type ?? TweenType.Move));
        }

        VisualElement BuildWidget(string label)
        {
            switch (info.Shape)
            {
                case TweenValueShape.Color:
                {
                    var field = new ColorField(label) { showAlpha = true };
                    field.BindProperty(valueProperty);
                    field.RegisterValueChangedCallback(_ => Changed?.Invoke());
                    return Stretch(field);
                }

                case TweenValueShape.Float:
                {
                    // A normalized channel gets a slider, which also communicates its range.
                    if (info.Unit == "0-1")
                    {
                        var slider = new Slider(label, 0f, 1f) { showInputField = true };
                        slider.BindProperty(valueProperty.FindPropertyRelative("x"));
                        slider.RegisterValueChangedCallback(_ => Changed?.Invoke());
                        return Stretch(slider);
                    }

                    var number = new FloatField(label);
                    number.BindProperty(valueProperty.FindPropertyRelative("x"));
                    number.RegisterValueChangedCallback(_ => Changed?.Invoke());
                    return Stretch(number);
                }

                case TweenValueShape.Vector4:
                {
                    var field = new Vector4Field(label);
                    field.BindProperty(valueProperty);
                    field.RegisterValueChangedCallback(_ => Changed?.Invoke());
                    Relabel(field);
                    return Stretch(field);
                }

                case TweenValueShape.Vector2:
                {
                    var field = new Vector2Field(label);
                    SyncFromProperty(field, v => new Vector2(v.x, v.y));
                    field.RegisterValueChangedCallback(evt => WriteComponents(evt.newValue.x, evt.newValue.y, null, null));
                    Relabel(field);
                    return Stretch(field);
                }

                default:
                {
                    var field = new Vector3Field(label);
                    SyncFromProperty(field, v => new Vector3(v.x, v.y, v.z));
                    field.RegisterValueChangedCallback(evt =>
                        WriteComponents(evt.newValue.x, evt.newValue.y, evt.newValue.z, null));
                    Relabel(field);
                    return Stretch(field);
                }
            }
        }

        static T Stretch<T>(T element) where T : VisualElement
        {
            element.style.flexGrow = 1f;
            element.style.flexShrink = 1f;
            element.style.minWidth = 0f;

            // Narrow hosts (the inspector is ~350px) need the label to give way first.
            var label = element.Q<Label>();
            if (label != null)
            {
                label.style.minWidth = 52f;
                label.style.flexShrink = 1f;
            }

            return element;
        }

        /// <summary>
        /// Renames a composite field's sub-labels to the channel's own component names.
        /// </summary>
        void Relabel(VisualElement field)
        {
            var parts = field.Query<FloatField>().ToList();
            for (var i = 0; i < parts.Count; i++)
            {
                var text = info.ComponentLabel(i);
                if (!string.IsNullOrEmpty(text)) parts[i].label = text;
            }
        }

        /// <summary>
        /// Mirrors the Vector4 property into a smaller widget, and keeps mirroring it when the
        /// property changes behind our back.
        /// </summary>
        void SyncFromProperty<TValue>(BaseField<TValue> field, Func<Vector4, TValue> convert)
        {
            field.SetValueWithoutNotify(convert(valueProperty.vector4Value));

            field.TrackPropertyValue(valueProperty, property =>
            {
                if (syncing) return;

                syncing = true;
                field.SetValueWithoutNotify(convert(property.vector4Value));
                syncing = false;
            });
        }

        /// <summary>
        /// Writes the components the widget shows, leaving the rest of the Vector4 alone.
        /// </summary>
        void WriteComponents(float? x, float? y, float? z, float? w)
        {
            if (valueProperty == null || syncing) return;

            var value = valueProperty.vector4Value;
            if (x.HasValue) value.x = x.Value;
            if (y.HasValue) value.y = y.Value;
            if (z.HasValue) value.z = z.Value;
            if (w.HasValue) value.w = w.Value;

            syncing = true;
            valueProperty.vector4Value = value;
            valueProperty.serializedObject.ApplyModifiedProperties();
            syncing = false;

            Changed?.Invoke();
        }

        void Grab()
        {
            if (step == null || valueProperty == null) return;

            var fallback = TargetProvider?.Invoke();
            if (!TweenChannelInfo.TryRead(step, fallback, out var live)) return;

            if (info.Shape == TweenValueShape.Color)
            {
                valueProperty.colorValue = live;
            }
            else
            {
                valueProperty.vector4Value = live;
            }

            valueProperty.serializedObject.ApplyModifiedProperties();

            // The widget may be hand-synced rather than bound, so rebuild rather than hope.
            Bind(stepProperty, step, info, endpoint, CurrentLabel());
            Changed?.Invoke();
        }

        void Apply()
        {
            if (step == null || valueProperty == null) return;

            var fallback = TargetProvider?.Invoke();
            var resolved = TweenBindingResolver.Resolve(step, fallback, out _);
            if (resolved == null) return;

            var value = info.Shape == TweenValueShape.Color
                ? (Vector4)valueProperty.colorValue
                : valueProperty.vector4Value;

            // Writing straight onto a scene object is a scene edit, so it has to be undoable
            // on its own -- this is not a preview and nothing will restore it.
            Undo.RecordObject(resolved, "Apply Tween Value");
            TweenChannelInfo.TryWrite(step, fallback, value);
        }

        string CurrentLabel()
        {
            var label = fieldHost.Q<Label>();
            return label == null ? string.Empty : label.text;
        }
    }
}
