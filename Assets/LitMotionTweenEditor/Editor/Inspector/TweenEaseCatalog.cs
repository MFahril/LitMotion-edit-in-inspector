using System;
using System.Collections.Generic;
using UnityEditor;

namespace LitMotion.TweenEditor.Editor
{
    /// <summary>
    /// The ease list, grouped and searchable, plus the author's favourites and recents.
    /// </summary>
    /// <remarks>
    /// Separated from the picker window so the part with rules in it -- what "bounce" matches,
    /// which family an ease belongs to, how favourites round-trip through EditorPrefs -- can be
    /// tested without opening a window.
    ///
    /// <see cref="Ease.CustomAnimationCurve"/> is deliberately absent from <see cref="All"/>:
    /// it is not an ease you pick from a gallery, it is a mode that hands editing to an
    /// AnimationCurve field, so the picker offers it separately.
    /// </remarks>
    internal static class TweenEaseCatalog
    {
        const string FavouritesKey = "LMTE.Ease.Favourites";
        const string RecentsKey = "LMTE.Ease.Recents";
        const int MaxRecents = 6;

        /// <summary>Families in the order the gallery shows them, gentlest first.</summary>
        public static readonly string[] Families =
        {
            "Linear", "Sine", "Quad", "Cubic", "Quart", "Quint",
            "Expo", "Circ", "Back", "Elastic", "Bounce",
        };

        static Ease[] all;

        /// <summary>Every ease that can be picked from the gallery.</summary>
        public static Ease[] All
        {
            get
            {
                if (all != null) return all;

                var values = (Ease[])Enum.GetValues(typeof(Ease));
                var list = new List<Ease>(values.Length);

                for (var i = 0; i < values.Length; i++)
                {
                    if (values[i] != Ease.CustomAnimationCurve) list.Add(values[i]);
                }

                all = list.ToArray();
                return all;
            }
        }

        /// <summary>The family an ease belongs to, such as "Bounce".</summary>
        public static string FamilyOf(Ease ease)
        {
            var name = ease.ToString();
            if (name == "Linear") return "Linear";

            for (var i = 0; i < Families.Length; i++)
            {
                if (name.EndsWith(Families[i], StringComparison.Ordinal)) return Families[i];
            }

            return "Other";
        }

        /// <summary>The direction of an ease: "In", "Out", "InOut", or empty for Linear.</summary>
        public static string DirectionOf(Ease ease)
        {
            var name = ease.ToString();
            if (name == "Linear") return string.Empty;
            if (name.StartsWith("InOut", StringComparison.Ordinal)) return "InOut";
            if (name.StartsWith("In", StringComparison.Ordinal)) return "In";
            if (name.StartsWith("Out", StringComparison.Ordinal)) return "Out";

            return string.Empty;
        }

        /// <summary>
        /// The eases matching a search query, in catalog order.
        /// </summary>
        /// <remarks>
        /// Matches on the ease name with spaces and case ignored, so "out b" finds OutBack and
        /// OutBounce, and an empty query returns everything.
        /// </remarks>
        public static List<Ease> Filter(string query)
        {
            var results = new List<Ease>();
            var needle = Normalize(query);

            for (var i = 0; i < All.Length; i++)
            {
                if (needle.Length == 0 || Normalize(All[i].ToString()).Contains(needle))
                {
                    results.Add(All[i]);
                }
            }

            return results;
        }

        /// <summary>The eases of one family, in In / Out / InOut order.</summary>
        public static List<Ease> InFamily(string family, List<Ease> within)
        {
            var results = new List<Ease>();
            var source = within ?? Filter(null);

            for (var i = 0; i < source.Count; i++)
            {
                if (FamilyOf(source[i]) == family) results.Add(source[i]);
            }

            results.Sort((a, b) => DirectionRank(a).CompareTo(DirectionRank(b)));
            return results;
        }

        static int DirectionRank(Ease ease)
        {
            switch (DirectionOf(ease))
            {
                case "In": return 0;
                case "Out": return 1;
                case "InOut": return 2;
                default: return -1;
            }
        }

        static string Normalize(string text)
        {
            return string.IsNullOrEmpty(text) ? string.Empty : text.Replace(" ", "").ToLowerInvariant();
        }

        // --- Favourites and recents ---

        /// <summary>The author's starred eases.</summary>
        public static List<Ease> Favourites => Read(FavouritesKey);

        /// <summary>The eases used most recently, newest first.</summary>
        public static List<Ease> Recents => Read(RecentsKey);

        /// <summary>True when the ease is starred.</summary>
        public static bool IsFavourite(Ease ease) => Favourites.Contains(ease);

        /// <summary>Stars or unstars an ease.</summary>
        public static void ToggleFavourite(Ease ease)
        {
            var list = Favourites;

            if (!list.Remove(ease)) list.Add(ease);
            Write(FavouritesKey, list);
        }

        /// <summary>Records an ease as just used, keeping the list short and duplicate-free.</summary>
        public static void PushRecent(Ease ease)
        {
            var list = Recents;

            list.Remove(ease);
            list.Insert(0, ease);
            while (list.Count > MaxRecents) list.RemoveAt(list.Count - 1);

            Write(RecentsKey, list);
        }

        /// <summary>Forgets favourites and recents. Used by the tests and the preferences page.</summary>
        public static void ClearHistory()
        {
            EditorPrefs.DeleteKey(FavouritesKey);
            EditorPrefs.DeleteKey(RecentsKey);
        }

        static List<Ease> Read(string key)
        {
            var results = new List<Ease>();
            var stored = EditorPrefs.GetString(key, string.Empty);
            if (string.IsNullOrEmpty(stored)) return results;

            var parts = stored.Split(',');
            for (var i = 0; i < parts.Length; i++)
            {
                // Stored by name so the list survives the enum gaining members.
                if (Enum.TryParse(parts[i], out Ease ease) && ease != Ease.CustomAnimationCurve)
                {
                    if (!results.Contains(ease)) results.Add(ease);
                }
            }

            return results;
        }

        static void Write(string key, List<Ease> list)
        {
            var names = new string[list.Count];
            for (var i = 0; i < list.Count; i++) names[i] = list[i].ToString();

            EditorPrefs.SetString(key, string.Join(",", names));
        }
    }
}
