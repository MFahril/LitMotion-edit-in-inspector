using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace LitMotion.TweenEditor.Editor
{
    /// <summary>
    /// Everything about the selected clip, laid out as the controls that clip actually has.
    /// </summary>
    /// <remarks>
    /// Replaces a single <c>PropertyField</c> over the whole step, which rendered a collapsed
    /// foldout of raw fields: a Move step's "where to where" came out as two Vector4s with a
    /// meaningless fourth component, and shake strength sat in a plain number box beside jump
    /// power that the type does not even use.
    ///
    /// Nothing is behind a foldout. One step is shown at a time, so everything it has fits, and
    /// a control you have to go looking for is a control you forget exists. What varies is
    /// *which* controls appear, driven by <see cref="TweenStepFields"/> -- the same table the
    /// old drawer used -- and by <see cref="TweenChannelInfo"/> for the value widgets.
    /// </remarks>
    internal sealed class TweenClipInspector : VisualElement
    {
        readonly Label headerLabel;
        readonly VisualElement body;

        readonly TweenValueModeField modeField = new();
        readonly TweenAxisField axisField = new();
        readonly TweenValueField fromField = new();
        readonly TweenValueField toField = new();
        readonly TweenShaderPropertyField shaderField = new();
        readonly EaseCurveElement easeCurve = new();

        SerializedProperty stepProperty;
        TweenStep step;
        bool binding;

        Label counterSample;
        Label scrambleSample;

        /// <summary>Supplies the GameObject unresolved steps fall back to.</summary>
        public Func<GameObject> TargetProvider { get; set; }

        /// <summary>Raised when a value changed in a way the preview should reflect.</summary>
        public event Action Changed;

        /// <summary>Raised when the author asks to preview this step on its own.</summary>
        public event Action SoloRequested;

        /// <summary>Raised after a fix button changed the scene, so binding badges can refresh.</summary>
        public event Action FixApplied;

        public TweenClipInspector()
        {
            style.marginTop = 2f;
            style.paddingTop = 4f;
            style.paddingBottom = 6f;
            style.paddingLeft = 6f;
            style.paddingRight = 6f;
            style.backgroundColor = TweenTimelineStyles.LaneBackground;
            TweenTimelineStyles.SetBorder(this, 1f, TweenTimelineStyles.Border);
            TweenTimelineStyles.SetRadius(this, 3f);

            headerLabel = new Label
            {
                style =
                {
                    fontSize = 11f,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    marginBottom = 2f,
                },
            };
            Add(headerLabel);

            body = new VisualElement();
            Add(body);

            fromField.Changed += () => Changed?.Invoke();
            toField.Changed += () =>
            {
                // The counter's sample prints the end value, so it follows it.
                RefreshSamples();
                Changed?.Invoke();
            };
            shaderField.Changed += Rebuild;
            axisField.Changed += () => Changed?.Invoke();
            modeField.ModeChanged += _ => Rebuild();

            // Registered once, not per rebuild: the graph is a long-lived element that gets
            // re-parented on every rebuild, so registering there would stack one handler per
            // rebuild and open a stack of pickers on the next click.
            easeCurve.RegisterCallback<PointerDownEvent>(_ => OpenEasePicker(easeCurve));
        }

        /// <summary>Shows a step, or clears the panel when given none.</summary>
        public void Bind(SerializedProperty property, TweenStep target)
        {
            stepProperty = property;
            step = target;
            Rebuild();
        }

        /// <summary>
        /// Moves the dot on the ease graph to where the preview currently is within this step.
        /// </summary>
        /// <param name="animationSeconds">
        /// Preview time measured from the start of the animation, or null when nothing is playing.
        /// </param>
        public void SetPreviewTime(float? animationSeconds)
        {
            if (step == null || !animationSeconds.HasValue)
            {
                easeCurve.SetPlayhead(null);
                return;
            }

            // Before the clip starts or after it ends there is no progress to show, rather than
            // a dot parked at either end pretending the step is mid-flight.
            var duration = Mathf.Max(0.0001f, step.Duration);
            var local = (animationSeconds.Value - step.StartTime - step.Delay) / duration;

            easeCurve.SetPlayhead(local is >= 0f and <= 1f ? local : null);
        }

        /// <summary>Re-reads the step and rebuilds every control.</summary>
        public void Rebuild()
        {
            body.Clear();
            counterSample = null;
            scrambleSample = null;

            if (stepProperty == null || step == null)
            {
                headerLabel.text = "No clip selected";
                body.Add(Hint("Click a clip on the timeline to edit it."));
                return;
            }

            var fallback = TargetProvider?.Invoke();
            var fields = TweenStepFields.For(step.Type);
            var info = TweenChannelInfo.Describe(step, fallback);

            fromField.TargetProvider = TargetProvider;
            toField.TargetProvider = TargetProvider;
            shaderField.TargetProvider = TargetProvider;

            headerLabel.text = step.DisplayName + "   —   " + Nice(step.Type);

            BuildIdentityRow();
            if (fields.ExtensionId) BuildExtensionRow();
            BuildStatus(fallback, fields);
            if (step.Type == TweenType.Custom) BuildCustomRestoreNote();
            if (fields.Target) BuildTargetRow(info);
            if (fields.PropertyName) BuildShaderRow();
            BuildValues(info, fields);
            BuildTiming(fields);
            if (fields.Ease) BuildEasing();
            BuildOptions(fields, info);
            if (fields.ExtensionId) BuildExtensionSection(fallback);
            BuildEvents(fields);

            BindFields();
        }

        /// <summary>
        /// Binds every field just built to the step.
        /// </summary>
        /// <remarks>
        /// Fields added to a hierarchy after it was bound are not bound automatically, so a
        /// rebuild has to bind its own -- otherwise the panel shows empty fields in any host that
        /// does not happen to re-bind it. A <c>PropertyField</c> also raises its change callback
        /// once, synchronously, when bound; several of those callbacks rebuild this panel, so
        /// without the guard a bind would recurse until the stack overflowed.
        /// </remarks>
        void BindFields()
        {
            binding = true;
            try
            {
                body.Bind(stepProperty.serializedObject);
            }
            finally
            {
                binding = false;
            }
        }

        /// <summary>
        /// True when a field's change callback is reporting a real edit, rather than a bind or a
        /// re-bind by the host replaying the value the field already had.
        /// </summary>
        bool IsRealEdit(SerializedProperty property, ref string lastSeen)
        {
            if (binding) return false;

            var now = ValueKey(property);
            if (now == lastSeen) return false;

            lastSeen = now;
            return true;
        }

        /// <summary>A comparable rendering of the simple property types the inspector tracks.</summary>
        static string ValueKey(SerializedProperty property)
        {
            if (property == null) return string.Empty;

            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.LayerMask:
                    return property.longValue.ToString();
                case SerializedPropertyType.Enum:
                    return property.intValue.ToString();
                case SerializedPropertyType.Boolean:
                    return property.boolValue ? "1" : "0";
                case SerializedPropertyType.Float:
                    return property.doubleValue.ToString("R");
                case SerializedPropertyType.String:
                    return property.stringValue ?? string.Empty;
                case SerializedPropertyType.ObjectReference:
                    return property.objectReferenceValue == null
                        ? "null"
                        : property.objectReferenceValue.GetHashCode().ToString();
                default:
                    // Anything richer is compared by its serialized hash, which changes with
                    // any content change and not otherwise.
                    return property.contentHash.ToString();
            }
        }

        // --- Sections ---

        void BuildIdentityRow()
        {
            var row = Row();

            var enabled = new PropertyField(stepProperty.FindPropertyRelative("Enabled"), string.Empty)
            {
                style = { width = 18f, flexShrink = 0f },
                tooltip = "Include this step when the animation plays",
            };
            row.Add(enabled);

            var type = new PropertyField(stepProperty.FindPropertyRelative("Type"), string.Empty)
            {
                style = { flexGrow = 1f, minWidth = 0f },
                tooltip = "Changing the type keeps the clip in place and re-reads the fields below",
            };
            row.Add(type);

            var label = new PropertyField(stepProperty.FindPropertyRelative("Label"), string.Empty)
            {
                style = { flexGrow = 1f, minWidth = 0f },
                tooltip = "Name shown on the timeline clip",
            };
            row.Add(label);

            var solo = new Button(() => SoloRequested?.Invoke())
            {
                text = "▶",
                tooltip = "Preview this step on its own",
                style = { width = 22f, flexShrink = 0f, paddingLeft = 0f, paddingRight = 0f },
            };
            row.Add(solo);

            body.Add(row);
        }

        /// <summary>
        /// Says why this step cannot bind, with a button for each unambiguous repair.
        /// </summary>
        void BuildStatus(GameObject fallback, TweenStepFields fields)
        {
            if (!fields.Target) return;
            if (TweenBindingStatus.Check(step, fallback, out var problem)) return;

            body.Add(new HelpBox(problem, HelpBoxMessageType.Warning)
            {
                style = { marginTop = 2f, marginBottom = 2f },
            });

            var fixes = TweenStepFixes.For(step, fallback);
            if (fixes.Count == 0) return;

            var row = Row();
            row.style.flexWrap = Wrap.Wrap;

            for (var i = 0; i < fixes.Count; i++)
            {
                var fix = fixes[i];
                row.Add(new Button(() =>
                {
                    fix.Apply?.Invoke();
                    Rebuild();
                    FixApplied?.Invoke();
                    Changed?.Invoke();
                })
                {
                    text = fix.Label,
                    tooltip = fix.Tooltip,
                    style = { unityFontStyleAndWeight = FontStyle.Bold },
                });
            }

            body.Add(row);
        }

        /// <summary>
        /// Puts the Custom-step restore limitation where it bites, rather than only in the docs.
        /// </summary>
        void BuildCustomRestoreNote()
        {
            body.Add(new HelpBox(
                "A Custom step writes through its event, which can touch anything, so stopping a " +
                "preview cannot put back what it changed. Whatever the event last wrote stays.",
                HelpBoxMessageType.Info)
            {
                style = { marginTop = 2f, marginBottom = 2f },
            });
        }

        /// <summary>Which extension channel the step drives, chosen from what is registered.</summary>
        void BuildExtensionRow()
        {
            var idProperty = stepProperty.FindPropertyRelative("ExtensionId");
            if (idProperty == null) return;

            var entries = TweenExtensionRegistry.All;
            var choices = new List<string>();
            var ids = new List<string>();

            for (var i = 0; i < entries.Count; i++)
            {
                choices.Add(entries[i].Category + " / " + entries[i].DisplayName);
                ids.Add(entries[i].Id);
            }

            var current = ids.IndexOf(idProperty.stringValue);

            // A missing channel stays visible by its raw id, so the author can see what the
            // step was meant to drive instead of it silently reading as the first choice.
            if (current < 0)
            {
                choices.Insert(0, string.IsNullOrEmpty(idProperty.stringValue)
                    ? "(choose a channel)"
                    : idProperty.stringValue + " (missing)");
                ids.Insert(0, idProperty.stringValue);
                current = 0;
            }

            var dropdown = new DropdownField("Channel", choices, current)
            {
                tooltip = "The extension channel this step animates. Channels are defined in code " +
                          "with ITweenExtensionChannel.",
            };

            dropdown.RegisterValueChangedCallback(evt =>
            {
                var index = choices.IndexOf(evt.newValue);
                if (index < 0 || ids[index] == idProperty.stringValue) return;

                idProperty.stringValue = ids[index];
                stepProperty.serializedObject.ApplyModifiedProperties();
                Rebuild();
                Changed?.Invoke();
            });

            body.Add(dropdown);
        }

        /// <summary>The channel author's own section, when they wrote one.</summary>
        void BuildExtensionSection(GameObject fallback)
        {
            var inspector = TweenExtensionInspectors.Create(step.ExtensionId);
            if (inspector == null) return;

            // The section may have written any field, so the standard ones are redrawn too --
            // next frame, since the element that raised this is about to be replaced.
            inspector.Changed = () =>
            {
                Changed?.Invoke();
                schedule.Execute(Rebuild);
            };

            VisualElement section;
            try
            {
                var resolved = TweenBindingResolver.Resolve(step, fallback, out _);
                section = inspector.CreateInspectorGUI(stepProperty, resolved);
            }
            catch (Exception exception)
            {
                section = new HelpBox("The inspector for this channel failed: " + exception.Message,
                    HelpBoxMessageType.Error);
            }

            if (section == null) return;

            body.Add(SectionHeader(step.DisplayName));
            body.Add(section);
        }

        void BuildTargetRow(TweenChannelInfo info)
        {
            var row = Row();

            var targetProperty = stepProperty.FindPropertyRelative("Target");
            var target = new PropertyField(targetProperty, "Target")
            {
                style = { flexGrow = 1f, minWidth = 0f },
                tooltip = "Leave empty to animate the player's own GameObject",
            };

            var lastTarget = ValueKey(targetProperty);
            target.RegisterValueChangeCallback(evt =>
            {
                if (!IsRealEdit(evt.changedProperty, ref lastTarget)) return;

                Rebuild();
                Changed?.Invoke();
            });
            row.Add(target);

            body.Add(row);

            // Naming the resolved channel is what tells an author that Fade found a CanvasGroup
            // rather than the Image they were looking at.
            if (!string.IsNullOrEmpty(info.ChannelName))
            {
                body.Add(Hint("Writes " + info.ChannelName + (info.IsResolved ? "" : " (unresolved)")));
            }
        }

        void BuildShaderRow()
        {
            shaderField.Bind(stepProperty, step);
            body.Add(shaderField);
        }

        void BuildValues(TweenChannelInfo info, TweenStepFields fields)
        {
            if (!fields.Values && !fields.Scramble) return;

            body.Add(SectionHeader("Values"));

            var showsMode = fields.Values && fields.SupportsFromCurrent && !fields.EndIsStrength;
            var mode = TweenValueModes.Of(step);

            if (showsMode)
            {
                modeField.Bind(stepProperty);
                body.Add(modeField);
                body.Add(Hint(TweenValueModes.Describe(mode, info.ChannelName)));
            }
            else if (fields.Scramble && fields.SupportsFromCurrent)
            {
                // A scramble has no numeric endpoints, only "grow out of the live text or not".
                body.Add(new PropertyField(stepProperty.FindPropertyRelative("FromCurrent"),
                    "From live text"));
            }

            if (!fields.Values) return;

            // Punch and Shake read the end value as a strength, so there is no start to author
            // and no mode to choose.
            var showStart = !fields.EndIsStrength
                            && (!showsMode || TweenValueModes.ShowsStartValue(mode));
            var showEnd = fields.EndIsStrength || !showsMode || TweenValueModes.ShowsEndValue(mode);

            if (showStart)
            {
                fromField.Bind(stepProperty, step, info, TweenValueField.Endpoint.From,
                    TweenValueModes.StartLabel(mode));
                body.Add(fromField);
            }

            if (showStart && showEnd)
            {
                var swap = new Button(Swap)
                {
                    text = "⇄ Swap",
                    tooltip = "Exchange the start and end values",
                    style = { alignSelf = Align.FlexEnd, fontSize = 9f, marginTop = 0f, marginBottom = 0f },
                };
                body.Add(swap);
            }

            if (showEnd)
            {
                toField.Bind(stepProperty, step, info, TweenValueField.Endpoint.To,
                    TweenValueModes.EndLabel(mode, fields.EndIsStrength));
                body.Add(toField);
            }

            if (fields.UniformScale)
            {
                var uniformProperty = stepProperty.FindPropertyRelative("UniformScale");
                var uniform = new PropertyField(uniformProperty, "Uniform");
                var lastUniform = ValueKey(uniformProperty);
                uniform.RegisterValueChangeCallback(evt =>
                {
                    if (IsRealEdit(evt.changedProperty, ref lastUniform)) Changed?.Invoke();
                });
                body.Add(uniform);
            }

            if (fields.Axis)
            {
                axisField.Bind(stepProperty, info.Shape, info.ComponentLabel);
                if (axisField.IsApplicable) body.Add(axisField);
            }
        }

        void BuildTiming(TweenStepFields fields)
        {
            body.Add(SectionHeader("Timing"));

            var row = Row();
            row.Add(Grow(new PropertyField(stepProperty.FindPropertyRelative("StartTime"), "Start")));

            if (fields.Duration)
            {
                row.Add(Grow(new PropertyField(stepProperty.FindPropertyRelative("Duration"), "Length")));
            }

            body.Add(row);

            if (!fields.Duration) return;

            var delayRow = Row();
            delayRow.Add(Grow(Gated("Delay", "Delay")));

            // The delay kind only means something once there is a delay to apply.
            if (step.Delay > 0f)
            {
                delayRow.Add(Grow(new PropertyField(stepProperty.FindPropertyRelative("DelayType"), "On")));
            }

            body.Add(delayRow);

            var loopRow = Row();
            loopRow.Add(Grow(Gated("Loops", "Loops")));

            if (step.Loops != 1)
            {
                loopRow.Add(Grow(new PropertyField(stepProperty.FindPropertyRelative("LoopType"), "Style")));
            }

            body.Add(loopRow);

            if (step.Loops < 0)
            {
                body.Add(new HelpBox(
                    "A step cannot loop forever inside a sequence, so this plays once. Use the "
                    + "animation's own Loops setting to repeat indefinitely.",
                    HelpBoxMessageType.Warning));
            }
        }

        void BuildEasing()
        {
            body.Add(SectionHeader("Easing"));

            var row = Row();

            var picker = new Button { text = Nice(step.Ease), style = { flexGrow = 1f, minWidth = 0f } };
            picker.tooltip = "Browse every ease as a curve";
            picker.clicked += () => OpenEasePicker(picker);
            row.Add(picker);

            body.Add(row);

            easeCurve.style.height = 44f;
            easeCurve.style.marginTop = 2f;
            easeCurve.SetEase(step.Ease, step.CustomCurve);

            // The graph is the honest control here, so clicking it opens the gallery too.
            easeCurve.tooltip = "Click to browse eases";
            body.Add(easeCurve);

            if (step.Ease == Ease.CustomAnimationCurve)
            {
                var curveProperty = stepProperty.FindPropertyRelative("CustomCurve");
                var curve = new PropertyField(curveProperty, "Curve");
                var lastCurve = ValueKey(curveProperty);
                curve.RegisterValueChangeCallback(evt =>
                {
                    if (!IsRealEdit(evt.changedProperty, ref lastCurve)) return;

                    easeCurve.SetEase(step.Ease, step.CustomCurve);
                    Changed?.Invoke();
                });
                body.Add(curve);
            }
        }

        void OpenEasePicker(VisualElement anchor)
        {
            if (step == null || stepProperty == null) return;

            var rect = GUIUtility.GUIToScreenRect(anchor.worldBound);

            TweenEasePicker.Open(rect, step.Ease,
                ease => WriteEnum("Ease", (int)ease),
                () => WriteEnum("Ease", (int)Ease.CustomAnimationCurve));
        }

        void BuildOptions(TweenStepFields fields, TweenChannelInfo info)
        {
            var any = fields.Space || fields.Channel || fields.AnchorTarget || fields.CameraProperty
                      || fields.Vibration || fields.RandomSeed || fields.Jump || fields.TextFormat
                      || fields.TextUnit || fields.Scramble || fields.TMPCharacter;

            if (!any) return;

            body.Add(SectionHeader("Options"));

            if (fields.Space) AddTracked("Space", "Space");
            if (fields.Channel) AddTracked("Channel", "Channel");
            if (fields.AnchorTarget) AddTracked("AnchorTarget", "Anchors");
            if (fields.CameraProperty) AddTracked("CameraProperty", "Property");

            if (fields.Vibration)
            {
                body.Add(IntSlider("Frequency", "Frequency",
                    TweenFieldRanges.FrequencyMin, TweenFieldRanges.FrequencyMax,
                    "Oscillations across the step's length"));

                body.Add(FloatSlider("DampingRatio", "Damping",
                    TweenFieldRanges.DampingMin, TweenFieldRanges.DampingMax,
                    "How quickly the oscillation dies away. 1 fully settles by the end."));
            }

            if (fields.RandomSeed) body.Add(SeedRow());

            if (fields.Jump)
            {
                body.Add(IntSlider("JumpCount", "Arcs",
                    TweenFieldRanges.JumpCountMin, TweenFieldRanges.JumpCountMax,
                    "How many hops the step makes"));

                body.Add(FloatSlider("JumpPower", "Height",
                    TweenFieldRanges.JumpPowerMin, TweenFieldRanges.JumpPowerMax,
                    "Peak height of the first arc, in the step's own units"));
            }

            if (fields.TMPCharacter) BuildCharacterOptions();
            if (fields.TextUnit) AddTracked("TextUnit", "Reveal by");
            if (fields.TextFormat) BuildCounterOptions();
            if (fields.Scramble) BuildScrambleOptions();
        }

        void BuildCharacterOptions()
        {
            AddTracked("TMPCharChannel", "Channel");

            var all = step.CharacterIndex < 0;

            var toggle = new Toggle("All characters")
            {
                value = all,
                tooltip = "Animate every character, offset in time, instead of just one",
            };
            toggle.RegisterValueChangedCallback(evt =>
            {
                // -1 is the "all characters" sentinel the builder reads.
                WriteInt("CharacterIndex", evt.newValue ? -1 : 0);
                Rebuild();
            });
            body.Add(toggle);

            if (all)
            {
                body.Add(FloatSlider("Stagger", "Stagger",
                    TweenFieldRanges.StaggerMin, TweenFieldRanges.StaggerMax,
                    "Delay added per character, which is what makes it read as a wave"));
            }
            else
            {
                AddTracked("CharacterIndex", "Character");
            }
        }

        void BuildCounterOptions()
        {
            AddTracked("TextFormat", "Format");

            // A format string is write-only in the inspector otherwise: you cannot tell
            // "{0:N0}" from "{0:0}" without running it.
            counterSample = Hint(string.Empty);
            body.Add(counterSample);
            RefreshSamples();
        }

        /// <summary>The counter's final text, formatted as it will be shown.</summary>
        internal static string CounterSample(TweenStep target)
        {
            try
            {
                return string.Format(string.IsNullOrEmpty(target.TextFormat) ? "{0}" : target.TextFormat, target.To.x);
            }
            catch (FormatException)
            {
                return "invalid format string";
            }
        }

        /// <summary>
        /// Recomputes the counter and scramble samples in place, so typing into the fields they
        /// describe updates them without rebuilding the panel under the caret.
        /// </summary>
        void RefreshSamples()
        {
            if (step == null) return;

            if (counterSample != null)
            {
                counterSample.text = "Shows as: " + CounterSample(step);
            }

            if (scrambleSample != null)
            {
                var source = step.FromCurrent ? LiveText() : string.Empty;
                var sample = TweenScrambleSample.At(step, source);
                scrambleSample.text = "Halfway: " + (string.IsNullOrEmpty(sample) ? "(empty)" : sample);
            }
        }

        void BuildScrambleOptions()
        {
            AddTracked("TargetText", "Target text");
            AddTracked("ScrambleMode", "Scramble");

            if (step.ScrambleMode == ScrambleMode.Custom) AddTracked("ScrambleChars", "Characters");

            AddTracked("RichText", "Parse rich text");

            // "Uppercase" and "Numerals" are names; what they fill the gap with is the choice.
            scrambleSample = Hint(string.Empty);
            body.Add(scrambleSample);
            RefreshSamples();
        }

        /// <summary>The text the step's target currently shows, for scramble samples.</summary>
        string LiveText()
        {
            var resolved = TweenBindingResolver.Resolve(step, TargetProvider?.Invoke(), out _);

#if LMTE_SUPPORT_TMP
            if (resolved is TMPro.TMP_Text tmp) return tmp.text;
#endif
#if LMTE_SUPPORT_UGUI
            if (resolved is UnityEngine.UI.Text text) return text.text;
#endif
            return string.Empty;
        }

        VisualElement SeedRow()
        {
            var row = Row();
            row.Add(Grow(Tracked("RandomSeed", "Seed")));

            row.Add(new Button(() => WriteInt("RandomSeed", UnityEngine.Random.Range(1, int.MaxValue)))
            {
                text = "↻",
                tooltip = "Roll a new seed. The same seed always reproduces the same shake.",
                style = { width = 22f, flexShrink = 0f, paddingLeft = 0f, paddingRight = 0f },
            });

            return row;
        }

        void BuildEvents(TweenStepFields fields)
        {
            if (!fields.Callback && !fields.Progress) return;

            body.Add(SectionHeader("Event"));

            var name = fields.Callback ? "OnCallback" : "OnProgress";
            var eventProperty = stepProperty.FindPropertyRelative(name);
            body.Add(new PropertyField(eventProperty));

            if (!TweenStepDrawer.HasRuntimeOnlyListener(eventProperty)) return;

            // Unity creates persistent listeners as Runtime Only, so an authored callback does
            // nothing while previewing and reads as broken.
            body.Add(new HelpBox(
                "These listeners are set to Runtime Only, so they will not fire while previewing.",
                HelpBoxMessageType.Info));

            body.Add(new Button(() =>
            {
                TweenStepDrawer.MakeListenersEditorAndRuntime(eventProperty);
                Rebuild();
            })
            {
                text = "Enable in editor too",
                tooltip = "Switch these listeners to Editor And Runtime, so they fire while previewing",
            });
        }

        // --- Edits ---

        void Swap()
        {
            var isColor = TweenChannelInfo.Describe(step, TargetProvider?.Invoke()).Shape
                          == TweenValueShape.Color;

            var fromName = isColor ? "FromColor" : "From";
            var toName = isColor ? "ToColor" : "To";

            var from = stepProperty.FindPropertyRelative(fromName);
            var to = stepProperty.FindPropertyRelative(toName);
            if (from == null || to == null) return;

            if (isColor)
            {
                var temp = from.colorValue;
                from.colorValue = to.colorValue;
                to.colorValue = temp;
            }
            else
            {
                var temp = from.vector4Value;
                from.vector4Value = to.vector4Value;
                to.vector4Value = temp;
            }

            stepProperty.serializedObject.ApplyModifiedProperties();
            Rebuild();
            Changed?.Invoke();
        }

        void WriteEnum(string name, int value)
        {
            var property = stepProperty?.FindPropertyRelative(name);
            if (property == null) return;

            property.intValue = value;
            stepProperty.serializedObject.ApplyModifiedProperties();

            Rebuild();
            Changed?.Invoke();
        }

        void WriteInt(string name, int value)
        {
            var property = stepProperty?.FindPropertyRelative(name);
            if (property == null) return;

            // RandomSeed is a uint in the model, which SerializedProperty exposes as a long.
            if (property.numericType == SerializedPropertyNumericType.UInt32)
            {
                property.uintValue = (uint)Mathf.Max(0, value);
            }
            else
            {
                property.intValue = value;
            }

            stepProperty.serializedObject.ApplyModifiedProperties();
            Changed?.Invoke();
        }

        // --- Small builders ---

        void AddTracked(string name, string label)
        {
            var field = Tracked(name, label);
            if (field != null) body.Add(field);
        }

        PropertyField Tracked(string name, string label)
        {
            var property = stepProperty?.FindPropertyRelative(name);
            if (property == null) return null;

            var field = new PropertyField(property, label);

            // Popups and toggles gate other controls (ScrambleMode shows the character set, the
            // TMP channel changes the value widget), so they re-run the layout. Text and number
            // fields only refresh the live samples: rebuilding on every keystroke would throw
            // the caret out of the field being typed in.
            var relayout = property.propertyType is SerializedPropertyType.Enum or SerializedPropertyType.Boolean;
            var lastSeen = ValueKey(property);
            field.RegisterValueChangeCallback(evt =>
            {
                if (!IsRealEdit(evt.changedProperty, ref lastSeen)) return;

                Changed?.Invoke();
                if (relayout) Rebuild();
                else RefreshSamples();
            });

            return field;
        }

        /// <summary>
        /// A number field that decides which other controls show -- a delay reveals its delay
        /// kind, a loop count its loop style and the infinite-loop warning.
        /// </summary>
        /// <remarks>
        /// The layout is re-run when the field loses focus, and only if what it gates actually
        /// changed. Re-running it on each keystroke would move the caret out of the field while
        /// "0.25" was still being typed.
        /// </remarks>
        PropertyField Gated(string name, string label)
        {
            var property = stepProperty.FindPropertyRelative(name);
            var field = new PropertyField(property, label);

            var gate = LayoutGate();
            field.RegisterCallback<FocusOutEvent>(_ =>
            {
                if (step == null || LayoutGate() == gate) return;
                schedule.Execute(Rebuild);
            });

            return field;
        }

        /// <summary>The timing facts that change which controls are shown.</summary>
        string LayoutGate()
        {
            if (step == null) return string.Empty;
            return (step.Delay > 0f ? "d" : "-") + (step.Loops != 1 ? "l" : "-") + (step.Loops < 0 ? "i" : "-");
        }

        VisualElement FloatSlider(string name, string label, float min, float max, string tooltip)
        {
            var property = stepProperty.FindPropertyRelative(name);
            var slider = new Slider(label, min, max) { showInputField = true, tooltip = tooltip };
            slider.BindProperty(property);
            slider.RegisterValueChangedCallback(_ =>
            {
                if (!binding) Changed?.Invoke();
            });

            return slider;
        }

        VisualElement IntSlider(string name, string label, int min, int max, string tooltip)
        {
            var property = stepProperty.FindPropertyRelative(name);
            var slider = new SliderInt(label, min, max) { showInputField = true, tooltip = tooltip };
            slider.BindProperty(property);
            slider.RegisterValueChangedCallback(_ =>
            {
                if (!binding) Changed?.Invoke();
            });

            return slider;
        }

        static VisualElement Row()
        {
            return new VisualElement
            {
                style = { flexDirection = FlexDirection.Row, alignItems = Align.Center },
            };
        }

        static T Grow<T>(T element) where T : VisualElement
        {
            element.style.flexGrow = 1f;
            element.style.flexShrink = 1f;
            element.style.minWidth = 0f;
            return element;
        }

        static Label SectionHeader(string text)
        {
            return new Label(text)
            {
                style =
                {
                    fontSize = 9f,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    color = TweenTimelineStyles.RulerText,
                    marginTop = 6f,
                    marginBottom = 1f,
                },
            };
        }

        static Label Hint(string text)
        {
            return new Label(text)
            {
                style =
                {
                    fontSize = 9f,
                    color = TweenTimelineStyles.RulerText,
                    marginBottom = 2f,
                    whiteSpace = WhiteSpace.Normal,
                },
            };
        }

        static string Nice(object value)
        {
            return ObjectNames.NicifyVariableName(value.ToString());
        }
    }
}
