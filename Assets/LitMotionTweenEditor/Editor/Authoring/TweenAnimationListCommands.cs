using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;

namespace LitMotion.TweenEditor.Editor
{
    /// <summary>
    /// Adds, duplicates, renames, reorders and removes the animations in a player's list.
    /// </summary>
    /// <remarks>
    /// Kept apart from the chip bar so the rules -- ids stay unique, a new animation does not
    /// inherit the previous one's steps, a duplicate keeps its event listeners -- can be tested
    /// without building any UI. Every command records one undo entry against the target.
    /// </remarks>
    internal static class TweenAnimationListCommands
    {
        /// <summary>
        /// Returns <paramref name="preferred"/>, or it with the lowest free number appended
        /// ("Show 2", "Show 3") when that id is already taken.
        /// </summary>
        public static string UniqueId(IReadOnlyList<string> existing, string preferred)
        {
            if (string.IsNullOrWhiteSpace(preferred)) preferred = "Animation";
            if (!Contains(existing, preferred)) return preferred;

            for (var n = 2; ; n++)
            {
                var candidate = preferred + " " + n;
                if (!Contains(existing, candidate)) return candidate;
            }
        }

        /// <summary>
        /// The first built-in id no animation uses yet, so adding a few animations in a row
        /// gives Show, Hide, Highlighted... rather than five copies of one name.
        /// </summary>
        public static string NextFreeId(IReadOnlyList<string> existing)
        {
            for (var i = 0; i < TweenAnimationId.BuiltIn.Length; i++)
            {
                if (!Contains(existing, TweenAnimationId.BuiltIn[i])) return TweenAnimationId.BuiltIn[i];
            }

            return UniqueId(existing, "Animation");
        }

        static bool Contains(IReadOnlyList<string> list, string value)
        {
            if (list == null) return false;

            for (var i = 0; i < list.Count; i++)
            {
                if (list[i] == value) return true;
            }

            return false;
        }

        /// <summary>Every animation's id, in list order.</summary>
        public static List<string> Ids(TweenAuthoringSource source)
        {
            var ids = new List<string>();
            if (source == null) return ids;

            for (var i = 0; i < source.AnimationCount; i++)
            {
                ids.Add(source.AnimationAt(i)?.Id ?? string.Empty);
            }

            return ids;
        }

        /// <summary>
        /// Copies everything but the listeners from <paramref name="from"/> into
        /// <paramref name="to"/>, keeping <paramref name="to"/>'s place in the list.
        /// </summary>
        public static void CopyInto(TweenAnimation to, TweenAnimation from)
        {
            if (to == null || from == null) return;

            var clone = from.Clone();
            to.Id = clone.Id;
            to.Steps = clone.Steps;
            to.Loops = clone.Loops;
            to.LoopType = clone.LoopType;
            to.PlaybackSpeed = clone.PlaybackSpeed;
            to.IgnoreTimeScale = clone.IgnoreTimeScale;
            to.BlendMode = clone.BlendMode;
            to.KillBehavior = clone.KillBehavior;
        }

        /// <summary>
        /// Appends an animation: empty, or a copy of <paramref name="template"/>.
        /// </summary>
        /// <returns>The new animation's index, or -1.</returns>
        public static int Add(TweenAuthoringSource source, TweenAnimation template = null)
        {
            var array = source?.AnimationsArray;
            if (array == null) return -1;

            var ids = Ids(source);
            var serialized = source.SerializedObject;
            Undo.RegisterCompleteObjectUndo(serialized.targetObject,
                template == null ? "Add Tween Animation" : "Add Tween Animation From Preset");

            var index = array.arraySize;
            array.InsertArrayElementAtIndex(index);
            serialized.ApplyModifiedProperties();
            serialized.Update();

            var created = source.AnimationAt(index);
            if (created == null) return -1;

            // The inserted element is a copy of the previous one, steps, listeners and all.
            // Neither an empty animation nor a preset copy should carry those along.
            if (template == null)
            {
                created.Id = NextFreeId(ids);
                created.Steps = new List<TweenStep>();
                created.Loops = 1;
                created.LoopType = LoopType.Restart;
                created.PlaybackSpeed = 1f;
                created.IgnoreTimeScale = false;
                created.BlendMode = TweenBlendMode.Override;
                created.KillBehavior = TweenKillBehavior.Cancel;
            }
            else
            {
                CopyInto(created, template);
                created.Id = UniqueId(ids, template.Id);
            }

            created.OnComplete = new UnityEvent();

            EditorUtility.SetDirty(serialized.targetObject);
            serialized.Update();
            return index;
        }

        /// <summary>
        /// Duplicates an animation next to itself, listeners included.
        /// </summary>
        /// <remarks>
        /// Goes through Unity's array duplication rather than a clone, because that is the only
        /// way UnityEvent listeners travel with a copy.
        /// </remarks>
        /// <returns>The duplicate's index, or -1.</returns>
        public static int Duplicate(TweenAuthoringSource source, int index)
        {
            var array = source?.AnimationsArray;
            if (array == null || index < 0 || index >= array.arraySize) return -1;

            var ids = Ids(source);
            var serialized = source.SerializedObject;
            Undo.RegisterCompleteObjectUndo(serialized.targetObject, "Duplicate Tween Animation");

            array.InsertArrayElementAtIndex(index);
            serialized.ApplyModifiedProperties();
            serialized.Update();

            var copy = array.GetArrayElementAtIndex(index + 1).FindPropertyRelative("Id");
            copy.stringValue = UniqueId(ids, (ids[index] ?? string.Empty) + " Copy");
            serialized.ApplyModifiedProperties();

            return index + 1;
        }

        /// <summary>Renames an animation. Ids may collide; that is the author's call to make.</summary>
        public static void Rename(TweenAuthoringSource source, int index, string id)
        {
            var property = source?.AnimationPropertyAt(index)?.FindPropertyRelative("Id");
            if (property == null || property.stringValue == id) return;

            Undo.RegisterCompleteObjectUndo(source.SerializedObject.targetObject, "Rename Tween Animation");
            property.stringValue = id ?? string.Empty;
            source.SerializedObject.ApplyModifiedProperties();
        }

        /// <summary>Moves an animation along the list.</summary>
        /// <returns>Its new index, or the old one when it cannot move.</returns>
        public static int Move(TweenAuthoringSource source, int index, int offset)
        {
            var array = source?.AnimationsArray;
            if (array == null || index < 0 || index >= array.arraySize) return index;

            var target = Mathf.Clamp(index + offset, 0, array.arraySize - 1);
            if (target == index) return index;

            Undo.RegisterCompleteObjectUndo(source.SerializedObject.targetObject, "Reorder Tween Animation");
            array.MoveArrayElement(index, target);
            source.SerializedObject.ApplyModifiedProperties();
            return target;
        }

        /// <summary>Deletes an animation.</summary>
        public static void Remove(TweenAuthoringSource source, int index)
        {
            var array = source?.AnimationsArray;
            if (array == null || index < 0 || index >= array.arraySize) return;

            Undo.RegisterCompleteObjectUndo(source.SerializedObject.targetObject, "Remove Tween Animation");
            array.DeleteArrayElementAtIndex(index);
            source.SerializedObject.ApplyModifiedProperties();
        }
    }
}
