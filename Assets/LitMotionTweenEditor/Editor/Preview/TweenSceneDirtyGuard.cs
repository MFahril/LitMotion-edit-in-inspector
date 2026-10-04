using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace LitMotion.TweenEditor.Editor
{
    /// <summary>
    /// Remembers which scenes were unmodified before a preview, and tries to leave them that way.
    /// </summary>
    /// <remarks>
    /// Writing to a component in edit mode marks its scene dirty, so previewing a tween would
    /// otherwise leave a spurious asterisk on a scene nobody actually edited.
    ///
    /// Correctness of the scene's *contents* does not depend on this class at all -- that is
    /// <see cref="TweenValueSnapshot"/>'s job, and it restores every written value exactly. This
    /// only clears the dirty *flag*, so the worst case if it fails is a cosmetic asterisk on a
    /// scene whose data is already identical to what was saved.
    ///
    /// Unity exposes no public way to clear scene dirtiness
    /// (<c>EditorSceneManager.ClearSceneDirtiness</c> is internal), so this looks the method up
    /// reflectively once and silently does nothing if a future Unity renames or removes it.
    /// </remarks>
    internal sealed class TweenSceneDirtyGuard
    {
        static MethodInfo clearSceneDirtiness;
        static bool lookupAttempted;

        readonly List<Scene> cleanScenes = new();

        /// <summary>
        /// Records which currently-open scenes are unmodified. Call before a preview writes anything.
        /// </summary>
        public void Capture()
        {
            cleanScenes.Clear();

            for (var i = 0; i < EditorSceneManager.sceneCount; i++)
            {
                var scene = EditorSceneManager.GetSceneAt(i);
                if (scene.isLoaded && !scene.isDirty) cleanScenes.Add(scene);
            }
        }

        /// <summary>
        /// Clears the dirty flag on every scene that was clean at <see cref="Capture"/> time.
        /// </summary>
        /// <remarks>
        /// Call only after values have been restored. A scene the user genuinely edited during
        /// the preview is left dirty, because it was already dirty when captured and so was
        /// never added to the list.
        /// </remarks>
        public void Restore()
        {
            if (cleanScenes.Count == 0) return;

            var method = ResolveClearMethod();
            if (method != null)
            {
                for (var i = 0; i < cleanScenes.Count; i++)
                {
                    var scene = cleanScenes[i];
                    if (!scene.isLoaded || !scene.isDirty) continue;

                    try
                    {
                        method.Invoke(null, new object[] { scene });
                    }
                    catch (Exception)
                    {
                        // A cosmetic flag is never worth throwing out of a preview teardown.
                        break;
                    }
                }
            }

            cleanScenes.Clear();
        }

        /// <summary>Forgets captured state without touching any dirty flags.</summary>
        public void Clear() => cleanScenes.Clear();

        static MethodInfo ResolveClearMethod()
        {
            if (lookupAttempted) return clearSceneDirtiness;
            lookupAttempted = true;

            clearSceneDirtiness = typeof(EditorSceneManager).GetMethod(
                "ClearSceneDirtiness",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public,
                null,
                new[] { typeof(Scene) },
                null);

            return clearSceneDirtiness;
        }
    }
}
