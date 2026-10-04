using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace LitMotion.TweenEditor
{
    /// <summary>One registered extension channel.</summary>
    public sealed class TweenExtensionChannelEntry
    {
        internal TweenExtensionChannelEntry(string id, string category, ITweenExtensionChannel channel)
        {
            Id = id;
            Category = string.IsNullOrWhiteSpace(category) ? TweenExtensionRegistry.DefaultCategory : category;
            Channel = channel;
        }

        /// <summary>The id stored in <see cref="TweenStep.ExtensionId"/>.</summary>
        public string Id { get; }

        /// <summary>Add-menu group.</summary>
        public string Category { get; }

        /// <summary>The channel itself.</summary>
        public ITweenExtensionChannel Channel { get; }

        /// <summary>Shorthand for the channel's display name, falling back to the id.</summary>
        public string DisplayName => string.IsNullOrWhiteSpace(Channel?.DisplayName) ? Id : Channel.DisplayName;
    }

    /// <summary>
    /// Finds and holds every <see cref="ITweenExtensionChannel"/> the project defines.
    /// </summary>
    /// <remarks>
    /// Channels are discovered by scanning for <see cref="TweenExtensionChannelAttribute"/> the
    /// first time anything asks, and cached from then on. In the editor the scan goes through
    /// <c>TypeCache</c>, which Unity has already built; in a player it walks the loaded
    /// assemblies once. Channels can also be added with <see cref="Register"/>, which is the
    /// route to take when code stripping or a non-default constructor rules out the scan.
    ///
    /// An unknown id is never fatal. A step whose channel has gone missing -- the defining
    /// package was removed, or the id was renamed -- reports a binding error naming the id and
    /// builds nothing, exactly as a Fade step on an object with nothing to fade does.
    /// </remarks>
    public static class TweenExtensionRegistry
    {
        /// <summary>Category used when an extension does not name one.</summary>
        public const string DefaultCategory = "Extensions";

        static readonly Dictionary<string, TweenExtensionChannelEntry> ById = new(StringComparer.Ordinal);
        static readonly List<TweenExtensionChannelEntry> Ordered = new();
        static bool scanned;

        /// <summary>Raised after the set of channels changes.</summary>
        public static event Action Changed;

        /// <summary>Every registered channel, sorted by category and then name.</summary>
        public static IReadOnlyList<TweenExtensionChannelEntry> All
        {
            get
            {
                EnsureScanned();
                return Ordered;
            }
        }

        /// <summary>The channel registered as <paramref name="id"/>, or null.</summary>
        public static ITweenExtensionChannel Find(string id)
        {
            return TryGet(id, out var entry) ? entry.Channel : null;
        }

        /// <summary>Looks up a channel's registration by id.</summary>
        public static bool TryGet(string id, out TweenExtensionChannelEntry entry)
        {
            entry = null;
            if (string.IsNullOrEmpty(id)) return false;

            EnsureScanned();
            return ById.TryGetValue(id, out entry);
        }

        /// <summary>
        /// Adds a channel by hand.
        /// </summary>
        /// <returns>
        /// False when the id is empty or already taken. The first registration wins, so a second
        /// package cannot silently replace a channel that steps already rely on.
        /// </returns>
        public static bool Register(string id, ITweenExtensionChannel channel, string category = DefaultCategory)
        {
            EnsureScanned();
            if (!Add(id, category, channel, out _)) return false;

            Sort();
            Changed?.Invoke();
            return true;
        }

        /// <summary>Removes a channel. Mostly for tests and for packages that unload.</summary>
        public static bool Unregister(string id)
        {
            EnsureScanned();
            if (string.IsNullOrEmpty(id) || !ById.TryGetValue(id, out var entry)) return false;

            ById.Remove(id);
            Ordered.Remove(entry);
            Changed?.Invoke();
            return true;
        }

        /// <summary>Forgets everything and scans again on next use.</summary>
        public static void Rescan()
        {
            ById.Clear();
            Ordered.Clear();
            scanned = false;
            Changed?.Invoke();
        }

        static void EnsureScanned()
        {
            if (scanned) return;
            scanned = true;

            foreach (var type in FindAttributedTypes())
            {
                var attribute = type.GetCustomAttribute<TweenExtensionChannelAttribute>(false);
                if (attribute == null) continue;

                if (!typeof(ITweenExtensionChannel).IsAssignableFrom(type) || type.IsAbstract)
                {
                    Debug.LogWarning("[LitMotion Tween Editor] " + type.FullName + " is marked as a tween " +
                                     "extension channel but does not implement ITweenExtensionChannel.");
                    continue;
                }

                if (type.GetConstructor(Type.EmptyTypes) == null)
                {
                    Debug.LogWarning("[LitMotion Tween Editor] " + type.FullName + " needs a public " +
                                     "parameterless constructor to be found automatically. Register it with " +
                                     "TweenExtensionRegistry.Register instead.");
                    continue;
                }

                ITweenExtensionChannel channel;
                try
                {
                    channel = (ITweenExtensionChannel)Activator.CreateInstance(type);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[LitMotion Tween Editor] Could not create extension channel " +
                                     type.FullName + ": " + exception.Message);
                    continue;
                }

                if (!Add(attribute.Id, attribute.Category, channel, out var reason))
                {
                    Debug.LogWarning("[LitMotion Tween Editor] Skipped extension channel " + type.FullName +
                                     ": " + reason);
                }
            }

            Sort();
        }

        static bool Add(string id, string category, ITweenExtensionChannel channel, out string reason)
        {
            reason = null;

            if (string.IsNullOrWhiteSpace(id))
            {
                reason = "its id is empty.";
                return false;
            }

            if (channel == null)
            {
                reason = "the channel is null.";
                return false;
            }

            if (ById.ContainsKey(id))
            {
                reason = "the id '" + id + "' is already registered.";
                return false;
            }

            var entry = new TweenExtensionChannelEntry(id, category, channel);
            ById.Add(id, entry);
            Ordered.Add(entry);
            return true;
        }

        static void Sort()
        {
            Ordered.Sort((a, b) =>
            {
                var byCategory = string.CompareOrdinal(a.Category, b.Category);
                return byCategory != 0 ? byCategory : string.CompareOrdinal(a.DisplayName, b.DisplayName);
            });
        }

        static IEnumerable<Type> FindAttributedTypes()
        {
#if UNITY_EDITOR
            return UnityEditor.TypeCache.GetTypesWithAttribute<TweenExtensionChannelAttribute>();
#else
            var found = new List<Type>();
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();

            for (var i = 0; i < assemblies.Length; i++)
            {
                Type[] types;
                try
                {
                    types = assemblies[i].GetTypes();
                }
                catch (ReflectionTypeLoadException exception)
                {
                    types = exception.Types;
                }

                if (types == null) continue;

                for (var t = 0; t < types.Length; t++)
                {
                    var type = types[t];
                    if (type != null && type.IsClass && type.IsDefined(typeof(TweenExtensionChannelAttribute), false))
                    {
                        found.Add(type);
                    }
                }
            }

            return found;
#endif
        }

        /// <summary>The interpolation LitMotion runs for a shape.</summary>
        internal static TweenValueKind KindOf(TweenValueShape shape)
        {
            switch (shape)
            {
                case TweenValueShape.Float: return TweenValueKind.Float;
                case TweenValueShape.Vector2: return TweenValueKind.Vector2;
                case TweenValueShape.Vector3: return TweenValueKind.Vector3;
                default: return TweenValueKind.Vector4;
            }
        }
    }
}
