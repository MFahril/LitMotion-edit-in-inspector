using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace LitMotion.TweenEditor.Editor
{
    /// <summary>
    /// A dockable home for the tween editor: <b>Window → LitMotion → Tween Editor</b>.
    /// </summary>
    /// <remarks>
    /// The inspector is a narrow column shared with every other component on the object, which
    /// is fine for a tweak and cramped for real timeline work. This hosts the same
    /// <see cref="TweenAuthoringView"/> laid out wide, with the clip inspector beside the
    /// timeline instead of under it.
    ///
    /// It follows the selection -- a <see cref="TweenPlayer"/> or a preset asset -- unless
    /// locked, in which case it keeps editing one target while the author selects other things
    /// in the scene, which is exactly what picking a step's target needs.
    /// </remarks>
    public sealed class TweenEditorWindow : EditorWindow
    {
        [SerializeField] Object target;
        [SerializeField] bool locked;

        TweenAuthoringView view;
        SerializedObject serialized;
        VisualElement content;
        ObjectField targetField;
        Toggle lockToggle;

        [MenuItem("Window/LitMotion/Tween Editor")]
        public static void ShowWindow()
        {
            var window = GetWindow<TweenEditorWindow>();
            window.titleContent = Title();
            window.Show();
        }

        /// <summary>Opens the window on a specific player or preset and locks it there.</summary>
        public static TweenEditorWindow Open(Object editTarget)
        {
            var window = GetWindow<TweenEditorWindow>();
            window.titleContent = Title();
            window.locked = editTarget != null;
            window.SetTarget(editTarget);
            window.Show();
            return window;
        }

        static GUIContent Title()
        {
            return new GUIContent("Tween Editor", TweenTimelineStyles.Tool("UnityEditor.AnimationWindow"));
        }

        /// <summary>The player or preset being edited, or null.</summary>
        public Object Target => target;

        /// <summary>The hosted view, for tests.</summary>
        internal TweenAuthoringView View => view;

        void OnDisable()
        {
            view?.Dispose();
            view = null;
        }

        void CreateGUI()
        {
            titleContent = Title();

            var root = rootVisualElement;
            root.style.paddingLeft = 4f;
            root.style.paddingRight = 4f;
            root.style.paddingTop = 2f;

            root.Add(BuildToolbar());

            content = new VisualElement { style = { flexGrow = 1f } };
            root.Add(content);

            if (target == null && !locked) target = FromSelection();
            Rebuild();
        }

        VisualElement BuildToolbar()
        {
            var bar = new Toolbar();

            targetField = new ObjectField
            {
                objectType = typeof(Object),
                allowSceneObjects = true,
                value = target,
                tooltip = "The player or preset being edited. Drop one here, or select it with the window unlocked.",
                style = { minWidth = 160f, flexGrow = 1f, maxWidth = 320f },
            };
            targetField.RegisterValueChangedCallback(evt =>
            {
                var resolved = Normalize(evt.newValue);
                if (resolved == null && evt.newValue != null)
                {
                    // Not something this window can edit; put the field back.
                    targetField.SetValueWithoutNotify(target);
                    return;
                }

                SetTarget(resolved);
            });
            bar.Add(targetField);

            lockToggle = new ToolbarToggle
            {
                text = "Lock",
                value = locked,
                tooltip = "Keep editing this target while selecting other objects, e.g. to pick a step's target",
            };
            lockToggle.RegisterValueChangedCallback(evt =>
            {
                locked = evt.newValue;
                if (!locked) SetTarget(FromSelection() ?? target);
            });
            bar.Add(lockToggle);

            bar.Add(new ToolbarSpacer { style = { flexGrow = 1f } });

            bar.Add(new ToolbarButton(() => view?.ToggleKeymap())
            {
                text = "Keys",
                tooltip = "Show every shortcut and gesture",
            });

            bar.Add(new ToolbarButton(TweenPresetBrowser.ShowWindow)
            {
                text = "Presets",
                tooltip = "Browse the project's tween presets; drag one onto the animation chips to add it",
            });

            bar.Add(new ToolbarButton(() => SettingsService.OpenUserPreferences(TweenEditorSettingsProvider.Path))
            {
                text = "Preferences",
                tooltip = "Zoom range, snapping, lane height, clip colours and defaults for new steps",
            });

            return bar;
        }

        void OnSelectionChange()
        {
            if (locked) return;

            var picked = FromSelection();
            if (picked != null && picked != target) SetTarget(picked);
            else if (target == null) Rebuild();   // the empty state names the selection
        }

        void OnHierarchyChange()
        {
            // The edited player was deleted, or a TweenPlayer was just added to the selection.
            if (target == null || (view != null && !view.Source.IsValid))
            {
                if (!locked) target = FromSelection();
                Rebuild();
            }
        }

        /// <summary>Points the window at a new player or preset.</summary>
        public void SetTarget(Object editTarget)
        {
            editTarget = Normalize(editTarget);
            if (editTarget == target && view != null) return;

            target = editTarget;
            targetField?.SetValueWithoutNotify(target);
            lockToggle?.SetValueWithoutNotify(locked);
            Rebuild();
        }

        /// <summary>The editable object behind a selection: a player, its GameObject, or a preset.</summary>
        static Object Normalize(Object candidate)
        {
            switch (candidate)
            {
                case TweenPlayer player:
                    return player;
                case TweenAnimationAsset asset:
                    return asset;
                case GameObject go:
                    return go.GetComponent<TweenPlayer>();
                case Component component:
                    return component.GetComponent<TweenPlayer>();
                default:
                    return null;
            }
        }

        static Object FromSelection() => Normalize(Selection.activeObject);

        void Rebuild()
        {
            if (content == null) return;

            view?.Dispose();
            view = null;
            content.Clear();

            if (target == null)
            {
                content.Add(BuildEmptyState());
                return;
            }

            serialized = new SerializedObject(target);

            TweenAuthoringSource source = target is TweenAnimationAsset
                ? new TweenAssetSource(serialized)
                : new TweenPlayerSource(serialized);

            view = new TweenAuthoringView(source, TweenAuthoringView.Layout.Wide, "Tween Editor window");
            content.Add(view);

            // No blanket Bind here: fields added after a bind are not bound by it, so each part
            // of the view binds what it builds, when it builds it.

            titleContent = new GUIContent("Tween Editor", Title().image,
                "Editing " + (target == null ? "nothing" : target.name));
        }

        VisualElement BuildEmptyState()
        {
            var box = TweenUi.Box();
            box.style.marginTop = 12f;

            box.Add(new Label("Nothing to edit")
            {
                style = { unityFontStyleAndWeight = FontStyle.Bold, fontSize = 13f, marginBottom = 4f },
            });
            box.Add(TweenUi.Dim("Select a GameObject with a Tween Player, or a Tween Animation preset asset. " +
                                "The window follows the selection until you lock it.", 11f));

            var selected = Selection.activeGameObject;
            if (selected != null && selected.GetComponent<TweenPlayer>() == null)
            {
                box.Add(new Button(() =>
                {
                    var player = Undo.AddComponent<TweenPlayer>(selected);
                    SetTarget(player);
                })
                {
                    text = "Add Tween Player to " + selected.name,
                    tooltip = "Add a TweenPlayer component to the selected GameObject and start editing it",
                    style = { alignSelf = Align.FlexStart, marginTop = 8f },
                });
            }

            return box;
        }
    }
}
