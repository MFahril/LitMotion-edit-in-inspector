using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace LitMotion.TweenEditor.Editor
{
    /// <summary>
    /// Every tween preset in the project, searchable, with a curve thumbnail and the means to
    /// apply one: <b>Window → LitMotion → Tween Presets</b>.
    /// </summary>
    /// <remarks>
    /// The preset picker menu answers "which one?" only for an author who already knows the
    /// name. This shows what each preset is -- its main ease, how many steps, how long -- and
    /// lets one be dragged onto a player's animation chips or added with a button.
    /// </remarks>
    public sealed class TweenPresetBrowser : EditorWindow
    {
        /// <summary>One preset, with the numbers the list shows and filters on.</summary>
        internal readonly struct Entry
        {
            public Entry(TweenAnimationAsset asset, string folder)
            {
                Asset = asset;
                Folder = folder;
            }

            public TweenAnimationAsset Asset { get; }
            public string Folder { get; }
            public string Name => Asset == null ? string.Empty : Asset.name;
        }

        ToolbarSearchField search;
        ScrollView list;
        Label countLabel;

        readonly List<Entry> entries = new();

        [MenuItem("Window/LitMotion/Tween Presets")]
        public static void ShowWindow()
        {
            var window = GetWindow<TweenPresetBrowser>();
            window.titleContent = new GUIContent("Tween Presets", TweenTimelineStyles.Tool("Favorite"));
            window.Show();
        }

        void CreateGUI()
        {
            var toolbar = new Toolbar();

            search = new ToolbarSearchField { tooltip = "Filter by name, folder, step type or ease" };
            search.style.flexGrow = 1f;
            search.RegisterValueChangedCallback(_ => Populate());
            toolbar.Add(search);

            toolbar.Add(new ToolbarButton(Reload)
            {
                text = "Refresh",
                tooltip = "Look for presets again",
            });

            toolbar.Add(new ToolbarButton(() =>
            {
                TweenPresetGenerator.GenerateAll();
                Reload();
            })
            {
                text = "Generate Built-ins",
                tooltip = "Write (or rewrite) the 14 built-in presets",
            });

            rootVisualElement.Add(toolbar);

            countLabel = TweenUi.Dim(string.Empty);
            countLabel.style.marginLeft = 4f;
            countLabel.style.marginTop = 2f;
            rootVisualElement.Add(countLabel);

            list = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1f } };
            rootVisualElement.Add(list);

            Reload();
        }

        void OnProjectChange() => Reload();

        void Reload()
        {
            entries.Clear();
            entries.AddRange(FindAll());
            Populate();
        }

        /// <summary>Every preset in the project, sorted by folder then name.</summary>
        internal static List<Entry> FindAll()
        {
            var found = new List<Entry>();
            var guids = AssetDatabase.FindAssets("t:" + nameof(TweenAnimationAsset));

            for (var i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var asset = AssetDatabase.LoadAssetAtPath<TweenAnimationAsset>(path);
                if (asset == null) continue;

                found.Add(new Entry(asset, System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path))));
            }

            found.Sort((a, b) =>
            {
                var byFolder = string.CompareOrdinal(a.Folder, b.Folder);
                return byFolder != 0 ? byFolder : string.CompareOrdinal(a.Name, b.Name);
            });

            return found;
        }

        /// <summary>True when the preset matches a search, on name, folder, step types or eases.</summary>
        internal static bool Matches(Entry entry, string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return true;
            if (entry.Asset == null) return false;

            var needle = query.Trim().ToLowerInvariant();
            if (entry.Name.ToLowerInvariant().Contains(needle)) return true;
            if (!string.IsNullOrEmpty(entry.Folder) && entry.Folder.ToLowerInvariant().Contains(needle)) return true;

            var steps = entry.Asset.Animation?.Steps;
            if (steps == null) return false;

            for (var i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                if (step == null) continue;

                if (step.Type.ToString().ToLowerInvariant().Contains(needle)) return true;
                if (step.Ease.ToString().ToLowerInvariant().Contains(needle)) return true;
            }

            return false;
        }

        /// <summary>
        /// The ease that characterises a preset: that of its longest enabled step, which is the
        /// motion the eye follows.
        /// </summary>
        internal static Ease SignatureEase(TweenAnimation animation, out AnimationCurve curve)
        {
            curve = null;
            var ease = Ease.Linear;
            var longest = -1f;

            if (animation?.Steps == null) return ease;

            for (var i = 0; i < animation.Steps.Count; i++)
            {
                var step = animation.Steps[i];
                if (step == null || !step.Enabled || TweenBindingResolver.IsNonBinding(step.Type)) continue;
                if (step.Duration <= longest) continue;

                longest = step.Duration;
                ease = step.Ease;
                curve = step.CustomCurve;
            }

            return ease;
        }

        void Populate()
        {
            if (list == null) return;

            list.Clear();
            var query = search?.value;
            var shown = 0;

            for (var i = 0; i < entries.Count; i++)
            {
                if (!Matches(entries[i], query)) continue;

                list.Add(BuildRow(entries[i]));
                shown++;
            }

            countLabel.text = entries.Count == 0
                ? "No presets in this project yet. Generate the built-ins, or save one from any animation."
                : shown + " of " + entries.Count + " presets · drag one onto a player's animation chips";
        }

        VisualElement BuildRow(Entry entry)
        {
            var asset = entry.Asset;
            var animation = asset.Animation;

            var row = TweenUi.Row();
            row.style.paddingLeft = 4f;
            row.style.paddingRight = 4f;
            row.style.paddingTop = 3f;
            row.style.paddingBottom = 3f;
            row.style.borderBottomWidth = 1f;
            row.style.borderBottomColor = TweenTimelineStyles.Border;
            row.tooltip = "Drag onto a player's animation chips to add a copy";

            var ease = SignatureEase(animation, out var curve);
            var graph = new EaseCurveElement
            {
                pickingMode = PickingMode.Ignore,
                style = { width = 54f, height = 34f, flexShrink = 0f, marginRight = 6f },
            };
            graph.SetEase(ease, curve);
            graph.ClearGhost();
            row.Add(graph);

            var text = new VisualElement { pickingMode = PickingMode.Ignore, style = { flexGrow = 1f, flexShrink = 1f } };
            text.Add(new Label(entry.Name) { style = { unityFontStyleAndWeight = FontStyle.Bold } });

            var steps = animation?.EnabledStepCount ?? 0;
            var duration = animation?.Duration ?? 0f;
            var length = float.IsPositiveInfinity(duration) ? "∞" : duration.ToString("0.##") + "s";
            var loops = animation != null && animation.Loops < 0 ? " · loops forever" : string.Empty;

            text.Add(TweenUi.Dim(entry.Folder + " · " + steps + (steps == 1 ? " step" : " steps") + " · " + length
                                 + " · " + ObjectNames.NicifyVariableName(ease.ToString()) + loops));
            row.Add(text);

            row.Add(new Button(() => AddToSelectedPlayer(asset))
            {
                text = "Add to Player",
                tooltip = "Add a copy of this preset as a new animation on the selected TweenPlayer",
            });

            row.Add(new Button(() =>
            {
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
            })
            {
                text = "Edit",
                tooltip = "Select the preset to edit and preview it in the inspector",
            });

            RegisterDrag(row, asset);
            return row;
        }

        static void RegisterDrag(VisualElement row, TweenAnimationAsset asset)
        {
            var pressed = false;
            var origin = Vector2.zero;

            row.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                pressed = true;
                origin = evt.position;
            });

            row.RegisterCallback<PointerUpEvent>(_ => pressed = false);
            row.RegisterCallback<PointerLeaveEvent>(_ => pressed = false);

            row.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!pressed || ((Vector2)evt.position - origin).sqrMagnitude < 36f) return;

                pressed = false;
                DragAndDrop.PrepareStartDrag();
                DragAndDrop.objectReferences = new Object[] { asset };
                DragAndDrop.StartDrag(asset.name);
            });
        }

        static void AddToSelectedPlayer(TweenAnimationAsset asset)
        {
            var player = Selection.activeGameObject == null
                ? null
                : Selection.activeGameObject.GetComponent<TweenPlayer>();

            if (player == null)
            {
                EditorUtility.DisplayDialog("Add Preset",
                    "Select a GameObject with a Tween Player first.", "OK");
                return;
            }

            var source = new TweenPlayerSource(new SerializedObject(player));
            TweenAnimationListCommands.Add(source, asset.Animation);
        }
    }
}
