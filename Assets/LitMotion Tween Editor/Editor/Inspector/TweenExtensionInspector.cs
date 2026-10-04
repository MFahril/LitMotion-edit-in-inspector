using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LitMotion.TweenEditor.Editor
{
    /// <summary>
    /// Extra controls for one extension channel, drawn in the clip inspector under the step's
    /// standard fields.
    /// </summary>
    /// <remarks>
    /// The standard fields -- values, mode, timing, easing, axis chips -- already cover what
    /// every channel shares. Derive from this when a channel has something of its own to show:
    /// a hint about what the numbers mean, a preview swatch, a button that picks a sensible
    /// value. Mark the class with <see cref="TweenExtensionInspectorAttribute"/> naming the
    /// channel's id, and give it a public parameterless constructor.
    ///
    /// Write through the <see cref="SerializedProperty"/> you are given, never the step object,
    /// so undo and prefab overrides behave. Call <see cref="NotifyChanged"/> after a write that
    /// the preview should show at once.
    /// </remarks>
    public abstract class TweenExtensionInspector
    {
        internal Action Changed;

        /// <summary>Builds the section. Called each time the clip inspector redraws.</summary>
        /// <param name="step">The step, as a serialized property.</param>
        /// <param name="target">The object the step resolved to, or null.</param>
        public abstract VisualElement CreateInspectorGUI(SerializedProperty step, UnityEngine.Object target);

        /// <summary>Tells the editor a value changed, so a running preview reflects it.</summary>
        protected void NotifyChanged() => Changed?.Invoke();
    }

    /// <summary>Names the extension channel a <see cref="TweenExtensionInspector"/> draws for.</summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class TweenExtensionInspectorAttribute : Attribute
    {
        public TweenExtensionInspectorAttribute(string channelId)
        {
            ChannelId = channelId;
        }

        public string ChannelId { get; }
    }

    /// <summary>Finds the inspector registered for an extension channel.</summary>
    internal static class TweenExtensionInspectors
    {
        static Dictionary<string, Type> byId;

        /// <summary>A fresh inspector for <paramref name="channelId"/>, or null.</summary>
        public static TweenExtensionInspector Create(string channelId)
        {
            if (string.IsNullOrEmpty(channelId)) return null;

            if (byId == null) Scan();
            if (!byId.TryGetValue(channelId, out var type)) return null;

            try
            {
                return (TweenExtensionInspector)Activator.CreateInstance(type);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[LitMotion Tween Editor] Could not create " + type.FullName + ": " + exception.Message);
                return null;
            }
        }

        static void Scan()
        {
            byId = new Dictionary<string, Type>(StringComparer.Ordinal);

            foreach (var type in TypeCache.GetTypesWithAttribute<TweenExtensionInspectorAttribute>())
            {
                if (type.IsAbstract || !typeof(TweenExtensionInspector).IsAssignableFrom(type)) continue;
                if (type.GetConstructor(Type.EmptyTypes) == null) continue;

                var attribute = (TweenExtensionInspectorAttribute)Attribute.GetCustomAttribute(
                    type, typeof(TweenExtensionInspectorAttribute));

                if (attribute != null && !string.IsNullOrEmpty(attribute.ChannelId))
                {
                    byId[attribute.ChannelId] = type;
                }
            }
        }
    }
}
