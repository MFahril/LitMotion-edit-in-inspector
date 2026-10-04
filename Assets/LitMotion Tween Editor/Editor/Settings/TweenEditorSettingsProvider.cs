using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace LitMotion.TweenEditor.Editor
{
    /// <summary>
    /// <b>Preferences → LitMotion Tween Editor</b>.
    /// </summary>
    internal static class TweenEditorSettingsProvider
    {
        public const string Path = "Preferences/LitMotion Tween Editor";

        static readonly string[] Fields =
        {
            "defaultDuration", "defaultEase",
            "snapEnabled", "snapInterval", "snapPixelThreshold",
            "defaultPixelsPerSecond", "minPixelsPerSecond", "maxPixelsPerSecond",
            "trackHeight", "maxTimelineHeight",
            "autoPreview", "showHints",
            "customClipColors",
        };

        [SettingsProvider]
        public static SettingsProvider Create()
        {
            return new SettingsProvider(Path, SettingsScope.User)
            {
                label = "LitMotion Tween Editor",
                keywords = new HashSet<string>(new[]
                {
                    "tween", "litmotion", "timeline", "snap", "zoom", "ease", "preview", "clip", "colour", "color",
                }),
                activateHandler = (_, root) => Build(root),
            };
        }

        static void Build(VisualElement root)
        {
            var settings = TweenEditorSettings.instance;

            // A ScriptableSingleton is created NotEditable, which would grey out every field.
            settings.hideFlags &= ~HideFlags.NotEditable;

            var serialized = new SerializedObject(settings);

            var page = new ScrollView { style = { paddingLeft = 10f, paddingRight = 10f, paddingTop = 4f } };
            root.Add(page);

            page.Add(new Label("LitMotion Tween Editor")
            {
                style = { fontSize = 19f, unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 8f },
            });

            for (var i = 0; i < Fields.Length; i++)
            {
                var property = serialized.FindProperty(Fields[i]);
                if (property != null) page.Add(new PropertyField(property));
            }

            var colors = new VisualElement { style = { marginLeft = 12f } };
            page.Add(colors);
            BuildColorFields(colors, settings);

            page.Add(new Button(() =>
            {
                settings.ResetToDefaults();
                serialized.Update();
                colors.Clear();
                BuildColorFields(colors, settings);
            })
            {
                text = "Reset to Defaults",
                tooltip = "Put every setting on this page back to how the package ships",
                style = { alignSelf = Align.FlexStart, marginTop = 10f },
            });

            page.Bind(serialized);

            // Bound fields apply their own edits; this saves them and tells open editors.
            page.TrackSerializedObjectValue(serialized, _ =>
            {
                settings.Commit();
                colors.SetEnabled(settings.CustomClipColors);
            });

            colors.SetEnabled(settings.CustomClipColors);
        }

        static void BuildColorFields(VisualElement container, TweenEditorSettings settings)
        {
            foreach (TweenFamily family in Enum.GetValues(typeof(TweenFamily)))
            {
                var field = new ColorField(ObjectNames.NicifyVariableName(family.ToString()))
                {
                    value = settings.FamilyColorOverride(family) ?? TweenTimelineStyles.BuiltInFamilyColor(family),
                    showAlpha = false,
                    tooltip = "Clip colour for " + ObjectNames.NicifyVariableName(family.ToString()) + " steps",
                };

                var captured = family;
                field.RegisterValueChangedCallback(evt => settings.SetFamilyColor(captured, evt.newValue));
                container.Add(field);
            }
        }
    }
}
