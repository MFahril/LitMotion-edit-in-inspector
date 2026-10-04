using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LitMotion.TweenEditor.Editor
{
    /// <summary>
    /// One step drawn as a draggable, resizable bar on the timeline.
    /// </summary>
    /// <remarks>
    /// All edits go through <see cref="SerializedProperty"/> rather than the step object, so
    /// Undo, prefab overrides and multi-object editing behave the way the rest of the inspector
    /// does. Writes during a drag use <c>ApplyModifiedPropertiesWithoutUndo</c> and a single
    /// <c>Undo.RegisterCompleteObjectUndo</c> taken at drag start, which is what makes one drag
    /// collapse to one undo step instead of hundreds.
    /// </remarks>
    internal sealed class TweenClipElement : VisualElement
    {
        /// <summary>What part of the clip a drag is manipulating.</summary>
        enum DragMode
        {
            None,
            Move,
            TrimStart,
            TrimEnd,
        }

        readonly Image icon;
        readonly Label label;
        readonly Label durationLabel;
        readonly Image warningBadge;
        readonly VisualElement loopBadge;
        readonly VisualElement headHandle;
        readonly VisualElement tailHandle;

        string bindingProblem;

        SerializedProperty stepProperty;
        TweenStep step;

        DragMode dragMode;
        Vector2 dragOrigin;
        float dragStartTime;
        float dragDuration;
        bool selected;

        /// <summary>Index of this clip's step within the animation's Steps list.</summary>
        public int StepIndex { get; private set; }

        /// <summary>
        /// Raised when this clip is clicked. The flag is true when a modifier key asked for the
        /// click to extend the selection rather than replace it.
        /// </summary>
        public event Action<TweenClipElement, bool> Selected;

        /// <summary>
        /// Raised when a drag starts on this clip. The flag is true for a move, false for a trim,
        /// which is what tells the view whether the rest of the selection should follow.
        /// </summary>
        public event Action<TweenClipElement, bool> DragBegan;

        /// <summary>Raised continuously while dragging, so the timeline can redraw.</summary>
        public event Action Changed;

        /// <summary>Raised once a drag finishes, so the preview can rebuild just once.</summary>
        public event Action EditFinished;

        /// <summary>Supplies the timeline's current scale and snapping behaviour.</summary>
        public Func<TweenTimelineContext> ContextProvider { get; set; }

        /// <summary>Populates this clip's right-click menu. Supplied by the view.</summary>
        public Action<ContextualMenuPopulateEvent, TweenClipElement> MenuBuilder { get; set; }

        /// <summary>The step this clip is bound to.</summary>
        public TweenStep Step => step;

        public TweenClipElement()
        {
            style.position = Position.Absolute;
            style.height = TweenTimelineStyles.TrackHeight;
            style.overflow = Overflow.Hidden;
            style.paddingLeft = 4f;
            style.paddingRight = 5f;
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;
            TweenTimelineStyles.SetRadius(this, 3f);

            // An icon as well as a colour: the clip says what it is when it is too narrow for a
            // label, and to a reader who cannot tell the family colours apart.
            icon = new Image
            {
                pickingMode = PickingMode.Ignore,
                scaleMode = ScaleMode.ScaleToFit,
                style = { width = 12f, height = 12f, flexShrink = 0f, marginRight = 3f },
            };
            Add(icon);

            label = new Label
            {
                pickingMode = PickingMode.Ignore,
                style =
                {
                    color = TweenTimelineStyles.ClipText,
                    fontSize = 10f,
                    unityTextAlign = TextAnchor.MiddleLeft,
                    overflow = Overflow.Hidden,
                    flexShrink = 1f,
                    flexGrow = 1f,
                    paddingLeft = 0f,
                    marginLeft = 0f,
                },
            };
            Add(label);

            durationLabel = new Label
            {
                pickingMode = PickingMode.Ignore,
                style =
                {
                    color = new StyleColor(new Color(1f, 1f, 1f, 0.7f)),
                    fontSize = 9f,
                    flexShrink = 0f,
                    unityTextAlign = TextAnchor.MiddleRight,
                    marginLeft = 3f,
                    marginRight = 0f,
                    paddingRight = 0f,
                },
            };
            Add(durationLabel);

            // Not ignored for picking, so hovering it shows why the step will not bind.
            warningBadge = new Image
            {
                image = TweenTimelineStyles.WarningIcon,
                scaleMode = ScaleMode.ScaleToFit,
                style = { width = 12f, height = 12f, flexShrink = 0f, marginLeft = 2f, display = DisplayStyle.None },
            };
            Add(warningBadge);

            loopBadge = new Label("∞")
            {
                pickingMode = PickingMode.Ignore,
                style =
                {
                    position = Position.Absolute,
                    right = 3f,
                    color = TweenTimelineStyles.ClipText,
                    fontSize = 10f,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    display = DisplayStyle.None,
                },
            };
            Add(loopBadge);

            // Trim grabbing has always worked by proximity to the edge; these make the invisible
            // hit zone visible, but only while the pointer is over the clip, so a timeline at
            // rest stays clean.
            headHandle = BuildHandle(true);
            tailHandle = BuildHandle(false);
            Add(headHandle);
            Add(tailHandle);

            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<PointerCaptureOutEvent>(_ => dragMode = DragMode.None);

            RegisterCallback<PointerEnterEvent>(_ => ShowHandles(true));
            RegisterCallback<PointerLeaveEvent>(_ => ShowHandles(false));

            this.AddManipulator(new ContextualMenuManipulator(evt => MenuBuilder?.Invoke(evt, this)));
        }

        static VisualElement BuildHandle(bool head)
        {
            var handle = new VisualElement
            {
                pickingMode = PickingMode.Ignore,
                style =
                {
                    position = Position.Absolute,
                    top = 3f,
                    bottom = 3f,
                    width = 2f,
                    backgroundColor = new StyleColor(new Color(1f, 1f, 1f, 0.55f)),
                    display = DisplayStyle.None,
                },
            };

            if (head) handle.style.left = 2f;
            else handle.style.right = 2f;

            return handle;
        }

        void ShowHandles(bool visible)
        {
            // A zero-length marker has no edges to trim, so it gets no handles to suggest it does.
            var trimmable = visible && step != null && step.Type != TweenType.Callback;
            var display = trimmable ? DisplayStyle.Flex : DisplayStyle.None;

            headHandle.style.display = display;
            tailHandle.style.display = display;
        }

        /// <summary>Points this clip at a step and refreshes its geometry and appearance.</summary>
        public void Bind(SerializedProperty property, TweenStep target, int index)
        {
            stepProperty = property;
            step = target;
            StepIndex = index;
            Refresh();
        }

        /// <summary>Marks this clip as the selected one.</summary>
        public void SetSelected(bool value)
        {
            selected = value;
            ApplySelectionStyle();
        }

        /// <summary>Recomputes position, width, colour and label from the bound step.</summary>
        public void Refresh()
        {
            if (step == null) return;

            var context = ContextProvider?.Invoke() ?? TweenTimelineContext.Default;

            var isMarker = step.Type == TweenType.Callback;
            var duration = isMarker ? 0f : Mathf.Max(0f, step.Duration);

            style.left = step.StartTime * context.PixelsPerSecond;
            style.width = isMarker
                ? TweenTimelineStyles.MinClipWidth * 1.5f
                : Mathf.Max(TweenTimelineStyles.MinClipWidth, duration * context.PixelsPerSecond);

            var color = TweenTimelineStyles.ClipColor(step.Type);
            if (!step.Enabled) color.a = 0.35f;
            style.backgroundColor = color;

            var width = style.width.value.value;

            icon.image = TweenTimelineStyles.IconFor(step.Type);
            icon.style.display = icon.image != null && width >= 22f ? DisplayStyle.Flex : DisplayStyle.None;

            label.text = step.DisplayName;
            label.style.display = width < 48f ? DisplayStyle.None : DisplayStyle.Flex;

            // The length is what a drag changes, so it is drawn where the eye already is.
            durationLabel.text = isMarker ? string.Empty : duration.ToString("0.##") + "s";
            durationLabel.style.display = !isMarker && width >= 96f ? DisplayStyle.Flex : DisplayStyle.None;

            // A disabled step is inert, so whether it would bind is not worth shouting about.
            bindingProblem = null;
            if (step.Enabled) TweenBindingStatus.Check(step, context.Fallback, out bindingProblem);
            warningBadge.style.display = bindingProblem != null ? DisplayStyle.Flex : DisplayStyle.None;
            warningBadge.tooltip = bindingProblem;

            // An infinite step loop is clamped to one pass inside a sequence, so flag it here
            // rather than letting the author assume it repeats.
            loopBadge.style.display = step.Loops < 0 ? DisplayStyle.Flex : DisplayStyle.None;

            tooltip = BuildTooltip();
            ApplySelectionStyle();
        }

        /// <summary>Why this clip will not bind, or null when it will.</summary>
        public string BindingProblem => bindingProblem;

        string BuildTooltip()
        {
            var end = step.StartTime + Mathf.Max(0f, step.Duration);
            var text = step.DisplayName + "\n" + step.StartTime.ToString("0.###") + "s to " + end.ToString("0.###") + "s";

            if (step.Delay > 0f) text += "\ndelay " + step.Delay.ToString("0.###") + "s";
            if (step.Loops != 1) text += "\nloops " + (step.Loops < 0 ? "infinite (clamped to 1)" : step.Loops.ToString());
            if (!step.Enabled) text += "\n(disabled)";
            if (bindingProblem != null) text += "\n\nWill not bind: " + bindingProblem;

            return text;
        }

        void ApplySelectionStyle()
        {
            TweenTimelineStyles.SetBorder(this, selected ? 2f : 1f,
                selected ? TweenTimelineStyles.SelectionOutline : TweenTimelineStyles.Border);
        }

        // --- Interaction ---

        void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0 || step == null) return;

            // Ctrl or Shift extends the selection. Command too, so the shortcut is the one a Mac
            // author already has in their hands.
            var additive = (evt.modifiers & (EventModifiers.Control | EventModifiers.Shift
                                             | EventModifiers.Command)) != 0;
            Selected?.Invoke(this, additive);

            // Measured in the clip's own space: the event may have come from the warning badge,
            // whose local position would read as the clip's left edge and start a trim.
            dragMode = ResolveDragMode(this.WorldToLocal(evt.position).x);
            if (dragMode == DragMode.None) return;

            dragOrigin = evt.position;
            dragStartTime = step.StartTime;
            dragDuration = step.Duration;

            // One undo entry for the whole drag, registered before anything changes.
            if (stepProperty != null)
            {
                Undo.RegisterCompleteObjectUndo(
                    stepProperty.serializedObject.targetObject,
                    dragMode == DragMode.Move ? "Move Tween Clip" : "Resize Tween Clip");
            }

            DragBegan?.Invoke(this, dragMode == DragMode.Move);

            this.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            if (dragMode == DragMode.None || step == null) return;

            var context = ContextProvider?.Invoke() ?? TweenTimelineContext.Default;
            var deltaSeconds = (evt.position.x - dragOrigin.x) / Mathf.Max(1f, context.PixelsPerSecond);

            switch (dragMode)
            {
                case DragMode.Move:
                {
                    var start = Mathf.Max(0f, dragStartTime + deltaSeconds);
                    Write(context.Snap(start, StepIndex), step.Duration);
                    break;
                }

                case DragMode.TrimEnd:
                {
                    var end = context.Snap(dragStartTime + dragDuration + deltaSeconds, StepIndex);
                    Write(step.StartTime, Mathf.Max(0f, end - step.StartTime));
                    break;
                }

                case DragMode.TrimStart:
                {
                    // Trimming the head holds the tail still, which is what makes a clip's end
                    // stay put while its in-point moves.
                    var fixedEnd = dragStartTime + dragDuration;
                    var start = Mathf.Clamp(context.Snap(dragStartTime + deltaSeconds, StepIndex), 0f, fixedEnd);
                    Write(start, fixedEnd - start);
                    break;
                }
            }

            evt.StopPropagation();
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            if (dragMode == DragMode.None) return;

            dragMode = DragMode.None;
            this.ReleasePointer(evt.pointerId);

            // Commit once, which is also the signal for the preview to rebuild.
            stepProperty?.serializedObject.ApplyModifiedProperties();
            EditFinished?.Invoke();
            evt.StopPropagation();
        }

        void Write(float startTime, float duration)
        {
            if (stepProperty == null) return;

            var startProperty = stepProperty.FindPropertyRelative("StartTime");
            var durationProperty = stepProperty.FindPropertyRelative("Duration");

            if (startProperty != null) startProperty.floatValue = Mathf.Max(0f, startTime);
            if (durationProperty != null && step.Type != TweenType.Callback)
            {
                durationProperty.floatValue = Mathf.Max(0f, duration);
            }

            // Without undo during the drag: the single entry registered on pointer-down already
            // captured the pre-drag state, and one entry per pointer-move would bury the history.
            stepProperty.serializedObject.ApplyModifiedPropertiesWithoutUndo();
            stepProperty.serializedObject.Update();

            Refresh();
            Changed?.Invoke();
        }

        DragMode ResolveDragMode(float localX)
        {
            // A zero-length marker has no meaningful edges to grab.
            if (step.Type == TweenType.Callback) return DragMode.Move;

            var width = resolvedStyle.width;
            if (localX <= TweenTimelineStyles.ClipEdgeGrab) return DragMode.TrimStart;
            if (localX >= width - TweenTimelineStyles.ClipEdgeGrab) return DragMode.TrimEnd;
            return DragMode.Move;
        }
    }
}
