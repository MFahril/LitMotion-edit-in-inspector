using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LitMotion.TweenEditor.Editor
{
    /// <summary>
    /// A player's animations as a row of chips, replacing the old dropdown.
    /// </summary>
    /// <remarks>
    /// A dropdown hid every animation but the current one, so "what does this object do?" took
    /// a click per animation to answer. Chips show the whole set at once and give each its own
    /// play button, rename on double-click, and a menu for the rest.
    ///
    /// The bar only raises requests; the view owns the data and decides what each one means.
    /// </remarks>
    internal sealed class TweenAnimationBar : VisualElement
    {
        readonly VisualElement chips;
        readonly VisualElement emptyState;
        readonly List<VisualElement> chipElements = new();

        int count;

        public event Action<int> Selected;
        public event Action<int> PlayRequested;
        public event Action<int, string> RenameRequested;
        public event Action<int> DuplicateRequested;
        public event Action<int, int> MoveRequested;
        public event Action<int> RemoveRequested;
        public event Action AddEmptyRequested;
        public event Action<TweenAnimationAsset> AddPresetRequested;

        public TweenAnimationBar()
        {
            style.marginTop = 2f;
            style.marginBottom = 2f;

            chips = TweenUi.Row(true);
            Add(chips);

            emptyState = TweenUi.Box();
            emptyState.Add(new Label("This player has no animations yet.")
            {
                style = { unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 2f },
            });
            emptyState.Add(TweenUi.Dim("An animation is a named timeline of steps, played by its id. " +
                                       "Start empty, or from one of the project's presets."));

            var actions = TweenUi.Row(true);
            actions.style.marginTop = 4f;
            actions.Add(new Button(() => AddEmptyRequested?.Invoke())
            {
                text = "Add Animation",
                tooltip = "Add an empty animation named after the first free built-in id",
            });
            actions.Add(new Button(ShowPresetMenu)
            {
                text = "Add From Preset…",
                tooltip = "Add a copy of a preset as a new animation you can edit freely",
            });
            emptyState.Add(actions);
            Add(emptyState);

            // Dropping a preset anywhere on the bar adds it, which is the drag half of the
            // preset browser.
            RegisterCallback<DragUpdatedEvent>(OnDragUpdated);
            RegisterCallback<DragPerformEvent>(OnDragPerform);
        }

        /// <summary>Redraws every chip.</summary>
        /// <param name="ids">Each animation's id, in list order.</param>
        /// <param name="selected">Index of the current animation.</param>
        /// <param name="playing">Index of the animation being previewed, or -1.</param>
        public void Refresh(IReadOnlyList<string> ids, int selected, int playing)
        {
            chips.Clear();
            chipElements.Clear();
            count = ids?.Count ?? 0;

            emptyState.style.display = count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            chips.style.display = count == 0 ? DisplayStyle.None : DisplayStyle.Flex;

            for (var i = 0; i < count; i++)
            {
                var chip = BuildChip(ids[i], i, i == selected, i == playing);
                chipElements.Add(chip);
                chips.Add(chip);
            }

            if (count > 0)
            {
                chips.Add(new Button(ShowAddMenu)
                {
                    text = "+",
                    tooltip = "Add an animation, empty or from a preset. You can also drop a preset here.",
                    style = { height = 20f, marginLeft = 2f, paddingLeft = 6f, paddingRight = 6f },
                });
            }
        }

        VisualElement BuildChip(string id, int index, bool selected, bool playing)
        {
            var chip = new VisualElement
            {
                tooltip = "Click to edit · double-click to rename · right-click for more",
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    height = 20f,
                    marginRight = 3f,
                    marginBottom = 2f,
                    paddingLeft = 6f,
                    paddingRight = 1f,
                    backgroundColor = selected ? TweenTimelineStyles.ChipSelected : TweenTimelineStyles.ChipIdle,
                },
            };
            TweenTimelineStyles.SetRadius(chip, 10f);
            TweenTimelineStyles.SetBorder(chip, 1f,
                playing ? TweenTimelineStyles.Playhead : TweenTimelineStyles.Border);

            var label = new Label(string.IsNullOrEmpty(id) ? "(no id)" : id)
            {
                pickingMode = PickingMode.Ignore,
                style =
                {
                    fontSize = 11f,
                    unityFontStyleAndWeight = selected ? FontStyle.Bold : FontStyle.Normal,
                    marginRight = 2f,
                },
            };
            chip.Add(label);

            var play = TweenUi.IconButton("PlayButton", "▶", "Preview this animation", () => PlayRequested?.Invoke(index));
            play.style.height = 16f;
            play.style.minWidth = 18f;
            play.style.backgroundColor = new StyleColor(Color.clear);
            TweenTimelineStyles.SetBorder(play, 0f, Color.clear);
            chip.Add(play);

            chip.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;

                if (evt.clickCount >= 2)
                {
                    BeginRename(chip, label, id, index);
                }
                else
                {
                    Selected?.Invoke(index);
                }

                evt.StopPropagation();
            });

            chip.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                evt.menu.AppendAction("Play", _ => PlayRequested?.Invoke(index));
                evt.menu.AppendAction("Rename", _ => BeginRename(chip, label, id, index));
                evt.menu.AppendAction("Duplicate", _ => DuplicateRequested?.Invoke(index));
                evt.menu.AppendSeparator();
                evt.menu.AppendAction("Move Left", _ => MoveRequested?.Invoke(index, -1),
                    index > 0 ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                evt.menu.AppendAction("Move Right", _ => MoveRequested?.Invoke(index, 1),
                    index < count - 1 ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                evt.menu.AppendSeparator();
                evt.menu.AppendAction("Delete", _ => RemoveRequested?.Invoke(index));
            }));

            return chip;
        }

        /// <summary>Swaps the chip's label for a text field until Enter, Escape or focus loss.</summary>
        void BeginRename(VisualElement chip, Label label, string id, int index)
        {
            var field = new TextField
            {
                value = id ?? string.Empty,
                tooltip = "Enter to rename, Escape to cancel",
                style = { minWidth = 70f, height = 18f, marginLeft = 0f, marginRight = 2f },
            };

            var done = false;

            void Finish(bool commit)
            {
                if (done) return;
                done = true;

                var value = field.value?.Trim();
                field.RemoveFromHierarchy();
                label.style.display = DisplayStyle.Flex;

                if (commit && !string.IsNullOrEmpty(value) && value != id) RenameRequested?.Invoke(index, value);
            }

            field.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode is KeyCode.Return or KeyCode.KeypadEnter)
                {
                    Finish(true);
                    evt.StopPropagation();
                }
                else if (evt.keyCode == KeyCode.Escape)
                {
                    Finish(false);
                    evt.StopPropagation();
                }
            }, TrickleDown.TrickleDown);

            field.RegisterCallback<FocusOutEvent>(_ => Finish(true));

            label.style.display = DisplayStyle.None;
            chip.Insert(0, field);

            field.schedule.Execute(() =>
            {
                field.Focus();
                field.SelectAll();
            });
        }

        void ShowAddMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Empty Animation"), false, () => AddEmptyRequested?.Invoke());
            menu.AddSeparator(string.Empty);
            TweenPresetPicker.AppendTo(menu, "From Preset/", asset => AddPresetRequested?.Invoke(asset));
            menu.ShowAsContext();
        }

        void ShowPresetMenu()
        {
            TweenPresetPicker.Show(asset => AddPresetRequested?.Invoke(asset));
        }

        // --- Drag and drop ---

        static List<TweenAnimationAsset> DraggedPresets()
        {
            var presets = new List<TweenAnimationAsset>();
            var dragged = DragAndDrop.objectReferences;
            if (dragged == null) return presets;

            for (var i = 0; i < dragged.Length; i++)
            {
                if (dragged[i] is TweenAnimationAsset asset) presets.Add(asset);
            }

            return presets;
        }

        void OnDragUpdated(DragUpdatedEvent evt)
        {
            if (DraggedPresets().Count == 0) return;

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            evt.StopPropagation();
        }

        void OnDragPerform(DragPerformEvent evt)
        {
            var presets = DraggedPresets();
            if (presets.Count == 0) return;

            DragAndDrop.AcceptDrag();
            for (var i = 0; i < presets.Count; i++) AddPresetRequested?.Invoke(presets[i]);

            evt.StopPropagation();
        }
    }
}
