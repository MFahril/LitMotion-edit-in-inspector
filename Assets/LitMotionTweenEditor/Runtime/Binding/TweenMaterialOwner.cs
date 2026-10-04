using System.Collections.Generic;
using UnityEngine;
#if LMTE_SUPPORT_UGUI
using UnityEngine.UI;
#endif

namespace LitMotion.TweenEditor
{
    /// <summary>
    /// Owns the material instances a MaterialProperty step created at runtime, and destroys
    /// them along with the object they were made for.
    /// </summary>
    /// <remarks>
    /// Unity does not destroy a material made through <c>renderer.material</c> when its
    /// renderer goes; it lingers until the next asset unload, so an object spawned and despawned
    /// repeatedly leaks one material per lifetime. Living on the same GameObject ties the
    /// instance's lifetime to the object's without involving the player that happened to
    /// animate it, which may not even be on the same object.
    ///
    /// Added on demand and hidden from the inspector. <c>ExecuteAlways</c> so that the cleanup
    /// also runs when the editor tears the scene down on leaving play mode.
    /// </remarks>
    [ExecuteAlways]
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    internal sealed class TweenMaterialOwner : MonoBehaviour
    {
        readonly List<Material> owned = new();

        /// <summary>Number of instances this object owns.</summary>
        internal int Count => owned.Count;

        /// <summary>True when <paramref name="material"/> is one of this object's instances.</summary>
        internal bool Owns(Material material) => material != null && owned.Contains(material);

        /// <summary>The renderer's own instance of its first material, created if needed.</summary>
        /// <remarks>
        /// Goes through <c>renderer.material</c> so that Unity's own instancing is respected: a
        /// material the game already instanced is reused as it is, and only an instance this
        /// call actually caused is taken into ownership.
        /// </remarks>
        internal static Material ForRenderer(Renderer renderer)
        {
            var shared = renderer.sharedMaterial;
            if (shared == null) return null;

            var owner = renderer.GetComponent<TweenMaterialOwner>();
            if (owner != null && owner.Owns(shared)) return shared;

            var instance = renderer.material;
            if (instance != shared) Track(renderer.gameObject, owner, instance);
            return instance;
        }

#if LMTE_SUPPORT_UGUI
        /// <summary>The Graphic's own copy of its material, created and assigned if needed.</summary>
        /// <remarks>
        /// A Graphic has no instancing of its own: <c>graphic.material</c> is the asset, and
        /// writing to it animates every Graphic using it -- and, in play mode in the editor,
        /// edits the asset on disk.
        /// </remarks>
        internal static Material ForGraphic(Graphic graphic)
        {
            var current = graphic.material;
            if (current == null) return null;

            var owner = graphic.GetComponent<TweenMaterialOwner>();
            if (owner != null && owner.Owns(current)) return current;

            var copy = new Material(current) { name = current.name + " (Tween Instance)" };
            graphic.material = copy;
            Track(graphic.gameObject, owner, copy);
            return copy;
        }
#endif

        static void Track(GameObject host, TweenMaterialOwner owner, Material material)
        {
            if (owner == null)
            {
                owner = host.AddComponent<TweenMaterialOwner>();
                owner.hideFlags = HideFlags.HideInInspector;
            }

            owner.owned.Add(material);
        }

        void OnDestroy()
        {
            for (var i = 0; i < owned.Count; i++)
            {
                var material = owned[i];
                if (material == null) continue;

                if (Application.isPlaying) Destroy(material);
                else DestroyImmediate(material);
            }

            owned.Clear();
        }
    }
}
