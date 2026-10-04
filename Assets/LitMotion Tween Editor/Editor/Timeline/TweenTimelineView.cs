using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LitMotion.TweenEditor.Editor
{
    /// <summary>
    /// The timeline: a track per step, with draggable clips, a ruler and a scrubbable playhead.
    /// </summary>
    /// <remarks>
    /// Headers are a fixed column; the ruler, lanes and playhead share one horizontally
    /// scrolling content element so they can never drift out of alignment.
    ///
    /// The view reads geometry from the live <see cref="TweenAnimation"/> but writes exclusively
    /// through <see cref="SerializedProperty"/>. Reading from the objects is safe precisely
    /// because <see cref="TweenStep"/> is a class: the instances survive
    /// <c>ApplyModifiedProperties</c>, so a drag does not invalidate them mid-gesture.
    ///
    /// Selection lives in <see cref="TweenTimelineContext"/> rather than here, because clips,
    /// snapping and the commands all need it.
    /// </remarks>
    internal sealed class TweenTimelineView : VisualElement
    {
        readonly TweenTimelineContext context = new();

        readonly VisualElement headerColumn;
        readonly VisualElement contentRoot;
        readonly VisualElement lanes;
        readonly VisualElement playhead;
        readonly VisualElement snapGuide;
        readonly VisualElement marquee;
        readonly TweenTimelineRuler ruler;
        readonly ScrollView scroll;
        readonly ScrollView bodyScroll;
        readonly Label durationLabel;
        readonly Label selectionLabel;
        readonly VisualElement emptyState;
        readonly Label emptyHint;
        readonly Button emptyAddButton;
        readonly TextField filterField;
        readonly Toggle onlyEnabledToggle;
        readonly Toggle snapToggle;
        readonly Slider zoomSlider;

        float? maxBodyHeight;

        readonly List<TweenClipElement> clips = new();

        /// <summary>Step index for each visible lane row, so a marquee can map Y back to a step.</summary>
        readonly List<int> visibleIndices = new();

        /// <summary>Start times captured when a group drag begins, keyed by step index.</summary>
        readonly Dictionary<int, float> dragOrigins = new();

        SerializedProperty stepsProperty;
        TweenAnimation animation;

        int dragPrimary = -1;
        float dragPrimaryOrigin;
        bool groupDragging;

        bool marqueeActive;
        Vector2 marqueeOrigin;

        /// <summary>Index of the primary selected step, or -1.</summary>
        public int SelectedIndex => context.PrimaryIndex;

        /// <summary>True while a clip drag is in progress.</summary>
        public bool IsEditing { get; private set; }

        /// <summary>Raised when the selected step changes.</summary>
        public event Action<int> StepSelected;

        /// <summary>Raised once a clip drag completes, so the preview rebuilds a single time.</summary>
        public event Action EditFinished;

        /// <summary>Raised when the ruler or playhead is dragged, with a time in seconds.</summary>
        public event Action<float> Scrubbed;

        /// <summary>Raised when the step list changes structurally and the owner must rebuild.</summary>
        public event Action StructureChanged;

        /// <summary>Raised when the author presses the play shortcut.</summary>
        public event Action PlayToggleRequested;

        /// <summary>
        /// Supplies the GameObject steps without a target resolve against, so clips can badge
        /// themselves when they will not bind.
        /// </summary>
        public Func<GameObject> TargetProvider
        {
            get => context.TargetProvider;
            set => context.TargetProvider = value;
        }

        /// <summary>
        /// Tallest the lanes grow before scrolling. Defaults to the Preferences value; a host
        /// with room to spare, such as the window, raises it.
        /// </summary>
        public float MaxBodyHeight
        {
            get => maxBodyHeight ?? TweenTimelineStyles.MaxBodyHeight;
            set
            {
                maxBodyHeight = value;
                bodyScroll.style.maxHeight = value;
            }
        }

        /// <summary>True when the timeline is showing <paramref name="target"/>.</summary>
        public bool IsBoundTo(TweenAnimation target) => animation == target;

        /// <summary>The animation being shown.</summary>
        public TweenAnimation Animation => animation;

        /// <summary>Indices of the selected steps; the last is the primary one.</summary>
        public IReadOnlyList<int> Selection => context.Selection;

        public TweenTimelineView()
        {
            style.marginTop = 4f;
            style.marginBottom = 4f;
            TweenTimelineStyles.SetBorder(this, 1f, TweenTimelineStyles.Border);
            TweenTimelineStyles.SetRadius(this, 3f);
            style.backgroundColor = TweenTimelineStyles.Background;

            // Focusable so the keymap works; clicking anywhere in the timeline takes focus.
            focusable = true;
            RegisterCallback<KeyDownEvent>(OnKeyDown);
            RegisterCallback<PointerDownEvent>(_ => Focus(), TrickleDown.TrickleDown);

            context.PixelsPerSecond = TweenTimelineStyles.DefaultPixelsPerSecond;
            context.SnapEnabled = TweenEditorSettings.instance.SnapEnabled;

            Add(BuildToolbar(out durationLabel, out selectionLabel, out filterField, out onlyEnabledToggle,
                out snapToggle, out zoomSlider));

            // A long animation would otherwise push the rest of the inspector off screen.
            bodyScroll = new ScrollView(ScrollViewMode.Vertical) { style = { maxHeight = TweenTimelineStyles.MaxBodyHeight } };
            bodyScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            Add(bodyScroll);

            var body = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            bodyScroll.Add(body);

            headerColumn = new VisualElement
            {
                style =
                {
                    width = TweenTimelineStyles.HeaderWidth,
                    flexShrink = 0f,
                    borderRightWidth = 1f,
                    borderRightColor = TweenTimelineStyles.Border,
                },
            };
            body.Add(headerColumn);

            scroll = new ScrollView(ScrollViewMode.Horizontal) { style = { flexGrow = 1f } };
            scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            body.Add(scroll);

            contentRoot = new VisualElement { style = { position = Position.Relative } };
            scroll.Add(contentRoot);

            ruler = new TweenTimelineRuler(() => context);
            ruler.Scrubbed += time => Scrubbed?.Invoke(time);
            contentRoot.Add(ruler);

            lanes = new VisualElement();
            contentRoot.Add(lanes);

            // Marquee and guide live above the lanes but must never take the pointer.
            marquee = new VisualElement
            {
                pickingMode = PickingMode.Ignore,
                style =
                {
                    position = Position.Absolute,
                    display = DisplayStyle.None,
                    backgroundColor = new StyleColor(new Color(
                        TweenTimelineStyles.SelectionOutline.r,
                        TweenTimelineStyles.SelectionOutline.g,
                        TweenTimelineStyles.SelectionOutline.b,
                        0.15f)),
                },
            };
            TweenTimelineStyles.SetBorder(marquee, 1f, TweenTimelineStyles.SelectionOutline);
            contentRoot.Add(marquee);

            snapGuide = new VisualElement
            {
                pickingMode = PickingMode.Ignore,
                style =
                {
                    position = Position.Absolute,
                    top = 0f,
                    bottom = 0f,
                    width = 1f,
                    display = DisplayStyle.None,
                    backgroundColor = TweenTimelineStyles.SelectionOutline,
                },
            };
            contentRoot.Add(snapGuide);

            playhead = new VisualElement
            {
                pickingMode = PickingMode.Ignore,
                style =
                {
                    position = Position.Absolute,
                    top = 0f,
                    bottom = 0f,
                    width = 1f,
                    backgroundColor = TweenTimelineStyles.Playhead,
                },
            };
            contentRoot.Add(playhead);

            // An empty timeline offers the one thing to do next instead of a blank strip.
            emptyState = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    paddingTop = 8f,
                    paddingBottom = 8f,
                    paddingLeft = 8f,
                },
            };

            emptyHint = new Label("No steps yet.")
            {
                style = { color = TweenTimelineStyles.RulerText, fontSize = 11f, marginRight = 6f },
            };
            emptyState.Add(emptyHint);

            emptyAddButton = new Button(ShowAddStepMenu)
            {
                text = "Add Step…",
                tooltip = "Choose a tween type to add at the end of the timeline",
            };
            emptyState.Add(emptyAddButton);
            Add(emptyState);

            // Zooming with the wheel keeps the hand on the timeline instead of the slider.
            contentRoot.RegisterCallback<WheelEvent>(OnWheel);

            // Dragging empty lane space rubber-bands a selection.
            lanes.RegisterCallback<PointerDownEvent>(OnLanesPointerDown);
            lanes.RegisterCallback<PointerMoveEvent>(OnLanesPointerMove);
            lanes.RegisterCallback<PointerUpEvent>(OnLanesPointerUp);
            lanes.AddManipulator(new ContextualMenuManipulator(BuildBackgroundMenu));
        }

        VisualElement BuildToolbar(out Label duration, out Label selectionCount, out TextField filter,
            out Toggle onlyEnabled, out Toggle snap, out Slider zoom)
        {
            var bar = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    paddingLeft = 4f,
                    paddingRight = 4f,
                    height = 22f,
                    borderBottomWidth = 1f,
                    borderBottomColor = TweenTimelineStyles.Border,
                },
            };

            bar.Add(IconButton("+", "Add a step", ShowAddStepMenu));
            bar.Add(IconButton("✕", "Delete the selected steps  (Del)", DeleteSelected));
            bar.Add(IconButton("⧉", "Duplicate the selected steps  (Ctrl+D)", DuplicateSelected));

            bar.Add(Divider());

            bar.Add(IconButton("Copy", "Copy the selected step  (Ctrl+C)", CopySelected));
            bar.Add(IconButton("Paste", "Paste a step at the playhead  (Ctrl+V)", PasteStep));

            bar.Add(Divider());

            filter = new TextField
            {
                isDelayed = false,
                tooltip = "Show only steps whose name or type matches",
                style = { width = 70f, marginRight = 2f },
            };
            filter.RegisterValueChangedCallback(_ => Rebuild());
            bar.Add(filter);

            onlyEnabled = new Toggle { value = false, tooltip = "Hide disabled steps" };
            bar.Add(onlyEnabled);
            onlyEnabled.RegisterValueChangedCallback(_ => Rebuild());

            bar.Add(new VisualElement { style = { flexGrow = 1f } });

            selectionCount = new Label
            {
                style = { fontSize = 9f, color = TweenTimelineStyles.RulerText, marginRight = 6f },
            };
            bar.Add(selectionCount);

            duration = new Label("0.00s")
            {
                style = { fontSize = 10f, color = TweenTimelineStyles.RulerText, marginRight = 6f },
            };
            bar.Add(duration);

            snap = new Toggle
            {
                value = context.SnapEnabled,
                tooltip = "Snap clip edges to the grid, other clips and the playhead",
            };

            // Remembered in Preferences, so the choice survives selecting another object.
            snap.RegisterValueChangedCallback(evt =>
            {
                context.SnapEnabled = evt.newValue;
                TweenEditorSettings.instance.SnapEnabled = evt.newValue;
            });
            snap.style.marginRight = 2f;
            bar.Add(snap);
            bar.Add(new Label("Snap") { style = { fontSize = 10f, color = TweenTimelineStyles.RulerText, marginRight = 4f } });

            bar.Add(IconButton("Fit", "Zoom to fit the whole animation  (F)", ZoomToFit));

            zoom = new Slider(TweenTimelineStyles.MinPixelsPerSecond, TweenTimelineStyles.MaxPixelsPerSecond)
            {
                value = context.PixelsPerSecond,
                tooltip = "Zoom. The mouse wheel zooms too, anchored at the cursor.",
                style = { width = 70f },
            };
            zoom.RegisterValueChangedCallback(evt =>
            {
                context.PixelsPerSecond = evt.newValue;
                RefreshGeometry();
            });
            bar.Add(zoom);

            return bar;
        }

        static VisualElement Divider()
        {
            return new VisualElement
            {
                style =
                {
                    width = 1f,
                    height = 14f,
                    marginLeft = 3f,
                    marginRight = 3f,
                    backgroundColor = TweenTimelineStyles.Border,
                },
            };
        }

        static Button IconButton(string text, string tooltip, Action action)
        {
            return new Button(action)
            {
                text = text,
                tooltip = tooltip,
                style = { fontSize = 10f, marginLeft = 1f, marginRight = 1f, paddingLeft = 5f, paddingRight = 5f },
            };
        }

        /// <summary>Points the timeline at an animation and rebuilds every track.</summary>
        public void Bind(SerializedProperty steps, TweenAnimation target)
        {
            stepsProperty = steps;
            animation = target;

            context.PruneSelection(animation?.Steps?.Count ?? 0);

            Rebuild();
        }

        /// <summary>Rebuilds every track row from scratch.</summary>
        public void Rebuild()
        {
            headerColumn.Clear();
            lanes.Clear();
            clips.Clear();
            visibleIndices.Clear();

            var count = animation?.Steps?.Count ?? 0;

            // Spacer so the header column lines up with the lanes, which sit below the ruler.
            headerColumn.Add(new VisualElement { style = { height = TweenTimelineStyles.RulerHeight } });

            for (var i = 0; i < count; i++)
            {
                var step = animation.Steps[i];
                if (step == null || !Matches(step)) continue;

                visibleIndices.Add(i);
                headerColumn.Add(BuildHeaderRow(step, i));
                lanes.Add(BuildLane(step, i));
            }

            emptyHint.text = animation == null
                ? "No animation selected."
                : count == 0
                    ? "No steps yet."
                    : "Nothing matches the filter.";
            emptyAddButton.style.display = animation != null && count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            emptyState.style.display = visibleIndices.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;

            ruler.Bind(animation);

            RefreshGeometry();
            ruler.RefreshLabels();
            RefreshSelectionLabel();
        }

        /// <summary>True when a step passes the filter field and the enabled-only toggle.</summary>
        bool Matches(TweenStep step)
        {
            if (onlyEnabledToggle != null && onlyEnabledToggle.value && !step.Enabled) return false;

            var query = filterField?.value;
            if (string.IsNullOrWhiteSpace(query)) return true;

            var needle = query.Trim().ToLowerInvariant();

            return step.DisplayName.ToLowerInvariant().Contains(needle)
                   || step.Type.ToString().ToLowerInvariant().Contains(needle);
        }

        VisualElement BuildHeaderRow(TweenStep step, int index)
        {
            var row = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    height = TweenTimelineStyles.TrackHeight,
                    marginBottom = TweenTimelineStyles.TrackSpacing,
                    paddingLeft = 3f,
                    backgroundColor = index % 2 == 0
                        ? TweenTimelineStyles.LaneBackground
                        : TweenTimelineStyles.LaneAlternate,
                },
            };

            var stepProperty = StepPropertyAt(index);
            var enabledProperty = stepProperty?.FindPropertyRelative("Enabled");

            var toggle = new Toggle
            {
                value = step.Enabled,
                tooltip = "Include this step when the animation plays  (M)",
                style = { marginRight = 2f },
            };
            toggle.RegisterValueChangedCallback(evt =>
            {
                if (enabledProperty == null) return;

                enabledProperty.boolValue = evt.newValue;
                stepProperty.serializedObject.ApplyModifiedProperties();
                Rebuild();
                EditFinished?.Invoke();
            });
            row.Add(toggle);

            var swatch = new VisualElement
            {
                pickingMode = PickingMode.Ignore,
                style =
                {
                    width = 3f,
                    height = 12f,
                    marginRight = 4f,
                    backgroundColor = TweenTimelineStyles.ClipColor(step.Type),
                },
            };
            row.Add(swatch);

            row.Add(new Label(step.DisplayName)
            {
                pickingMode = PickingMode.Ignore,
                tooltip = step.DisplayName,
                style =
                {
                    fontSize = 10f,
                    overflow = Overflow.Hidden,
                    flexGrow = 1f,
                    unityFontStyleAndWeight = context.IsSelected(index) ? FontStyle.Bold : FontStyle.Normal,
                },
            });

            row.RegisterCallback<PointerDownEvent>(evt =>
            {
                var additive = (evt.modifiers & (EventModifiers.Control | EventModifiers.Shift
                                                 | EventModifiers.Command)) != 0;
                Select(index, additive);
            });

            row.AddManipulator(new ContextualMenuManipulator(evt => BuildClipMenu(evt, index)));
            return row;
        }

        VisualElement BuildLane(TweenStep step, int index)
        {
            var lane = new VisualElement
            {
                style =
                {
                    position = Position.Relative,
                    height = TweenTimelineStyles.TrackHeight,
                    marginBottom = TweenTimelineStyles.TrackSpacing,
                    backgroundColor = index % 2 == 0
                        ? TweenTimelineStyles.LaneBackground
                        : TweenTimelineStyles.LaneAlternate,
                },
            };

            var clip = new TweenClipElement
            {
                ContextProvider = () => context,
                MenuBuilder = (evt, element) => BuildClipMenu(evt, element.StepIndex),
            };

            clip.Bind(StepPropertyAt(index), step, index);
            clip.SetSelected(context.IsSelected(index));

            clip.Selected += OnClipSelected;
            clip.DragBegan += OnClipDragBegan;
            clip.Changed += OnClipChanged;
            clip.EditFinished += OnClipEditFinished;

            clips.Add(clip);
            lane.Add(clip);
            return lane;
        }

        // --- Selection ---

        void OnClipSelected(TweenClipElement clip, bool additive)
        {
            IsEditing = true;
            context.DraggingStepIndex = clip.StepIndex;
            context.RebuildSnapTargets(animation, clip.StepIndex);

            Select(clip.StepIndex, additive);
        }

        void Select(int index, bool additive)
        {
            if (additive)
            {
                context.ToggleSelection(index);
            }
            else if (context.IsSelected(index))
            {
                // Clicking an already-selected clip keeps the selection, so a group can be
                // dragged without re-picking it -- but the clicked clip becomes the one the
                // inspector shows.
                context.MakePrimary(index);
            }
            else
            {
                context.SelectOnly(index);
            }

            ApplySelectionStyles();
            StepSelected?.Invoke(context.PrimaryIndex);
        }

        void ApplySelectionStyles()
        {
            for (var i = 0; i < clips.Count; i++)
            {
                clips[i].SetSelected(context.IsSelected(clips[i].StepIndex));
            }

            RefreshSelectionLabel();
        }

        void RefreshSelectionLabel()
        {
            if (selectionLabel == null) return;

            selectionLabel.text = context.Selection.Count > 1
                ? context.Selection.Count + " selected"
                : string.Empty;
        }

        /// <summary>
        /// Selects one step from outside the timeline, such as from the binding status list, and
        /// scrolls it into view.
        /// </summary>
        public void SelectStep(int index)
        {
            if (animation?.Steps == null || index < 0 || index >= animation.Steps.Count) return;

            context.SelectOnly(index);
            ApplySelectionStyles();

            // ScrollTo throws on a view that is not in a panel yet, such as one being built.
            for (var i = 0; i < clips.Count && scroll.panel != null; i++)
            {
                if (clips[i].StepIndex == index) scroll.ScrollTo(clips[i]);
            }

            StepSelected?.Invoke(context.PrimaryIndex);
        }

        /// <summary>
        /// Re-reads the Preferences-backed metrics and colours after they change.
        /// </summary>
        public void ApplySettings()
        {
            var settings = TweenEditorSettings.instance;

            context.SnapEnabled = settings.SnapEnabled;
            snapToggle.SetValueWithoutNotify(settings.SnapEnabled);

            zoomSlider.lowValue = TweenTimelineStyles.MinPixelsPerSecond;
            zoomSlider.highValue = TweenTimelineStyles.MaxPixelsPerSecond;
            context.PixelsPerSecond = Mathf.Clamp(context.PixelsPerSecond, zoomSlider.lowValue, zoomSlider.highValue);
            zoomSlider.SetValueWithoutNotify(context.PixelsPerSecond);

            if (!maxBodyHeight.HasValue) bodyScroll.style.maxHeight = TweenTimelineStyles.MaxBodyHeight;

            // Lane height and clip colours are baked in when rows are built.
            Rebuild();
        }

        /// <summary>Selects every visible step.</summary>
        void SelectAll()
        {
            context.Selection.Clear();
            for (var i = 0; i < visibleIndices.Count; i++) context.Selection.Add(visibleIndices[i]);

            ApplySelectionStyles();
            StepSelected?.Invoke(context.PrimaryIndex);
        }

        // --- Group drag ---

        void OnClipDragBegan(TweenClipElement clip, bool isMove)
        {
            dragOrigins.Clear();
            groupDragging = false;
            dragPrimary = clip.StepIndex;

            // Only a move carries the rest of the selection. Trimming is per-clip: dragging one
            // clip's edge should not resize everything that happens to be selected.
            if (!isMove || context.Selection.Count < 2 || animation?.Steps == null) return;

            for (var i = 0; i < context.Selection.Count; i++)
            {
                var index = context.Selection[i];
                if (index < 0 || index >= animation.Steps.Count) continue;

                var step = animation.Steps[index];
                if (step != null) dragOrigins[index] = step.StartTime;
            }

            if (!dragOrigins.TryGetValue(dragPrimary, out dragPrimaryOrigin)) return;

            groupDragging = dragOrigins.Count > 1;
        }

        void OnClipChanged()
        {
            if (groupDragging) DragFollowers();

            RefreshDurationLabel();
            RefreshSnapGuide();
        }

        /// <summary>
        /// Moves the rest of the selection by the same delta the dragged clip just took.
        /// </summary>
        /// <remarks>
        /// The followers are written without snapping, deliberately: the dragged clip is the one
        /// the author is aiming, and re-snapping each follower would shear the group apart.
        /// </remarks>
        void DragFollowers()
        {
            if (stepsProperty == null || animation?.Steps == null) return;
            if (dragPrimary < 0 || dragPrimary >= animation.Steps.Count) return;

            var primary = animation.Steps[dragPrimary];
            if (primary == null) return;

            var delta = primary.StartTime - dragPrimaryOrigin;

            // Clamp so the leftmost clip in the group stops at zero and the shape is preserved.
            foreach (var pair in dragOrigins)
            {
                if (pair.Key == dragPrimary) continue;
                delta = Mathf.Max(delta, -pair.Value);
            }

            foreach (var pair in dragOrigins)
            {
                if (pair.Key == dragPrimary) continue;
                if (pair.Key < 0 || pair.Key >= stepsProperty.arraySize) continue;

                var start = stepsProperty.GetArrayElementAtIndex(pair.Key)
                    .FindPropertyRelative("StartTime");

                if (start != null) start.floatValue = Mathf.Max(0f, pair.Value + delta);
            }

            stepsProperty.serializedObject.ApplyModifiedPropertiesWithoutUndo();
            stepsProperty.serializedObject.Update();

            for (var i = 0; i < clips.Count; i++)
            {
                if (clips[i].StepIndex != dragPrimary) clips[i].Refresh();
            }
        }

        void OnClipEditFinished()
        {
            IsEditing = false;
            context.DraggingStepIndex = -1;
            context.LastSnappedTime = null;

            dragOrigins.Clear();
            groupDragging = false;
            dragPrimary = -1;

            RefreshSnapGuide();
            RefreshGeometry();
            EditFinished?.Invoke();
        }

        void RefreshSnapGuide()
        {
            var snapped = IsEditing ? context.LastSnappedTime : null;

            if (!snapped.HasValue)
            {
                snapGuide.style.display = DisplayStyle.None;
                return;
            }

            snapGuide.style.display = DisplayStyle.Flex;
            snapGuide.style.left = snapped.Value * context.PixelsPerSecond;
        }

        // --- Marquee ---

        void OnLanesPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0) return;

            var additive = (evt.modifiers & (EventModifiers.Control | EventModifiers.Shift
                                             | EventModifiers.Command)) != 0;

            // A plain click on empty space clears the selection, which is the only way to get
            // back to "nothing selected" without reaching for the inspector.
            if (!additive)
            {
                context.Selection.Clear();
                ApplySelectionStyles();
                StepSelected?.Invoke(-1);
            }

            marqueeActive = true;
            marqueeOrigin = LanesLocal(evt.position);

            marquee.style.display = DisplayStyle.Flex;
            UpdateMarquee(marqueeOrigin);

            lanes.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        void OnLanesPointerMove(PointerMoveEvent evt)
        {
            if (!marqueeActive) return;

            UpdateMarquee(LanesLocal(evt.position));
            evt.StopPropagation();
        }

        void OnLanesPointerUp(PointerUpEvent evt)
        {
            if (!marqueeActive) return;

            marqueeActive = false;
            marquee.style.display = DisplayStyle.None;
            lanes.ReleasePointer(evt.pointerId);

            SelectWithinMarquee(marqueeOrigin, LanesLocal(evt.position));
            evt.StopPropagation();
        }

        /// <summary>
        /// Converts a pointer position into lanes-container space.
        /// </summary>
        /// <remarks>
        /// The handlers are on <c>lanes</c> but the events arrive having bubbled from an
        /// individual lane row, so <c>localPosition</c> would be measured from that row's own
        /// top-left and every Y would resolve to row zero.
        /// </remarks>
        Vector2 LanesLocal(Vector3 pointerPosition)
        {
            return lanes.WorldToLocal(new Vector2(pointerPosition.x, pointerPosition.y));
        }

        void UpdateMarquee(Vector2 current)
        {
            var left = Mathf.Min(marqueeOrigin.x, current.x);
            var right = Mathf.Max(marqueeOrigin.x, current.x);
            var top = Mathf.Min(marqueeOrigin.y, current.y);
            var bottom = Mathf.Max(marqueeOrigin.y, current.y);

            // The marquee sits in contentRoot, whose origin is the ruler's top edge.
            marquee.style.left = left;
            marquee.style.width = Mathf.Max(1f, right - left);
            marquee.style.top = top + TweenTimelineStyles.RulerHeight;
            marquee.style.height = Mathf.Max(1f, bottom - top);
        }

        /// <summary>
        /// Selects every visible clip whose row and time range intersect the marquee.
        /// </summary>
        void SelectWithinMarquee(Vector2 from, Vector2 to)
        {
            if (animation?.Steps == null) return;

            var pixelsPerSecond = Mathf.Max(1f, context.PixelsPerSecond);
            var startTime = Mathf.Min(from.x, to.x) / pixelsPerSecond;
            var endTime = Mathf.Max(from.x, to.x) / pixelsPerSecond;

            var rowHeight = TweenTimelineStyles.TrackHeight + TweenTimelineStyles.TrackSpacing;
            var firstRow = Mathf.FloorToInt(Mathf.Min(from.y, to.y) / rowHeight);
            var lastRow = Mathf.FloorToInt(Mathf.Max(from.y, to.y) / rowHeight);

            // A click rather than a drag: nothing to add, and the clear already happened.
            if (Mathf.Abs(to.x - from.x) < 3f && Mathf.Abs(to.y - from.y) < 3f) return;

            for (var row = Mathf.Max(0, firstRow); row <= lastRow && row < visibleIndices.Count; row++)
            {
                var index = visibleIndices[row];
                var step = animation.Steps[index];
                if (step == null) continue;

                var clipEnd = step.StartTime + Mathf.Max(0f, step.Duration);
                var overlaps = clipEnd >= startTime && step.StartTime <= endTime;

                if (overlaps && !context.IsSelected(index)) context.Selection.Add(index);
            }

            ApplySelectionStyles();
            StepSelected?.Invoke(context.PrimaryIndex);
        }

        // --- Playhead and geometry ---

        /// <summary>Moves the playhead marker. Does not scrub; the owner drives the preview.</summary>
        public void SetPlayheadTime(float seconds)
        {
            context.PlayheadTime = Mathf.Max(0f, seconds);
            playhead.style.left = context.PlayheadTime * context.PixelsPerSecond;
        }

        /// <summary>Re-reads every clip's geometry and resizes the scrollable content.</summary>
        public void RefreshGeometry()
        {
            for (var i = 0; i < clips.Count; i++) clips[i].Refresh();

            var duration = animation?.Duration ?? 0f;
            if (float.IsPositiveInfinity(duration)) duration = 0f;

            // Extra trailing room so a clip can always be dragged past the current end.
            var width = Mathf.Max(400f, duration * context.PixelsPerSecond + 160f);
            contentRoot.style.width = width;
            ruler.style.width = width;

            SetPlayheadTime(context.PlayheadTime);
            RefreshDurationLabel();
            ruler.RefreshLabels();
        }

        void RefreshDurationLabel()
        {
            var duration = animation?.Duration ?? 0f;
            durationLabel.text = float.IsPositiveInfinity(duration) ? "∞" : duration.ToString("0.00") + "s";
        }

        void OnWheel(WheelEvent evt)
        {
            // Shift pans instead of zooming, which is what a horizontal timeline wants most.
            if ((evt.modifiers & EventModifiers.Shift) != 0)
            {
                scroll.scrollOffset = new Vector2(
                    Mathf.Max(0f, scroll.scrollOffset.x + evt.delta.y * 20f),
                    scroll.scrollOffset.y);

                evt.StopPropagation();
                return;
            }

            // Anchored zoom: the time under the cursor stays under the cursor.
            var pixelsPerSecond = Mathf.Max(1f, context.PixelsPerSecond);
            var cursorInViewport = evt.localMousePosition.x - scroll.scrollOffset.x;
            var timeUnderCursor = evt.localMousePosition.x / pixelsPerSecond;

            context.PixelsPerSecond = TweenTimelineCommands.ZoomStep(context.PixelsPerSecond, evt.delta.y);

            RefreshGeometry();

            scroll.scrollOffset = new Vector2(
                TweenTimelineCommands.AnchoredScroll(timeUnderCursor, cursorInViewport, context.PixelsPerSecond),
                scroll.scrollOffset.y);

            evt.StopPropagation();
        }

        /// <summary>Scales the timeline so the whole animation is visible.</summary>
        void ZoomToFit()
        {
            var duration = animation?.Duration ?? 0f;

            context.PixelsPerSecond = TweenTimelineCommands.FitPixelsPerSecond(duration, scroll.contentRect.width);
            RefreshGeometry();

            scroll.scrollOffset = new Vector2(0f, scroll.scrollOffset.y);
        }

        SerializedProperty StepPropertyAt(int index)
        {
            if (stepsProperty == null || index < 0 || index >= stepsProperty.arraySize) return null;
            return stepsProperty.GetArrayElementAtIndex(index);
        }

        // --- Keyboard ---

        void OnKeyDown(KeyDownEvent evt)
        {
            // Bare letters are shortcuts here, so typing "shake" into the filter must not split,
            // mute and zoom-to-fit along the way.
            if (IsTextEntry(evt.target as VisualElement)) return;

            var control = (evt.modifiers & (EventModifiers.Control | EventModifiers.Command)) != 0;
            var shift = (evt.modifiers & EventModifiers.Shift) != 0;
            var alt = (evt.modifiers & EventModifiers.Alt) != 0;

            switch (evt.keyCode)
            {
                case KeyCode.Space:
                    PlayToggleRequested?.Invoke();
                    break;

                case KeyCode.Delete:
                case KeyCode.Backspace:
                    DeleteSelected();
                    break;

                case KeyCode.D when control:
                    DuplicateSelected();
                    break;

                case KeyCode.C when control:
                    CopySelected();
                    break;

                case KeyCode.V when control:
                    PasteStep();
                    break;

                case KeyCode.A when control:
                    SelectAll();
                    break;

                case KeyCode.S:
                    SplitSelectedAtPlayhead();
                    break;

                case KeyCode.M:
                    ToggleSelectedEnabled();
                    break;

                case KeyCode.F:
                    ZoomToFit();
                    break;

                case KeyCode.Home:
                    Scrubbed?.Invoke(0f);
                    break;

                case KeyCode.End:
                    Scrubbed?.Invoke(animation?.Duration ?? 0f);
                    break;

                case KeyCode.UpArrow when alt:
                    MoveSelectedRow(-1);
                    break;

                case KeyCode.DownArrow when alt:
                    MoveSelectedRow(1);
                    break;

                case KeyCode.LeftArrow:
                    NudgeSelected(-NudgeStep(shift));
                    break;

                case KeyCode.RightArrow:
                    NudgeSelected(NudgeStep(shift));
                    break;

                default:
                    return;
            }

            evt.StopPropagation();
        }

        static float NudgeStep(bool coarse)
        {
            return TweenTimelineStyles.SnapInterval * (coarse ? 10f : 1f);
        }

        /// <summary>True when the element, or anything containing it, takes typed text.</summary>
        static bool IsTextEntry(VisualElement element)
        {
            for (var current = element; current != null; current = current.parent)
            {
                if (current is TextField or TextElement && current is not Label) return true;
            }

            return false;
        }

        // --- Context menus ---

        void BuildClipMenu(ContextualMenuPopulateEvent evt, int index)
        {
            // Right-clicking a clip that is not in the selection acts on that clip, which is
            // what every other timeline does.
            if (!context.IsSelected(index)) Select(index, false);

            var many = context.Selection.Count > 1;
            var suffix = many ? " (" + context.Selection.Count + ")" : string.Empty;
            var step = StepAt(index);

            evt.menu.AppendAction("Duplicate" + suffix, _ => DuplicateSelected());
            evt.menu.AppendAction("Delete" + suffix, _ => DeleteSelected());
            evt.menu.AppendSeparator();

            evt.menu.AppendAction("Copy", _ => CopySelected());
            evt.menu.AppendAction("Paste at Playhead", _ => PasteStep(),
                TweenStepClipboard.HasStep ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            evt.menu.AppendSeparator();

            evt.menu.AppendAction("Split at Playhead", _ => SplitSelectedAtPlayhead(),
                TweenTimelineCommands.CanSplit(step, context.PlayheadTime)
                    ? DropdownMenuAction.Status.Normal
                    : DropdownMenuAction.Status.Disabled);

            evt.menu.AppendAction("Start at Playhead", _ =>
                ApplyPlan(TweenTimelineCommands.SetStart(animation, index, context.PlayheadTime),
                    "Move Tween Clip"));

            evt.menu.AppendAction("Trim Start to Playhead", _ =>
                ApplyPlan(TweenTimelineCommands.TrimStartTo(animation, index, context.PlayheadTime),
                    "Trim Tween Clip"));

            evt.menu.AppendAction("Trim End to Playhead", _ =>
                ApplyPlan(TweenTimelineCommands.TrimEndTo(animation, index, context.PlayheadTime),
                    "Trim Tween Clip"));

            evt.menu.AppendSeparator();

            var groupStatus = many ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled;
            evt.menu.AppendAction("Align Starts", _ =>
                ApplyPlan(TweenTimelineCommands.AlignStarts(animation, context.Selection), "Align Tween Clips"),
                groupStatus);

            evt.menu.AppendAction("Align Ends", _ =>
                ApplyPlan(TweenTimelineCommands.AlignEnds(animation, context.Selection), "Align Tween Clips"),
                groupStatus);

            evt.menu.AppendAction("Distribute Evenly", _ =>
                ApplyPlan(TweenTimelineCommands.Distribute(animation, context.Selection), "Distribute Tween Clips"),
                context.Selection.Count > 2 ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);

            evt.menu.AppendSeparator();

            evt.menu.AppendAction(step != null && step.Enabled ? "Mute" : "Unmute",
                _ => ToggleSelectedEnabled());

            evt.menu.AppendAction("Solo (disable the others)", _ => SoloSelected());
            evt.menu.AppendAction("Reset to Type Defaults", _ => ResetSelectedToDefaults());

            evt.menu.AppendSeparator();
            evt.menu.AppendAction("Save Selection as Preset…" + suffix, _ => SaveSelectionAsPreset());

            evt.menu.AppendSeparator();
            evt.menu.AppendAction("Move Up", _ => MoveSelectedRow(-1),
                index > 0 ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            evt.menu.AppendAction("Move Down", _ => MoveSelectedRow(1),
                stepsProperty != null && index < stepsProperty.arraySize - 1
                    ? DropdownMenuAction.Status.Normal
                    : DropdownMenuAction.Status.Disabled);
        }

        void BuildBackgroundMenu(ContextualMenuPopulateEvent evt)
        {
            evt.menu.AppendAction("Add Step…", _ => ShowAddStepMenu());
            evt.menu.AppendAction("Paste at Playhead", _ => PasteStep(),
                TweenStepClipboard.HasStep ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            evt.menu.AppendSeparator();
            evt.menu.AppendAction("Select All", _ => SelectAll());
            evt.menu.AppendAction("Zoom to Fit", _ => ZoomToFit());
        }

        TweenStep StepAt(int index)
        {
            if (animation?.Steps == null || index < 0 || index >= animation.Steps.Count) return null;
            return animation.Steps[index];
        }

        // --- Commands ---

        void ApplyPlan(List<TweenClipTiming> plan, string undoName)
        {
            if (!TweenTimelineCommands.Apply(stepsProperty, plan, undoName)) return;

            stepsProperty.serializedObject.Update();
            RefreshGeometry();
            EditFinished?.Invoke();
        }

        /// <summary>
        /// Writes just the selected clips out as a preset, so one good flourish inside a larger
        /// animation can be reused without carrying the rest along.
        /// </summary>
        void SaveSelectionAsPreset()
        {
            if (animation == null || context.Selection.Count == 0) return;

            var extracted = TweenTimelineCommands.ExtractSelection(animation, context.Selection);
            TweenPresetFiles.SaveWithPrompt(extracted, extracted.Id);
        }

        void NudgeSelected(float deltaSeconds)
        {
            ApplyPlan(TweenTimelineCommands.Nudge(animation, context.Selection, deltaSeconds),
                "Nudge Tween Clips");
        }

        /// <summary>
        /// Cuts the primary clip in two at the playhead, the second half becoming a new step.
        /// </summary>
        void SplitSelectedAtPlayhead()
        {
            var index = context.PrimaryIndex;
            var step = StepAt(index);
            if (stepsProperty == null || step == null) return;

            if (!TweenTimelineCommands.TrySplit(step, context.PlayheadTime,
                    out var firstDuration, out var secondStart, out var secondDuration))
            {
                return;
            }

            var serialized = stepsProperty.serializedObject;
            Undo.RegisterCompleteObjectUndo(serialized.targetObject, "Split Tween Clip");

            // Unity's array duplicate copies the element verbatim, including UnityEvent
            // listeners, which a manual clone cannot do.
            stepsProperty.InsertArrayElementAtIndex(index);
            serialized.ApplyModifiedProperties();
            serialized.Update();

            var head = stepsProperty.GetArrayElementAtIndex(index);
            head.FindPropertyRelative("Duration").floatValue = firstDuration;

            var tail = stepsProperty.GetArrayElementAtIndex(index + 1);
            tail.FindPropertyRelative("StartTime").floatValue = secondStart;
            tail.FindPropertyRelative("Duration").floatValue = secondDuration;

            serialized.ApplyModifiedProperties();

            context.SelectOnly(index + 1);
            StructureChanged?.Invoke();
        }

        void ToggleSelectedEnabled()
        {
            if (stepsProperty == null || context.Selection.Count == 0) return;

            var primary = StepAt(context.PrimaryIndex);
            if (primary == null) return;

            // The primary clip decides the new state so a mixed selection resolves one way
            // rather than flipping each clip independently.
            var enabled = !primary.Enabled;

            var serialized = stepsProperty.serializedObject;
            Undo.RegisterCompleteObjectUndo(serialized.targetObject, enabled ? "Unmute Tween Step" : "Mute Tween Step");

            for (var i = 0; i < context.Selection.Count; i++)
            {
                var element = StepPropertyAt(context.Selection[i]);
                var property = element?.FindPropertyRelative("Enabled");
                if (property != null) property.boolValue = enabled;
            }

            serialized.ApplyModifiedProperties();
            Rebuild();
            EditFinished?.Invoke();
        }

        /// <summary>Leaves the selection enabled and disables everything else.</summary>
        void SoloSelected()
        {
            if (stepsProperty == null || animation?.Steps == null) return;

            var serialized = stepsProperty.serializedObject;
            Undo.RegisterCompleteObjectUndo(serialized.targetObject, "Solo Tween Step");

            for (var i = 0; i < stepsProperty.arraySize; i++)
            {
                var property = stepsProperty.GetArrayElementAtIndex(i).FindPropertyRelative("Enabled");
                if (property != null) property.boolValue = context.IsSelected(i);
            }

            serialized.ApplyModifiedProperties();
            Rebuild();
            EditFinished?.Invoke();
        }

        /// <summary>
        /// Puts the selected steps back to their type's defaults, keeping where they sit on the
        /// timeline.
        /// </summary>
        void ResetSelectedToDefaults()
        {
            if (stepsProperty == null || animation?.Steps == null || context.Selection.Count == 0) return;

            var serialized = stepsProperty.serializedObject;
            Undo.RegisterCompleteObjectUndo(serialized.targetObject, "Reset Tween Step");

            for (var i = 0; i < context.Selection.Count; i++)
            {
                var step = StepAt(context.Selection[i]);
                if (step == null) continue;

                TweenStepDefaults.Apply(step, step.Type, step.StartTime);
            }

            EditorUtility.SetDirty(serialized.targetObject);
            serialized.Update();

            Rebuild();
            EditFinished?.Invoke();
        }

        /// <summary>
        /// Moves the primary step up or down the list.
        /// </summary>
        /// <remarks>
        /// List order is display order only -- timing comes from each step's StartTime -- so
        /// this is purely about grouping related clips next to each other.
        /// </remarks>
        void MoveSelectedRow(int offset)
        {
            var index = context.PrimaryIndex;
            if (stepsProperty == null || index < 0) return;

            var target = index + offset;
            if (target < 0 || target >= stepsProperty.arraySize) return;

            var serialized = stepsProperty.serializedObject;
            Undo.RegisterCompleteObjectUndo(serialized.targetObject, "Reorder Tween Step");

            stepsProperty.MoveArrayElement(index, target);
            serialized.ApplyModifiedProperties();

            context.SelectOnly(target);
            StructureChanged?.Invoke();
        }

        // --- Structural edits ---

        void ShowAddStepMenu()
        {
            if (stepsProperty == null) return;

            var menu = new GenericMenu();
            AddTypeGroup(menu, "Transform", TweenType.Move, TweenType.Scale, TweenType.Rotate,
                TweenType.Jump, TweenType.Punch, TweenType.Shake);
            AddTypeGroup(menu, "Rect Transform", TweenType.SizeDelta, TweenType.Pivot, TweenType.Anchors);
            AddTypeGroup(menu, "Graphics", TweenType.Fade, TweenType.Color, TweenType.FillAmount);
            AddTypeGroup(menu, "Text", TweenType.TextReveal, TweenType.TextCounter,
                TweenType.TextScramble, TweenType.TMPCharacter);
            AddTypeGroup(menu, "Rendering", TweenType.MaterialProperty, TweenType.VolumeWeight);
            AddTypeGroup(menu, "Camera", TweenType.CameraProperty);
            AddTypeGroup(menu, "Audio", TweenType.AudioVolume, TweenType.AudioPitch);
            AddTypeGroup(menu, "Timeline", TweenType.Interval, TweenType.Callback, TweenType.Custom);

            // Channels defined in the project's own code, grouped under their own categories.
            var extensions = TweenExtensionRegistry.All;
            if (extensions.Count > 0) menu.AddSeparator(string.Empty);

            for (var i = 0; i < extensions.Count; i++)
            {
                var entry = extensions[i];
                var id = entry.Id;
                menu.AddItem(new GUIContent(entry.Category + "/" + entry.DisplayName), false,
                    () => AddStep(TweenType.Extension, id));
            }

            menu.ShowAsContext();
        }

        void AddTypeGroup(GenericMenu menu, string group, params TweenType[] types)
        {
            for (var i = 0; i < types.Length; i++)
            {
                var type = types[i];
                var label = group + "/" + ObjectNames.NicifyVariableName(type.ToString());

                menu.AddItem(new GUIContent(label), false, () => AddStep(type));
            }
        }

        /// <summary>
        /// Applies the author's preferred duration and ease to a freshly defaulted step.
        /// </summary>
        /// <remarks>
        /// Only where the type kept the shipped baseline: a type that deliberately picks its own
        /// length or ease (a shake that needs time to settle, say) keeps it, so a preference
        /// cannot make a new step of that type look broken.
        /// </remarks>
        internal static void ApplyPreferredDefaults(TweenStep step, float duration, Ease ease)
        {
            if (step == null) return;

            if (step.Type != TweenType.Callback
                && Mathf.Approximately(step.Duration, TweenEditorSettings.DefaultStepDuration))
            {
                step.Duration = duration;
            }

            if (step.Ease == TweenEditorSettings.DefaultStepEase) step.Ease = ease;
        }

        void AddStep(TweenType type, string extensionId = null)
        {
            if (stepsProperty == null) return;

            var serialized = stepsProperty.serializedObject;
            Undo.RegisterCompleteObjectUndo(serialized.targetObject, "Add Tween Step");

            var index = stepsProperty.arraySize;
            stepsProperty.InsertArrayElementAtIndex(index);
            serialized.ApplyModifiedProperties();

            // The new element inherits the previous one's values, so reset it to a usable default
            // and place it after whatever is already on the timeline.
            if (animation != null && index < animation.Steps.Count)
            {
                var step = animation.Steps[index];
                if (step != null)
                {
                    var previousEnd = 0f;
                    for (var i = 0; i < index; i++)
                    {
                        var other = animation.Steps[i];
                        if (other == null) continue;

                        var end = other.StartTime + Mathf.Max(0f, other.Duration);
                        if (!float.IsInfinity(end) && end > previousEnd) previousEnd = end;
                    }

                    TweenStepDefaults.Apply(step, type, previousEnd);
                    if (type == TweenType.Extension) step.ExtensionId = extensionId ?? string.Empty;

                    var settings = TweenEditorSettings.instance;
                    ApplyPreferredDefaults(step, settings.DefaultDuration, settings.DefaultEase);
                }
            }

            serialized.Update();
            EditorUtility.SetDirty(serialized.targetObject);

            context.SelectOnly(index);
            StructureChanged?.Invoke();
        }

        void DeleteSelected()
        {
            if (stepsProperty == null || context.Selection.Count == 0) return;

            var serialized = stepsProperty.serializedObject;
            Undo.RegisterCompleteObjectUndo(serialized.targetObject, "Delete Tween Step");

            // Descending, so each removal cannot invalidate the indices still to be removed.
            var ordered = new List<int>(context.Selection);
            ordered.Sort();

            for (var i = ordered.Count - 1; i >= 0; i--)
            {
                if (ordered[i] < 0 || ordered[i] >= stepsProperty.arraySize) continue;

                stepsProperty.DeleteArrayElementAtIndex(ordered[i]);
            }

            serialized.ApplyModifiedProperties();

            var remaining = stepsProperty.arraySize;
            context.SelectOnly(remaining == 0 ? -1 : Mathf.Min(ordered[0], remaining - 1));

            StructureChanged?.Invoke();
        }

        void DuplicateSelected()
        {
            if (stepsProperty == null || context.Selection.Count == 0) return;

            var serialized = stepsProperty.serializedObject;
            Undo.RegisterCompleteObjectUndo(serialized.targetObject, "Duplicate Tween Step");

            var ordered = new List<int>(context.Selection);
            ordered.Sort();

            for (var i = ordered.Count - 1; i >= 0; i--)
            {
                if (ordered[i] < 0 || ordered[i] >= stepsProperty.arraySize) continue;

                // Unity's array duplicate copies the element verbatim, including UnityEvent
                // listeners, which a manual clone cannot do.
                stepsProperty.InsertArrayElementAtIndex(ordered[i]);
            }

            serialized.ApplyModifiedProperties();

            context.SelectOnly(ordered[ordered.Count - 1] + 1);
            StructureChanged?.Invoke();
        }

        void CopySelected()
        {
            var step = StepAt(context.PrimaryIndex);
            if (step == null) return;

            TweenStepClipboard.CopyStep(step);
        }

        /// <summary>
        /// Pastes the clipboard step, placed at the playhead rather than at the end.
        /// </summary>
        void PasteStep()
        {
            if (stepsProperty == null || !TweenStepClipboard.HasStep) return;

            var serialized = stepsProperty.serializedObject;
            Undo.RegisterCompleteObjectUndo(serialized.targetObject, "Paste Tween Step");

            var index = stepsProperty.arraySize;
            stepsProperty.InsertArrayElementAtIndex(index);
            serialized.ApplyModifiedProperties();
            serialized.Update();

            if (animation != null && index < animation.Steps.Count)
            {
                var step = animation.Steps[index];
                if (TweenStepClipboard.PasteInto(step)) step.StartTime = context.PlayheadTime;
            }

            EditorUtility.SetDirty(serialized.targetObject);
            context.SelectOnly(index);
            StructureChanged?.Invoke();
        }
    }
}
