using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace LitMotion.TweenEditor.Editor
{
    /// <summary>
    /// The whole authoring surface -- animation chips, timeline, preview bar, binding status,
    /// clip inspector and animation settings -- as one element that any host can embed.
    /// </summary>
    /// <remarks>
    /// The player inspector, the preset inspector and the dockable window all host this. Before
    /// it existed the two inspectors each carried their own copy of the preview wiring, and
    /// every feature had to be added twice; the window would have made it three times.
    ///
    /// <b>One preview at a time.</b> Two views previewing the same object would each snapshot
    /// the other's half-animated pose and "restore" the wrong values. So starting a preview
    /// stops whichever view was previewing before, and a view showing the same object as the
    /// one previewing says where the preview is running instead of fighting it.
    ///
    /// <b>Lifetime.</b> The host calls <see cref="Dispose"/> when it goes away, which restores
    /// the scene. Detaching from a panel does the same, because an inspector that rebuilds
    /// itself drops the old element without telling anyone.
    /// </remarks>
    internal sealed class TweenAuthoringView : VisualElement, IDisposable
    {
        /// <summary>How the view arranges itself for its host.</summary>
        public enum Layout
        {
            /// <summary>Stacked, for an inspector.</summary>
            Narrow,

            /// <summary>Timeline and clip inspector side by side, for a window.</summary>
            Wide,
        }

        static TweenAuthoringView previewOwner;
        static event Action PreviewOwnerChanged;

        // Which animation each target last showed, so reselecting an object returns to it.
        static readonly Dictionary<UnityEngine.Object, int> RememberedAnimation = new();

        readonly TweenAuthoringSource source;
        readonly Layout layoutMode;
        readonly string hostName;
        readonly TweenPreviewController preview = new();

        TweenTimelineView timeline;
        TweenClipInspector clipInspector;
        TweenAnimationBar animationBar;
        TweenPreviewBar previewBar;
        TweenBindingStatusPanel statusPanel;
        VisualElement animationSettings;
        VisualElement hintStrip;
        VisualElement keymapSheet;
        HelpBox warnings;
        Label ownershipNote;
        ObjectField previewTargetField;

        TweenBindingReport report;
        int selectedAnimation;
        int previewingAnimation = -1;
        bool hooked;
        bool disposed;

        public TweenAuthoringView(TweenAuthoringSource source, Layout layout, string hostName)
        {
            this.source = source;
            layoutMode = layout;
            this.hostName = hostName;

            if (source?.Target != null && RememberedAnimation.TryGetValue(source.Target, out var remembered))
            {
                selectedAnimation = remembered;
            }

            preview.Changed += OnPreviewChanged;

            Build();
            RefreshAll();

            RegisterCallback<AttachToPanelEvent>(_ => Hook());
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                Unhook();
                StopPreview();
            });

            if (source?.SerializedObject != null)
            {
                this.TrackSerializedObjectValue(source.SerializedObject, _ => OnSerializedChanged());
            }
        }

        // --- Public surface, for hosts and tests ---

        /// <summary>The preview this view drives.</summary>
        public TweenPreviewController Preview => preview;

        public TweenTimelineView Timeline => timeline;

        public TweenClipInspector ClipInspector => clipInspector;

        public TweenAuthoringSource Source => source;

        /// <summary>The binding check from the last refresh.</summary>
        public TweenBindingReport Report => report;

        /// <summary>Index of the animation being edited.</summary>
        public int SelectedAnimationIndex
        {
            get => ClampAnimationIndex(selectedAnimation);
            set => SelectAnimation(value);
        }

        /// <summary>Stops the preview, restoring the scene, and releases every hook.</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            Unhook();
            StopPreview();
            preview.Changed -= OnPreviewChanged;
            preview.Dispose();
        }

        // --- Construction ---

        void Build()
        {
            style.flexGrow = 1f;

            hintStrip = BuildHintStrip();
            Add(hintStrip);

            keymapSheet = TweenKeymap.BuildSheet();
            keymapSheet.style.display = DisplayStyle.None;
            Add(keymapSheet);

            var main = new VisualElement();
            var side = new VisualElement();

            if (source != null && source.BorrowsPreviewTarget) main.Add(BuildPreviewTargetField());

            if (source != null && source.SupportsMultipleAnimations)
            {
                animationBar = BuildAnimationBar();
                main.Add(animationBar);
            }

            main.Add(BuildTimeline());

            previewBar = BuildPreviewBar();
            main.Add(previewBar);

            statusPanel = new TweenBindingStatusPanel();
            statusPanel.StepRequested += SelectStep;
            statusPanel.FixApplied += () =>
            {
                RefreshStatus();
                clipInspector?.Rebuild();
                timeline?.RefreshGeometry();
            };
            main.Add(statusPanel);

            ownershipNote = new Label
            {
                style =
                {
                    display = DisplayStyle.None,
                    fontSize = 10f,
                    color = TweenTimelineStyles.Warning,
                    marginBottom = 2f,
                    whiteSpace = WhiteSpace.Normal,
                },
            };
            main.Add(ownershipNote);

            warnings = new HelpBox(string.Empty, HelpBoxMessageType.Warning) { style = { display = DisplayStyle.None } };
            main.Add(warnings);

            side.Add(BuildClipInspector());

            var settingsFoldout = new Foldout { text = "Animation Settings", value = false };
            animationSettings = settingsFoldout.contentContainer;

            if (layoutMode == Layout.Wide)
            {
                main.Add(settingsFoldout);

                var split = new TwoPaneSplitView(1, 340f, TwoPaneSplitViewOrientation.Horizontal)
                {
                    style = { flexGrow = 1f },
                };

                var left = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1f, paddingRight = 4f } };
                left.Add(main);

                var right = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1f, paddingLeft = 4f } };
                right.Add(side);

                split.Add(left);
                split.Add(right);
                Add(split);

                // The window has room to spare, so the lanes need not scroll inside a scroll.
                timeline.MaxBodyHeight = 4000f;
            }
            else
            {
                Add(main);
                Add(side);
                Add(settingsFoldout);
            }
        }

        VisualElement BuildHintStrip()
        {
            var strip = TweenUi.Row();
            strip.style.marginBottom = 2f;

            var text = TweenUi.Dim("Click a clip to edit it · drag to move, edges to trim · right-click for more · " +
                                   "Space plays · wheel zooms", 9f);
            text.style.flexGrow = 1f;
            text.style.flexShrink = 1f;
            strip.Add(text);

            strip.Add(new Button(ToggleKeymap)
            {
                text = "Keys",
                tooltip = "Show every shortcut and gesture",
                style = { fontSize = 9f, height = 16f, flexShrink = 0f },
            });

            strip.Add(new Button(() =>
            {
                TweenEditorSettings.instance.ShowHints = false;
                ApplyHintVisibility();
            })
            {
                text = "✕",
                tooltip = "Hide this hint. It can be turned back on in Preferences → LitMotion Tween Editor.",
                style = { fontSize = 9f, height = 16f, width = 18f, flexShrink = 0f },
            });

            return strip;
        }

        /// <summary>Shows or hides the shortcut sheet.</summary>
        public void ToggleKeymap()
        {
            var visible = keymapSheet.style.display == DisplayStyle.Flex;
            keymapSheet.style.display = visible ? DisplayStyle.None : DisplayStyle.Flex;
        }

        void ApplyHintVisibility()
        {
            hintStrip.style.display = TweenEditorSettings.instance.ShowHints ? DisplayStyle.Flex : DisplayStyle.None;
        }

        VisualElement BuildPreviewTargetField()
        {
            previewTargetField = new ObjectField("Preview On")
            {
                objectType = typeof(GameObject),
                allowSceneObjects = true,
                value = source.PreviewTarget,
                tooltip = "Scene object this preset is previewed against. Not saved in the asset.",
            };

            previewTargetField.RegisterValueChangedCallback(evt =>
            {
                StopPreview();
                source.PreviewTarget = evt.newValue as GameObject;
                RefreshStatus();
                timeline?.RefreshGeometry();
                clipInspector?.Rebuild();
                SyncPreviewUI();
            });

            return previewTargetField;
        }

        TweenAnimationBar BuildAnimationBar()
        {
            var bar = new TweenAnimationBar();

            bar.Selected += SelectAnimation;
            bar.PlayRequested += index =>
            {
                SelectAnimation(index);
                StopPreview();
                if (BeginPreview()) preview.Play();
                SyncPreviewUI();
            };
            bar.RenameRequested += (index, id) =>
            {
                TweenAnimationListCommands.Rename(source, index, id);
                RefreshAll();
            };
            bar.DuplicateRequested += index =>
            {
                StopPreview();
                var copy = TweenAnimationListCommands.Duplicate(source, index);
                if (copy >= 0) selectedAnimation = copy;
                RefreshAll();
            };
            bar.MoveRequested += (index, offset) =>
            {
                StopPreview();
                var moved = TweenAnimationListCommands.Move(source, index, offset);
                if (index == selectedAnimation) selectedAnimation = moved;
                RefreshAll();
            };
            bar.RemoveRequested += index =>
            {
                StopPreview();
                TweenAnimationListCommands.Remove(source, index);
                selectedAnimation = Mathf.Max(0, Mathf.Min(selectedAnimation, source.AnimationCount - 1));
                RefreshAll();
            };
            bar.AddEmptyRequested += () => AddAnimation(null);
            bar.AddPresetRequested += asset => AddAnimation(asset == null ? null : asset.Animation, asset != null);

            return bar;
        }

        void AddAnimation(TweenAnimation template, bool fromPreset = false)
        {
            if (fromPreset && template == null) return;

            StopPreview();
            var index = TweenAnimationListCommands.Add(source, template);
            if (index >= 0) selectedAnimation = index;
            RefreshAll();
        }

        VisualElement BuildTimeline()
        {
            timeline = new TweenTimelineView
            {
                TargetProvider = () => source?.PreviewTarget,
            };

            timeline.StepSelected += _ => RefreshStepInspector();

            // Space on the timeline is the play shortcut, which only the host can service.
            timeline.PlayToggleRequested += TogglePlay;
            timeline.EditFinished += () =>
            {
                if (preview.IsActive) preview.Rebuild();
                RefreshStatus();
                SyncPreviewUI();
            };
            timeline.Scrubbed += seconds =>
            {
                if (!preview.IsActive && !BeginPreview()) return;

                preview.Pause();
                preview.Scrub(seconds);
                SyncPreviewUI();
            };
            timeline.StructureChanged += () =>
            {
                source?.SerializedObject?.Update();
                RebindTimeline();

                if (preview.IsActive) preview.Rebuild();
                SyncPreviewUI();
            };

            return timeline;
        }

        TweenPreviewBar BuildPreviewBar()
        {
            var bar = new TweenPreviewBar();

            bar.PlayToggled += TogglePlay;
            bar.StopRequested += StopPreviewAndRewind;
            bar.FrameStepRequested += frames =>
            {
                if (!preview.IsActive && !BeginPreview()) return;

                preview.StepFrames(frames);
                SyncPreviewUI();
            };
            bar.TimeEntered += seconds =>
            {
                if (!preview.IsActive && !BeginPreview()) return;

                preview.Pause();
                preview.Scrub(seconds);
                SyncPreviewUI();
            };
            bar.LoopChanged += value => preview.Loop = value;
            bar.SpeedChanged += value => preview.SpeedMultiplier = value;
            bar.AutoChanged += value => TweenEditorSettings.instance.AutoPreview = value;
            bar.StatusClicked += () => statusPanel.Toggle();

            bar.SetAuto(TweenEditorSettings.instance.AutoPreview);
            bar.SetLoop(preview.Loop);

            return bar;
        }

        VisualElement BuildClipInspector()
        {
            clipInspector = new TweenClipInspector
            {
                TargetProvider = () => source?.PreviewTarget,
            };

            // While a preview is running, an edit should be visible immediately -- that is the
            // whole point of scrubbing to a pose and tuning it there.
            clipInspector.Changed += () =>
            {
                RefreshStatus();
                timeline?.RefreshGeometry();

                if (!AutoPreview || !preview.IsActive) return;

                preview.Rebuild();
                SyncPreviewUI();
            };

            clipInspector.SoloRequested += SoloPreviewSelectedStep;
            clipInspector.FixApplied += () =>
            {
                RefreshStatus();
                timeline?.RefreshGeometry();
            };

            return clipInspector;
        }

        static bool AutoPreview => TweenEditorSettings.instance.AutoPreview;

        // --- Hooks ---

        void Hook()
        {
            if (hooked || disposed) return;
            hooked = true;

            PreviewOwnerChanged += SyncOwnershipNote;
            TweenEditorSettings.Changed += OnSettingsChanged;
            TweenExtensionRegistry.Changed += OnExtensionsChanged;
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.hierarchyChanged += OnHierarchyChanged;

            SyncOwnershipNote();
        }

        void Unhook()
        {
            if (!hooked) return;
            hooked = false;

            PreviewOwnerChanged -= SyncOwnershipNote;
            TweenEditorSettings.Changed -= OnSettingsChanged;
            TweenExtensionRegistry.Changed -= OnExtensionsChanged;
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
        }

        void OnSettingsChanged()
        {
            ApplyHintVisibility();
            previewBar?.SetAuto(AutoPreview);
            timeline?.ApplySettings();
        }

        void OnExtensionsChanged()
        {
            timeline?.Rebuild();
            clipInspector?.Rebuild();
            RefreshStatus();
        }

        void OnUndoRedo()
        {
            if (!source.IsValid) return;

            source.SerializedObject.Update();
            RefreshAll();
            if (preview.IsActive) preview.Rebuild();
        }

        void OnHierarchyChanged()
        {
            // Adding a CanvasGroup by hand fixes a binding without touching the player, so the
            // badges have to notice changes to the scene as well as to the steps.
            if (!source.IsValid) return;

            RefreshStatus();
            timeline?.RefreshGeometry();
        }

        void OnSerializedChanged()
        {
            if (!source.IsValid) return;

            RefreshAnimationBar();

            // Mid-drag the clip writes on every pointer move; rebuilding the preview there
            // would thrash. TweenTimelineView.EditFinished handles the single rebuild.
            if (timeline is { IsEditing: true }) return;

            if (preview.IsActive) preview.Rebuild();
            timeline?.RefreshGeometry();
            RefreshStatus();
            SyncPreviewUI();
        }

        // --- Selection ---

        int ClampAnimationIndex(int index)
        {
            var count = source?.AnimationCount ?? 0;
            if (count == 0) return -1;
            return Mathf.Clamp(index, 0, count - 1);
        }

        TweenAnimation SelectedAnimation() => source?.AnimationAt(SelectedAnimationIndex);

        SerializedProperty SelectedAnimationProperty() => source?.AnimationPropertyAt(SelectedAnimationIndex);

        void SelectAnimation(int index)
        {
            index = ClampAnimationIndex(index);
            if (index == selectedAnimation && timeline != null && timeline.IsBoundTo(SelectedAnimation()))
            {
                return;
            }

            StopPreview();
            selectedAnimation = index;
            if (source?.Target != null) RememberedAnimation[source.Target] = index;

            RefreshAll();
        }

        /// <summary>Selects a step on the timeline and shows it in the clip inspector.</summary>
        public void SelectStep(int index)
        {
            timeline?.SelectStep(index);
            RefreshStepInspector();
        }

        // --- Refresh ---

        /// <summary>Re-reads everything from the source.</summary>
        public void RefreshAll()
        {
            if (source == null || !source.IsValid) return;

            selectedAnimation = Mathf.Max(0, ClampAnimationIndex(selectedAnimation));

            ApplyHintVisibility();
            RefreshAnimationBar();
            RebindTimeline();
        }

        void RefreshAnimationBar()
        {
            animationBar?.Refresh(TweenAnimationListCommands.Ids(source), SelectedAnimationIndex,
                preview.IsActive ? previewingAnimation : -1);
        }

        void RebindTimeline()
        {
            var animationProperty = SelectedAnimationProperty();
            var animation = SelectedAnimation();

            timeline?.Bind(animationProperty?.FindPropertyRelative("Steps"), animation);
            timeline?.SetEnabled(animation != null);

            RefreshAnimationSettings(animationProperty);
            RefreshStepInspector();
            RefreshStatus();
            SyncPreviewUI();
        }

        void RefreshStatus()
        {
            if (source == null || !source.IsValid) return;

            report = TweenBindingStatus.Evaluate(SelectedAnimation(), source.PreviewTarget);
            previewBar?.SetStatus(SelectedAnimation() == null ? null : report);
            statusPanel?.Show(report);
        }

        void RefreshAnimationSettings(SerializedProperty animationProperty)
        {
            if (animationSettings == null) return;

            animationSettings.Clear();
            if (animationProperty == null) return;

            // The id stays free text -- any string is a valid id -- with the built-in names one
            // click away, so the common ones are discoverable without being a closed set.
            var idProperty = animationProperty.FindPropertyRelative("Id");
            if (idProperty != null) animationSettings.Add(BuildIdRow(idProperty));

            var names = new List<string> { "Loops", "LoopType", "PlaybackSpeed", "IgnoreTimeScale", "BlendMode", "KillBehavior" };
            if (source.AllowsSceneReferences) names.Add("OnComplete");

            for (var i = 0; i < names.Count; i++)
            {
                var child = animationProperty.FindPropertyRelative(names[i]);
                if (child != null) animationSettings.Add(new PropertyField(child));
            }

            if (source.SupportsMultipleAnimations)
            {
                animationSettings.Add(BuildPresetRow());
            }
            else
            {
                animationSettings.Add(new HelpBox(
                    "Steps in a preset cannot target a specific scene object. They resolve against " +
                    "whichever TweenPlayer runs them, which is what makes the preset reusable.",
                    HelpBoxMessageType.Info));
            }

            animationSettings.Bind(source.SerializedObject);
        }

        VisualElement BuildIdRow(SerializedProperty idProperty)
        {
            var row = TweenUi.Row();

            var field = new PropertyField(idProperty) { style = { flexGrow = 1f } };
            row.Add(field);

            var pick = new Button { text = "▾", tooltip = "Use a built-in animation name", style = { width = 22f } };
            pick.clicked += () =>
            {
                var menu = new GenericMenu();
                var current = idProperty.stringValue;

                for (var i = 0; i < TweenAnimationId.BuiltIn.Length; i++)
                {
                    var id = TweenAnimationId.BuiltIn[i];
                    menu.AddItem(new GUIContent(id), current == id, () =>
                    {
                        source.SerializedObject.Update();
                        idProperty.stringValue = id;
                        source.SerializedObject.ApplyModifiedProperties();
                        RefreshAnimationBar();
                    });
                }

                menu.ShowAsContext();
            };
            row.Add(pick);

            return row;
        }

        VisualElement BuildPresetRow()
        {
            var row = TweenUi.Row();
            row.style.marginTop = 4f;

            row.Add(new Button(SaveAsPreset)
            {
                text = "Save as Preset",
                tooltip = "Write this animation to a reusable TweenAnimationAsset",
                style = { flexGrow = 1f },
            });

            row.Add(new Button(LoadFromPreset)
            {
                text = "Load Preset",
                tooltip = "Replace this animation with the contents of a TweenAnimationAsset",
                style = { flexGrow = 1f },
            });

            return row;
        }

        void SaveAsPreset()
        {
            var animation = SelectedAnimation();
            if (animation == null) return;

            TweenPresetFiles.SaveWithPrompt(animation, animation.Id);
        }

        void LoadFromPreset()
        {
            if (SelectedAnimationProperty() == null) return;

            TweenPresetPicker.Show(asset =>
            {
                if (asset?.Animation == null) return;

                StopPreview();
                Undo.RegisterCompleteObjectUndo(source.Target, "Load Tween Preset");

                // Replacing the contents rather than the element keeps the list slot, so the
                // selection and any references to this animation stay valid.
                TweenAnimationListCommands.CopyInto(SelectedAnimation(), asset.Animation);

                EditorUtility.SetDirty(source.Target);
                source.SerializedObject.Update();
                RefreshAll();
            });
        }

        void RefreshStepInspector()
        {
            if (clipInspector == null) return;

            var animation = SelectedAnimation();
            var index = timeline?.SelectedIndex ?? -1;
            var stepsProperty = SelectedAnimationProperty()?.FindPropertyRelative("Steps");

            if (animation == null || index < 0 || index >= animation.Steps.Count
                || stepsProperty == null || index >= stepsProperty.arraySize)
            {
                clipInspector.Bind(null, null);
                return;
            }

            clipInspector.Bind(stepsProperty.GetArrayElementAtIndex(index), animation.Steps[index]);

            // Selecting a clip parks the playhead at its start, so the pose being edited is the
            // pose on screen. Only while already previewing: clicking a clip must not start
            // writing to the scene on its own.
            if (AutoPreview && preview.IsActive)
            {
                preview.Pause();
                preview.Scrub(animation.Steps[index].StartTime);
                timeline?.SetPlayheadTime(preview.Time);
                SyncPreviewUI();
            }
        }

        // --- Preview ---

        bool BeginPreview()
        {
            var animation = SelectedAnimation();
            var target = source?.PreviewTarget;
            if (animation == null) return false;

            if (target == null)
            {
                ShowWarning(source != null && source.BorrowsPreviewTarget
                    ? "Pick a scene object in Preview On to preview this preset."
                    : "Nothing to preview against.");
                return false;
            }

            ClaimPreview();

            var started = preview.Begin(animation, target);
            previewingAnimation = started ? SelectedAnimationIndex : -1;

            if (!started && animation.EnabledStepCount == 0) ShowWarning("This animation has no enabled steps to preview.");

            RefreshAnimationBar();
            SyncPreviewUI();
            return started;
        }

        /// <summary>
        /// Makes this view the only one previewing, stopping any other first so two previews
        /// never snapshot each other's half-animated poses.
        /// </summary>
        void ClaimPreview()
        {
            if (previewOwner != null && previewOwner != this) previewOwner.StopPreview();

            previewOwner = this;
            PreviewOwnerChanged?.Invoke();
        }

        internal void TogglePlay()
        {
            if (preview.IsPlaying)
            {
                preview.Pause();
            }
            else
            {
                if (!preview.IsActive && !BeginPreview()) return;
                preview.Play();
            }

            SyncPreviewUI();
        }

        /// <summary>Stops the preview and restores the scene. Safe to call when not previewing.</summary>
        public void StopPreview()
        {
            var wasActive = preview.IsActive;
            preview.Stop();
            previewingAnimation = -1;

            if (previewOwner == this)
            {
                previewOwner = null;
                PreviewOwnerChanged?.Invoke();
            }

            if (!wasActive) return;

            clipInspector?.SetPreviewTime(null);
            RefreshAnimationBar();
            SyncPreviewUI();
        }

        void StopPreviewAndRewind()
        {
            StopPreview();
            timeline?.SetPlayheadTime(0f);
            SyncPreviewUI();
        }

        /// <summary>
        /// Previews the selected step on its own, so one clip can be judged without the rest of
        /// the animation playing over it.
        /// </summary>
        /// <remarks>
        /// The throwaway animation holds the live step by reference rather than a copy, so edits
        /// made while it plays still apply.
        /// </remarks>
        void SoloPreviewSelectedStep()
        {
            var animation = SelectedAnimation();
            var index = timeline?.SelectedIndex ?? -1;
            var target = source?.PreviewTarget;
            if (animation == null || target == null || index < 0 || index >= animation.Steps.Count) return;

            var solo = new TweenAnimation { Id = animation.Id + " (solo)" };
            solo.Steps.Clear();
            solo.Steps.Add(animation.Steps[index]);

            StopPreview();
            ClaimPreview();
            if (!preview.Begin(solo, target)) return;

            previewingAnimation = SelectedAnimationIndex;
            preview.Play();
            RefreshAnimationBar();
            SyncPreviewUI();
        }

        void OnPreviewChanged()
        {
            timeline?.SetPlayheadTime(preview.Time);
            clipInspector?.SetPreviewTime(preview.IsActive ? preview.Time : null);
            SyncPreviewUI();
        }

        void ShowWarning(string text)
        {
            if (warnings == null) return;

            warnings.text = text;
            warnings.style.display = DisplayStyle.Flex;
        }

        void SyncPreviewUI()
        {
            if (previewBar == null) return;

            var animation = SelectedAnimation();
            var duration = preview.IsActive ? preview.Duration : animation?.Duration ?? 0f;
            if (float.IsPositiveInfinity(duration)) duration = 0f;

            previewBar.Sync(preview.IsActive, preview.IsPlaying, preview.Time, duration);
            previewBar.SetPlayEnabled(animation != null && source?.PreviewTarget != null);

            var errors = preview.BuildErrors;
            if (preview.IsActive && errors.Count > 0)
            {
                ShowWarning(string.Join("\n", errors));
            }
            else if (warnings != null && (preview.IsActive || source?.PreviewTarget != null))
            {
                warnings.style.display = DisplayStyle.None;
            }

            SyncOwnershipNote();
        }

        void SyncOwnershipNote()
        {
            if (ownershipNote == null) return;

            var elsewhere = previewOwner != null && previewOwner != this && previewOwner.preview.IsActive
                            && source != null && previewOwner.source?.Target == source.Target;

            ownershipNote.style.display = elsewhere ? DisplayStyle.Flex : DisplayStyle.None;
            if (elsewhere)
            {
                ownershipNote.text = "Previewing in the " + previewOwner.hostName
                                     + ". Starting a preview here stops that one.";
            }
        }
    }
}
