using LitMotion.TweenEditor.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LitMotion.TweenEditor.Samples.Editor
{
    /// <summary>
    /// A worked example of a per-extension inspector section: shows the light's current
    /// intensity and offers to flash it, under the step's standard fields.
    /// </summary>
    [TweenExtensionInspector("com.litmotion.samples.light-intensity")]
    public sealed class LightIntensityInspector : TweenExtensionInspector
    {
        public override VisualElement CreateInspectorGUI(SerializedProperty step, Object target)
        {
            var root = new VisualElement();

            var light = target as Light;
            root.Add(new Label(light == null
                ? "No Light resolved yet."
                : light.name + " is at intensity " + light.intensity.ToString("0.##") + " right now.")
            {
                style = { fontSize = 10f, whiteSpace = WhiteSpace.Normal },
            });

            // Writes go through the serialized property, never the step object, so undo works.
            root.Add(new Button(() =>
            {
                var to = step.FindPropertyRelative("To");
                var vector = to.vector4Value;
                vector.x = light == null ? 4f : light.intensity * 4f;
                to.vector4Value = vector;
                step.serializedObject.ApplyModifiedProperties();
                NotifyChanged();
            })
            {
                text = "Flash ×4",
                tooltip = "Set the end value to four times the light's current intensity",
                style = { alignSelf = Align.FlexStart },
            });

            return root;
        }
    }
}
