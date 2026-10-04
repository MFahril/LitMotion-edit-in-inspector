using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UIElements;

namespace LitMotion.TweenEditor.Editor
{
    /// <summary>
    /// Draws a <see cref="TweenStep"/>, showing only the fields its current type actually uses.
    /// </summary>
    /// <remarks>
    /// The flat step model carries every type's payload in one object, so without filtering the
    /// inspector would show a Fade step fields for jump power and scramble mode. Visibility is
    /// driven off a single table in <see cref="TweenStepFields"/> so the rules stay in one place.
    /// </remarks>
    [CustomPropertyDrawer(typeof(TweenStep))]
    public sealed class TweenStepDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var typeProperty = property.FindPropertyRelative("Type");
            var easeProperty = property.FindPropertyRelative("Ease");
            var loopsProperty = property.FindPropertyRelative("Loops");
            var delayProperty = property.FindPropertyRelative("Delay");
            var fromCurrentProperty = property.FindPropertyRelative("FromCurrent");
            var cameraProperty = property.FindPropertyRelative("CameraProperty");
            var fromOffsetProperty = property.FindPropertyRelative("FromOffset");
            var materialKindProperty = property.FindPropertyRelative("MaterialPropertyKind");
            var tmpChannelProperty = property.FindPropertyRelative("TMPCharChannel");
            var scrambleModeProperty = property.FindPropertyRelative("ScrambleMode");
            var characterIndexProperty = property.FindPropertyRelative("CharacterIndex");
            var callbackProperty = property.FindPropertyRelative("OnCallback");
            var progressProperty = property.FindPropertyRelative("OnProgress");

            var startTimeProperty = property.FindPropertyRelative("StartTime");
            var durationProperty = property.FindPropertyRelative("Duration");
            var labelProperty = property.FindPropertyRelative("Label");
            var enabledProperty = property.FindPropertyRelative("Enabled");

            // A collapsible step keeps a long timeline scannable; the header carries enough to
            // identify a step without expanding it.
            var foldout = new Foldout { value = false };
            var root = foldout.contentContainer;
            root.AddToClassList("lmte-step");

            // --- Header: enabled, type, label ---
            var header = Row();
            header.Add(Field(property, "Enabled", 64f));
            header.Add(Grow(Field(property, "Type")));
            root.Add(header);
            root.Add(Field(property, "Label"));

            root.Add(Separator());

            // --- Target ---
            var target = Field(property, "Target");
            root.Add(target);

            var propertyName = Field(property, "PropertyName");
            root.Add(propertyName);

            // --- Timing ---
            var timing = Row();
            timing.Add(Grow(Field(property, "StartTime")));
            var duration = Grow(Field(property, "Duration"));
            timing.Add(duration);
            root.Add(timing);

            var delayRow = Row();
            delayRow.Add(Grow(Field(property, "Delay")));
            var delayType = Grow(Field(property, "DelayType"));
            delayRow.Add(delayType);
            root.Add(delayRow);

            // --- Easing ---
            var ease = Field(property, "Ease");
            root.Add(ease);
            var customCurve = Field(property, "CustomCurve");
            root.Add(customCurve);

            // --- Looping ---
            var loopRow = Row();
            loopRow.Add(Grow(Field(property, "Loops")));
            var loopType = Grow(Field(property, "LoopType"));
            loopRow.Add(loopType);
            root.Add(loopRow);

            var infiniteWarning = new HelpBox(
                "A step cannot loop forever inside a sequence; this will play once. Use the " +
                "animation's own Loops setting to repeat indefinitely.",
                HelpBoxMessageType.Warning);
            root.Add(infiniteWarning);

            root.Add(Separator());

            // --- Values ---
            var fromCurrent = Field(property, "FromCurrent");
            root.Add(fromCurrent);
            var relative = Field(property, "Relative");
            root.Add(relative);
            var fromOffset = Field(property, "FromOffset");
            root.Add(fromOffset);

            var from = Field(property, "From", label: "From");
            var to = Field(property, "To", label: "To");
            var fromColor = Field(property, "FromColor", label: "From");
            var toColor = Field(property, "ToColor", label: "To");
            root.Add(from);
            root.Add(fromColor);
            root.Add(to);
            root.Add(toColor);

            // --- Per-type sub-options ---
            var space = Field(property, "Space");
            var axis = Field(property, "Axis");
            var uniformScale = Field(property, "UniformScale");
            var channel = Field(property, "Channel");
            var anchorTarget = Field(property, "AnchorTarget");
            var cameraPropertyField = Field(property, "CameraProperty");
            var materialKind = Field(property, "MaterialPropertyKind");
            var tmpChannel = Field(property, "TMPCharChannel");
            var characterIndex = Field(property, "CharacterIndex");
            var frequency = Field(property, "Frequency");
            var damping = Field(property, "DampingRatio");
            var randomSeed = Field(property, "RandomSeed");
            var jumpCount = Field(property, "JumpCount");
            var jumpPower = Field(property, "JumpPower");
            var stagger = Field(property, "Stagger");
            var textFormat = Field(property, "TextFormat");
            var textUnit = Field(property, "TextUnit");
            var scrambleMode = Field(property, "ScrambleMode");
            var scrambleChars = Field(property, "ScrambleChars");
            var richText = Field(property, "RichText");
            var targetText = Field(property, "TargetText");
            var onCallback = Field(property, "OnCallback");
            var onProgress = Field(property, "OnProgress");
            var extensionId = Field(property, "ExtensionId", label: "Extension");

            foreach (var element in new[]
                     {
                         extensionId, space, axis, uniformScale, channel, anchorTarget, cameraPropertyField,
                         materialKind, tmpChannel, characterIndex, stagger, frequency, damping,
                         randomSeed, jumpCount, jumpPower, textFormat, textUnit, scrambleMode,
                         scrambleChars, richText, targetText, onCallback, onProgress,
                     })
            {
                root.Add(element);
            }

            // Persistent UnityEvent listeners are created as Runtime Only, which means an
            // authored callback silently does nothing while previewing. Surfacing that here --
            // with the one-click fix -- is the difference between a working feature and one
            // that reads as broken.
            var eventStateWarning = new HelpBox(
                "This event's listeners are set to Runtime Only, so they will not fire while " +
                "previewing in the editor.",
                HelpBoxMessageType.Info);
            var enableInEditor = new Button { text = "Enable in editor too" };
            root.Add(eventStateWarning);
            root.Add(enableInEditor);

            void RefreshHeader()
            {
                if (typeProperty == null) return;

                var type = (TweenType)typeProperty.intValue;
                var name = string.IsNullOrWhiteSpace(labelProperty?.stringValue)
                    ? type.ToString()
                    : labelProperty.stringValue;

                var start = startTimeProperty?.floatValue ?? 0f;
                var length = durationProperty?.floatValue ?? 0f;

                foldout.text = TweenBindingResolver.IsNonBinding(type)
                    ? $"{name}   @ {start:0.##}s"
                    : $"{name}   {start:0.##}s - {start + length:0.##}s";

                // Dim a disabled step so it reads as inert at a glance.
                var enabled = enabledProperty == null || enabledProperty.boolValue;
                foldout.style.opacity = enabled ? 1f : 0.5f;
            }

            void Refresh()
            {
                if (typeProperty == null) return;

                RefreshHeader();

                var type = (TweenType)typeProperty.intValue;
                var fields = TweenStepFields.For(type);
                var isColor = TweenStepFields.UsesColor(type, cameraProperty, materialKindProperty,
                    tmpChannelProperty);

                Show(target, fields.Target);
                Show(propertyName, fields.PropertyName);
                Show(extensionId, fields.ExtensionId);
                Show(duration, fields.Duration);
                Show(delayRow, fields.Duration);
                Show(delayType, delayProperty != null && delayProperty.floatValue > 0f);
                Show(ease, fields.Ease);
                Show(customCurve, fields.Ease && easeProperty != null
                                               && easeProperty.intValue == (int)Ease.CustomAnimationCurve);
                Show(loopRow, fields.Duration);
                Show(loopType, loopsProperty != null && loopsProperty.intValue != 1);
                Show(infiniteWarning, loopsProperty != null && loopsProperty.intValue < 0);

                var showValues = fields.Values;
                var startFromCurrent = fields.SupportsFromCurrent
                                       && fromCurrentProperty != null
                                       && fromCurrentProperty.boolValue;

                // FromOffset ends on the live value, so an end field would be ignored and a
                // "start from current" toggle is already implied.
                var offsetFrom = fields.SupportsFromCurrent && !fields.EndIsStrength
                                               && fromOffsetProperty != null
                                               && fromOffsetProperty.boolValue;

                // A scramble shows no From/To vectors but still chooses whether to grow out of
                // the text already on the component.
                Show(fromCurrent, fields.SupportsFromCurrent && !offsetFrom
                                                             && (showValues || fields.Scramble));
                Show(fromOffset, showValues && fields.SupportsFromCurrent && !fields.EndIsStrength);
                Show(relative, showValues && !fields.EndIsStrength && !offsetFrom);
                Show(from, showValues && !isColor && (!startFromCurrent || offsetFrom));
                Show(fromColor, showValues && isColor && (!startFromCurrent || offsetFrom));
                Show(to, showValues && !isColor && !offsetFrom);
                Show(toColor, showValues && isColor && !offsetFrom);

                // Punch and Shake read the end value as an oscillation strength, so labelling it
                // "To" would actively mislead.
                SetLabel(to, fields.EndIsStrength ? "Strength" : "To");
                SetLabel(from, offsetFrom ? "Offset" : "From");

                Show(space, fields.Space);
                Show(axis, fields.Axis);
                Show(uniformScale, fields.UniformScale);
                Show(channel, fields.Channel);
                Show(anchorTarget, fields.AnchorTarget);
                Show(cameraPropertyField, fields.CameraProperty);
                Show(materialKind, fields.PropertyName);
                Show(tmpChannel, fields.TMPCharacter);
                Show(characterIndex, fields.TMPCharacter);

                // Stagger only means something when the step covers every character.
                Show(stagger, fields.TMPCharacter
                              && characterIndexProperty != null
                              && characterIndexProperty.intValue < 0);

                Show(frequency, fields.Vibration);
                Show(damping, fields.Vibration);

                // A seeded scramble reproduces the same character soup every run, same as Shake.
                Show(randomSeed, fields.RandomSeed || fields.Scramble);

                Show(jumpCount, fields.Jump);
                Show(jumpPower, fields.Jump);
                Show(textFormat, fields.TextFormat);
                Show(textUnit, fields.TextUnit);
                Show(scrambleMode, fields.Scramble);
                Show(scrambleChars, fields.Scramble
                                    && scrambleModeProperty != null
                                    && scrambleModeProperty.intValue == (int)ScrambleMode.Custom);
                Show(richText, fields.Scramble);
                Show(targetText, fields.Scramble);
                Show(onCallback, fields.Callback);
                Show(onProgress, fields.Progress);

                RefreshEventState(fields);
            }

            void RefreshEventState(TweenStepFields fields)
            {
                var eventProperty = fields.Callback ? callbackProperty
                    : fields.Progress ? progressProperty
                    : null;

                var needsFix = eventProperty != null && HasRuntimeOnlyListener(eventProperty);
                Show(eventStateWarning, needsFix);
                Show(enableInEditor, needsFix);
            }

            enableInEditor.clicked += () =>
            {
                var type = typeProperty == null ? TweenType.Move : (TweenType)typeProperty.intValue;
                var fields = TweenStepFields.For(type);
                var eventProperty = fields.Callback ? callbackProperty
                    : fields.Progress ? progressProperty
                    : null;

                if (eventProperty == null) return;

                MakeListenersEditorAndRuntime(eventProperty);
                RefreshEventState(fields);
            };

            Refresh();

            // Re-filter whenever a field that gates visibility changes.
            TrackChanges(foldout, typeProperty, Refresh);
            TrackChanges(foldout, easeProperty, Refresh);
            TrackChanges(foldout, loopsProperty, Refresh);
            TrackChanges(foldout, delayProperty, Refresh);
            TrackChanges(foldout, fromCurrentProperty, Refresh);
            TrackChanges(foldout, fromOffsetProperty, Refresh);
            TrackChanges(foldout, cameraProperty, Refresh);
            TrackChanges(foldout, materialKindProperty, Refresh);
            TrackChanges(foldout, tmpChannelProperty, Refresh);
            TrackChanges(foldout, scrambleModeProperty, Refresh);
            TrackChanges(foldout, characterIndexProperty, Refresh);
            TrackChanges(foldout, callbackProperty, Refresh);
            TrackChanges(foldout, progressProperty, Refresh);

            // Header-only fields: no need to re-evaluate visibility for these.
            TrackChanges(foldout, labelProperty, RefreshHeader);
            TrackChanges(foldout, startTimeProperty, RefreshHeader);
            TrackChanges(foldout, durationProperty, RefreshHeader);
            TrackChanges(foldout, enabledProperty, RefreshHeader);

            return foldout;
        }

        /// <summary>
        /// True when the event has a persistent listener that will not fire in the editor.
        /// </summary>
        /// <remarks>
        /// Unity adds persistent listeners with a call state of Runtime Only. That is the right
        /// default for gameplay events, but it makes an authored Callback or Custom step appear
        /// dead during preview, because the editor simply skips the invoke.
        /// </remarks>
        internal static bool HasRuntimeOnlyListener(SerializedProperty eventProperty)
        {
            var calls = eventProperty?.FindPropertyRelative("m_PersistentCalls.m_Calls");
            if (calls == null || !calls.isArray) return false;

            for (var i = 0; i < calls.arraySize; i++)
            {
                var state = calls.GetArrayElementAtIndex(i).FindPropertyRelative("m_CallState");
                if (state != null && state.intValue == (int)UnityEventCallState.RuntimeOnly) return true;
            }

            return false;
        }

        /// <summary>Promotes every Runtime Only listener on the event to Editor And Runtime.</summary>
        internal static void MakeListenersEditorAndRuntime(SerializedProperty eventProperty)
        {
            var calls = eventProperty?.FindPropertyRelative("m_PersistentCalls.m_Calls");
            if (calls == null || !calls.isArray) return;

            for (var i = 0; i < calls.arraySize; i++)
            {
                var state = calls.GetArrayElementAtIndex(i).FindPropertyRelative("m_CallState");
                if (state == null) continue;
                if (state.intValue == (int)UnityEventCallState.Off) continue;

                state.intValue = (int)UnityEventCallState.EditorAndRuntime;
            }

            eventProperty.serializedObject.ApplyModifiedProperties();
        }

        static void TrackChanges(VisualElement root, SerializedProperty property, Action callback)
        {
            if (property == null) return;
            root.TrackPropertyValue(property, _ => callback());
        }

        static PropertyField Field(SerializedProperty property, string relativeName, float width = 0f,
            string label = null)
        {
            var relative = property.FindPropertyRelative(relativeName);
            var field = relative == null ? new PropertyField() : new PropertyField(relative);

            if (label != null) field.label = label;
            if (width > 0f) field.style.width = width;

            return field;
        }

        static VisualElement Row()
        {
            return new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    justifyContent = Justify.FlexStart,
                },
            };
        }

        static T Grow<T>(T element) where T : VisualElement
        {
            element.style.flexGrow = 1f;
            element.style.flexShrink = 1f;
            return element;
        }

        static VisualElement Separator()
        {
            return new VisualElement
            {
                style =
                {
                    height = 1f,
                    marginTop = 4f,
                    marginBottom = 4f,
                    backgroundColor = new StyleColor(new Color(0f, 0f, 0f, 0.2f)),
                },
            };
        }

        static void Show(VisualElement element, bool visible)
        {
            if (element == null) return;
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        static void SetLabel(PropertyField field, string label)
        {
            if (field == null) return;
            field.label = label;
        }
    }
}
