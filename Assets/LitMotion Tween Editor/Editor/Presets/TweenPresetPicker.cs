using System;
using UnityEditor;
using UnityEngine;

namespace LitMotion.TweenEditor.Editor
{
    /// <summary>
    /// Offers the project's tween presets as a context menu.
    /// </summary>
    /// <remarks>
    /// A menu rather than Unity's object picker: the picker reports its result through an IMGUI
    /// event (<c>ObjectSelectorClosed</c>) that a UI Toolkit inspector has no natural place to
    /// pump, whereas a menu hands the choice straight to a callback. It also shows the folder
    /// each preset came from, which matters once a project has both shared and per-feature ones.
    /// </remarks>
    internal static class TweenPresetPicker
    {
        /// <summary>Shows the preset menu, invoking <paramref name="onPicked"/> on a choice.</summary>
        public static void Show(Action<TweenAnimationAsset> onPicked)
        {
            if (onPicked == null) return;

            var guids = AssetDatabase.FindAssets("t:" + nameof(TweenAnimationAsset));
            var menu = new GenericMenu();

            if (guids.Length == 0)
            {
                menu.AddDisabledItem(new GUIContent("No tween presets in this project"));
                menu.AddSeparator(string.Empty);
                menu.AddItem(new GUIContent("Create the built-in presets"), false,
                    TweenPresetGenerator.GenerateAll);
                menu.ShowAsContext();
                return;
            }

            AppendTo(menu, string.Empty, onPicked);
            menu.ShowAsContext();
        }

        /// <summary>
        /// Adds every preset to an existing menu under <paramref name="prefix"/>, so a menu with
        /// other entries can offer presets as a submenu.
        /// </summary>
        public static void AppendTo(GenericMenu menu, string prefix, Action<TweenAnimationAsset> onPicked)
        {
            if (menu == null || onPicked == null) return;

            var guids = AssetDatabase.FindAssets("t:" + nameof(TweenAnimationAsset));
            if (guids.Length == 0)
            {
                menu.AddDisabledItem(new GUIContent(prefix + "No presets in this project"));
                menu.AddItem(new GUIContent(prefix + "Create the built-in presets"), false,
                    TweenPresetGenerator.GenerateAll);
                return;
            }

            for (var i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var asset = AssetDatabase.LoadAssetAtPath<TweenAnimationAsset>(path);
                if (asset == null) continue;

                // Grouped by folder, so presets from different features stay distinguishable
                // even when they share a name.
                var folder = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path));
                var label = string.IsNullOrEmpty(folder) ? asset.name : folder + "/" + asset.name;

                var captured = asset;
                menu.AddItem(new GUIContent(prefix + label), false, () => onPicked(captured));
            }
        }
    }
}
