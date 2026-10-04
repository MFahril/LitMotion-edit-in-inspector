using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LitMotion.TweenEditor.Editor
{
    /// <summary>
    /// Picks a MaterialProperty step's shader property from the material it will actually write
    /// to, instead of asking the author to type the name from memory.
    /// </summary>
    /// <remarks>
    /// Typing <c>_BaseColor</c> correctly requires knowing that URP renamed it from
    /// <c>_Color</c>, that it is a colour rather than a vector, and that a typo produces a step
    /// that silently refuses to build. The shader already knows all three, so this reads the
    /// property list off it and sets the step's value kind to match the one that was chosen.
    ///
    /// The text field stays, because a property can legitimately exist outside the shader's
    /// declared list -- set by another script or a material variant -- and because the material
    /// may not resolve at all while the scene is still being assembled.
    /// </remarks>
    internal sealed class TweenShaderPropertyField : VisualElement
    {
        readonly TextField nameField;
        readonly Button pickButton;
        readonly Label statusLabel;

        SerializedProperty stepProperty;
        TweenStep step;

        /// <summary>Supplies the GameObject unresolved steps fall back to.</summary>
        public Func<GameObject> TargetProvider { get; set; }

        /// <summary>Raised after the property name or kind changes.</summary>
        public event Action Changed;

        public TweenShaderPropertyField()
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            Add(row);

            // Delayed, so typing a property name is one undo entry on Enter rather than one per
            // keystroke.
            nameField = new TextField("Property")
            {
                isDelayed = true,
                style = { flexGrow = 1f, minWidth = 0f },
            };
            nameField.RegisterValueChangedCallback(evt => Write(evt.newValue, null));
            row.Add(nameField);

            pickButton = new Button(ShowMenu)
            {
                text = "▾",
                tooltip = "Choose a property from the target's shader",
                style = { width = 20f, marginLeft = 1f, paddingLeft = 0f, paddingRight = 0f },
            };
            row.Add(pickButton);

            statusLabel = new Label
            {
                pickingMode = PickingMode.Ignore,
                style = { fontSize = 9f, color = TweenTimelineStyles.RulerText, marginLeft = 54f },
            };
            Add(statusLabel);
        }

        /// <summary>Points the control at a step.</summary>
        public void Bind(SerializedProperty property, TweenStep target)
        {
            stepProperty = property;
            step = target;

            var nameProperty = property?.FindPropertyRelative("PropertyName");
            nameField.SetValueWithoutNotify(nameProperty == null ? string.Empty : nameProperty.stringValue);

            Refresh();
        }

        /// <summary>Re-resolves the material and updates the status line.</summary>
        public void Refresh()
        {
            var material = TweenChannelInfo.ResolveMaterial(step, TargetProvider?.Invoke());
            pickButton.SetEnabled(material != null);

            if (material == null)
            {
                statusLabel.text = "No material resolved yet. Assign a Renderer, a Graphic with "
                                   + "its own material, or a Material asset as the target.";
                return;
            }

            var name = nameField.value;
            if (string.IsNullOrWhiteSpace(name))
            {
                statusLabel.text = material.shader == null
                    ? material.name
                    : material.name + " — " + material.shader.name;
                return;
            }

            statusLabel.text = material.HasProperty(name)
                ? material.name + " has " + name
                : material.name + " has no property called " + name;
        }

        void ShowMenu()
        {
            var material = TweenChannelInfo.ResolveMaterial(step, TargetProvider?.Invoke());
            var shader = material == null ? null : material.shader;
            if (shader == null) return;

            var menu = new GenericMenu();
            var current = nameField.value;
            var count = shader.GetPropertyCount();
            var added = 0;

            for (var i = 0; i < count; i++)
            {
                var type = shader.GetPropertyType(i);
                var kind = KindOf(type);
                if (kind == null) continue;

                var name = shader.GetPropertyName(i);
                var label = GroupOf(kind.Value) + "/" + name;
                var captured = name;
                var capturedKind = kind.Value;

                menu.AddItem(new GUIContent(label), current == name, () => Write(captured, capturedKind));
                added++;
            }

            if (added == 0) menu.AddDisabledItem(new GUIContent("No animatable properties on this shader"));

            menu.ShowAsContext();
        }

        /// <summary>
        /// Maps a shader property type onto the step's value kind, or null when the type cannot
        /// be tweened (textures and integer-only properties).
        /// </summary>
        static TweenMaterialPropertyKind? KindOf(UnityEngine.Rendering.ShaderPropertyType type)
        {
            switch (type)
            {
                case UnityEngine.Rendering.ShaderPropertyType.Color:
                    return TweenMaterialPropertyKind.Color;
                case UnityEngine.Rendering.ShaderPropertyType.Vector:
                    return TweenMaterialPropertyKind.Vector;
                case UnityEngine.Rendering.ShaderPropertyType.Float:
                case UnityEngine.Rendering.ShaderPropertyType.Range:
                    return TweenMaterialPropertyKind.Float;
                default:
                    return null;
            }
        }

        static string GroupOf(TweenMaterialPropertyKind kind)
        {
            switch (kind)
            {
                case TweenMaterialPropertyKind.Color: return "Color";
                case TweenMaterialPropertyKind.Vector: return "Vector";
                default: return "Float";
            }
        }

        /// <summary>
        /// Writes the name and, when it came from the shader, the matching value kind -- so
        /// picking a colour property cannot leave the step interpolating it as a float.
        /// </summary>
        void Write(string name, TweenMaterialPropertyKind? kind)
        {
            if (stepProperty == null) return;

            var nameProperty = stepProperty.FindPropertyRelative("PropertyName");
            if (nameProperty == null) return;

            nameProperty.stringValue = name ?? string.Empty;

            if (kind.HasValue)
            {
                var kindProperty = stepProperty.FindPropertyRelative("MaterialPropertyKind");
                if (kindProperty != null) kindProperty.intValue = (int)kind.Value;
            }

            stepProperty.serializedObject.ApplyModifiedProperties();

            nameField.SetValueWithoutNotify(nameProperty.stringValue);
            Refresh();
            Changed?.Invoke();
        }
    }
}
