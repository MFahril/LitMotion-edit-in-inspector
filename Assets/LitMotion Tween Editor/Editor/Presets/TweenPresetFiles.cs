using UnityEditor;
using UnityEngine;

namespace LitMotion.TweenEditor.Editor
{
    /// <summary>Writes animations out as <see cref="TweenAnimationAsset"/> presets.</summary>
    internal static class TweenPresetFiles
    {
        /// <summary>
        /// Asks where to save, then writes a portable copy of <paramref name="animation"/>.
        /// </summary>
        /// <returns>The created asset, or null when cancelled.</returns>
        public static TweenAnimationAsset SaveWithPrompt(TweenAnimation animation, string suggestedName)
        {
            if (animation == null) return null;

            var path = EditorUtility.SaveFilePanelInProject(
                "Save Tween Preset",
                string.IsNullOrWhiteSpace(suggestedName) ? "TweenAnimation" : suggestedName,
                "asset",
                "Where should this animation be saved?");

            if (string.IsNullOrEmpty(path)) return null;

            var asset = Save(animation, path);
            EditorGUIUtility.PingObject(asset);
            return asset;
        }

        /// <summary>Writes a portable copy of <paramref name="animation"/> to <paramref name="path"/>.</summary>
        public static TweenAnimationAsset Save(TweenAnimation animation, string path)
        {
            var asset = ScriptableObject.CreateInstance<TweenAnimationAsset>();
            asset.SetAnimation(animation);
            StripSceneReferences(asset.Animation);

            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            return asset;
        }

        /// <summary>
        /// Drops every step target. Scene references cannot survive in an asset, and a dangling
        /// one would silently resolve to the wrong object elsewhere.
        /// </summary>
        public static void StripSceneReferences(TweenAnimation animation)
        {
            if (animation?.Steps == null) return;

            for (var i = 0; i < animation.Steps.Count; i++)
            {
                var step = animation.Steps[i];
                if (step == null) continue;

                // A material saved as an asset travels fine; anything else belongs to a scene.
                var portable = step.Target is Material material && EditorUtility.IsPersistent(material);
                if (!portable) step.Target = null;
            }
        }
    }
}
