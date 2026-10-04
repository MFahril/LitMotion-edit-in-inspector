using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LitMotion.TweenEditor.Editor
{
    /// <summary>
    /// A dropdown gallery of every ease, drawn as curves.
    /// </summary>
    /// <remarks>
    /// Easing is a visual choice and an enum dropdown makes it a naming quiz: "OutBack" and
    /// "OutElastic" tell you nothing about which one overshoots further. Showing all 31 curves
    /// at once turns the decision back into looking at shapes.
    ///
    /// Shown as a dropdown window rather than a <c>PopupWindowContent</c> because the cells are
    /// UI Toolkit elements that paint themselves with Painter2D, and popup content is IMGUI.
    /// </remarks>
    internal sealed class TweenEasePicker : EditorWindow
    {
        const float CellWidth = 86f;
        const float CellHeight = 56f;

        Action<Ease> onPicked;
        Action onCustomCurvePicked;
        Ease current;

        ScrollView body;
        TextField search;

        /// <summary>
        /// Opens the gallery under <paramref name="anchor"/>.
        /// </summary>
        /// <param name="anchor">Screen rect of the control that opened it.</param>
        /// <param name="selected">Ease to mark as current.</param>
        /// <param name="onPick">Receives the chosen ease.</param>
        /// <param name="onPickCustomCurve">Invoked when the author chooses a custom curve instead.</param>
        public static void Open(Rect anchor, Ease selected, Action<Ease> onPick,
            Action onPickCustomCurve = null)
        {
            var window = CreateInstance<TweenEasePicker>();
            window.current = selected;
            window.onPicked = onPick;
            window.onCustomCurvePicked = onPickCustomCurve;

            // The contents are built from CreateGUI, which Unity calls once the window has a
            // visual tree. Touching rootVisualElement before showing is not guaranteed to work.
            window.ShowAsDropDown(anchor, new Vector2(CellWidth * 3f + 34f, 420f));
        }

        void CreateGUI()
        {
            rootVisualElement.Clear();
            rootVisualElement.style.paddingTop = 4f;
            rootVisualElement.style.paddingLeft = 4f;
            rootVisualElement.style.paddingRight = 4f;
            rootVisualElement.style.paddingBottom = 4f;

            search = new TextField { value = string.Empty };
            search.RegisterValueChangedCallback(_ => RefreshBody());
            rootVisualElement.Add(search);

            var hint = new Label("Type to filter. Right-click a curve to star it.")
            {
                style = { fontSize = 9f, color = TweenTimelineStyles.RulerText, marginBottom = 2f },
            };
            rootVisualElement.Add(hint);

            body = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1f } };
            rootVisualElement.Add(body);

            if (onCustomCurvePicked != null)
            {
                var custom = new Button(() =>
                {
                    onCustomCurvePicked();
                    Close();
                })
                {
                    text = "Custom curve…",
                    tooltip = "Hand easing over to an AnimationCurve on this step",
                    style = { marginTop = 2f },
                };
                rootVisualElement.Add(custom);
            }

            RefreshBody();

            // Focus goes to the search field so the window is type-to-filter from the first key.
            rootVisualElement.schedule.Execute(() => search.Focus()).ExecuteLater(1);
        }

        void RefreshBody()
        {
            body.Clear();

            var matches = TweenEaseCatalog.Filter(search?.value);
            if (matches.Count == 0)
            {
                body.Add(new Label("Nothing matches that.")
                {
                    style = { fontSize = 10f, color = TweenTimelineStyles.RulerText, marginTop = 6f },
                });
                return;
            }

            var query = string.IsNullOrWhiteSpace(search?.value);

            // Favourites and recents are only useful when the author has not asked for
            // something specific, so a query collapses the view to one flat list of matches.
            if (query)
            {
                AddGroup("Favourites", Intersect(TweenEaseCatalog.Favourites, matches));
                AddGroup("Recent", Intersect(TweenEaseCatalog.Recents, matches));

                for (var i = 0; i < TweenEaseCatalog.Families.Length; i++)
                {
                    var family = TweenEaseCatalog.Families[i];
                    AddGroup(family, TweenEaseCatalog.InFamily(family, matches));
                }
            }
            else
            {
                AddGroup(matches.Count + " matches", matches);
            }
        }

        static List<Ease> Intersect(List<Ease> ordered, List<Ease> allowed)
        {
            var results = new List<Ease>();
            for (var i = 0; i < ordered.Count; i++)
            {
                if (allowed.Contains(ordered[i])) results.Add(ordered[i]);
            }

            return results;
        }

        void AddGroup(string title, List<Ease> eases)
        {
            if (eases == null || eases.Count == 0) return;

            body.Add(new Label(title)
            {
                style =
                {
                    fontSize = 10f,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    color = TweenTimelineStyles.RulerText,
                    marginTop = 6f,
                    marginBottom = 2f,
                },
            });

            var row = NewRow();
            body.Add(row);

            for (var i = 0; i < eases.Count; i++)
            {
                if (i > 0 && i % 3 == 0)
                {
                    row = NewRow();
                    body.Add(row);
                }

                row.Add(BuildCell(eases[i]));
            }
        }

        static VisualElement NewRow()
        {
            return new VisualElement { style = { flexDirection = FlexDirection.Row } };
        }

        VisualElement BuildCell(Ease ease)
        {
            var cell = new VisualElement
            {
                tooltip = ease.ToString(),
                style =
                {
                    width = CellWidth,
                    marginRight = 2f,
                    marginBottom = 2f,
                    paddingTop = 2f,
                    paddingBottom = 2f,
                },
            };

            var isCurrent = ease == current;
            TweenTimelineStyles.SetBorder(cell, 1f,
                isCurrent ? TweenTimelineStyles.SelectionOutline : TweenTimelineStyles.Border);
            TweenTimelineStyles.SetRadius(cell, 3f);

            var graph = new EaseCurveElement { style = { height = CellHeight, marginBottom = 1f } };
            graph.SetEase(ease, null);

            // A thumbnail shows one ease, not a comparison: the ghost only belongs on the
            // inspector graph where an ease was just changed.
            graph.ClearGhost();
            cell.Add(graph);

            var starred = TweenEaseCatalog.IsFavourite(ease);
            cell.Add(new Label((starred ? "★ " : string.Empty) + ease)
            {
                style =
                {
                    fontSize = 9f,
                    unityTextAlign = TextAnchor.MiddleCenter,
                    color = TweenTimelineStyles.RulerText,
                    unityFontStyleAndWeight = isCurrent ? FontStyle.Bold : FontStyle.Normal,
                    overflow = Overflow.Hidden,
                },
            });

            cell.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == 1)
                {
                    // Right-click stars without choosing, so building a shortlist does not
                    // close the window on every star.
                    TweenEaseCatalog.ToggleFavourite(ease);
                    RefreshBody();
                    evt.StopPropagation();
                    return;
                }

                TweenEaseCatalog.PushRecent(ease);
                onPicked?.Invoke(ease);
                Close();
            });

            return cell;
        }
    }
}
