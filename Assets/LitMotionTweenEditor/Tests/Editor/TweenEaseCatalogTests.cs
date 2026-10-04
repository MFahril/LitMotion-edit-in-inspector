using System.Collections.Generic;
using NUnit.Framework;
using LitMotion.TweenEditor.Editor;

namespace LitMotion.TweenEditor.Tests
{
    /// <summary>
    /// Covers the ease gallery's catalog: grouping, search and the starred list.
    /// </summary>
    public sealed class TweenEaseCatalogTests
    {
        List<Ease> savedFavourites;
        List<Ease> savedRecents;

        [SetUp]
        public void SetUp()
        {
            // Favourites live in EditorPrefs, which is the author's own state, so it gets put
            // back exactly as it was.
            savedFavourites = TweenEaseCatalog.Favourites;
            savedRecents = TweenEaseCatalog.Recents;
            TweenEaseCatalog.ClearHistory();
        }

        [TearDown]
        public void TearDown()
        {
            TweenEaseCatalog.ClearHistory();

            for (var i = 0; i < savedFavourites.Count; i++) TweenEaseCatalog.ToggleFavourite(savedFavourites[i]);

            // Recents are newest-first, so replaying them in reverse restores the order.
            for (var i = savedRecents.Count - 1; i >= 0; i--) TweenEaseCatalog.PushRecent(savedRecents[i]);
        }

        [Test]
        public void TheCatalogHasEveryEaseExceptTheCustomCurve()
        {
            // A custom curve is a mode, not a shape you can preview in a gallery cell.
            Assert.AreEqual(31, TweenEaseCatalog.All.Length);
            Assert.IsFalse(System.Array.Exists(TweenEaseCatalog.All, e => e == Ease.CustomAnimationCurve));
        }

        [Test]
        public void SearchingNarrowsToAFamily()
        {
            var matches = TweenEaseCatalog.Filter("bounce");

            Assert.AreEqual(3, matches.Count);
            Assert.Contains(Ease.InBounce, matches);
            Assert.Contains(Ease.OutBounce, matches);
            Assert.Contains(Ease.InOutBounce, matches);
        }

        [Test]
        public void SearchIgnoresCaseAndSpaces()
        {
            var matches = TweenEaseCatalog.Filter("Out B");

            Assert.Contains(Ease.OutBack, matches);
            Assert.Contains(Ease.OutBounce, matches);
        }

        [Test]
        public void AnEmptyQueryReturnsEverything()
        {
            Assert.AreEqual(TweenEaseCatalog.All.Length, TweenEaseCatalog.Filter(null).Count);
            Assert.AreEqual(TweenEaseCatalog.All.Length, TweenEaseCatalog.Filter("   ").Count);
        }

        [Test]
        public void NothingMatchesNonsense()
        {
            Assert.IsEmpty(TweenEaseCatalog.Filter("zzzz"));
        }

        [Test]
        public void EveryEaseLandsInAKnownFamily()
        {
            // A new ease in a future LitMotion must not fall out of the gallery silently.
            for (var i = 0; i < TweenEaseCatalog.All.Length; i++)
            {
                var family = TweenEaseCatalog.FamilyOf(TweenEaseCatalog.All[i]);

                Assert.Contains(family, TweenEaseCatalog.Families, TweenEaseCatalog.All[i].ToString());
            }
        }

        [Test]
        public void FamiliesAreOrderedInThenOutThenInOut()
        {
            var bounce = TweenEaseCatalog.InFamily("Bounce", null);

            Assert.AreEqual(new[] { Ease.InBounce, Ease.OutBounce, Ease.InOutBounce }, bounce.ToArray());
        }

        [Test]
        public void LinearIsItsOwnFamilyWithNoDirection()
        {
            Assert.AreEqual("Linear", TweenEaseCatalog.FamilyOf(Ease.Linear));
            Assert.AreEqual(string.Empty, TweenEaseCatalog.DirectionOf(Ease.Linear));
        }

        [Test]
        public void DirectionDistinguishesInOutFromIn()
        {
            // "InOutQuad".StartsWith("In") is also true, so order matters in the implementation.
            Assert.AreEqual("InOut", TweenEaseCatalog.DirectionOf(Ease.InOutQuad));
            Assert.AreEqual("In", TweenEaseCatalog.DirectionOf(Ease.InQuad));
            Assert.AreEqual("Out", TweenEaseCatalog.DirectionOf(Ease.OutQuad));
        }

        [Test]
        public void StarringAnEaseTogglesIt()
        {
            Assert.IsFalse(TweenEaseCatalog.IsFavourite(Ease.OutBack));

            TweenEaseCatalog.ToggleFavourite(Ease.OutBack);
            Assert.IsTrue(TweenEaseCatalog.IsFavourite(Ease.OutBack));

            TweenEaseCatalog.ToggleFavourite(Ease.OutBack);
            Assert.IsFalse(TweenEaseCatalog.IsFavourite(Ease.OutBack));
        }

        [Test]
        public void RecentsAreNewestFirstAndDoNotRepeat()
        {
            TweenEaseCatalog.PushRecent(Ease.OutQuad);
            TweenEaseCatalog.PushRecent(Ease.OutBack);
            TweenEaseCatalog.PushRecent(Ease.OutQuad);

            var recents = TweenEaseCatalog.Recents;

            Assert.AreEqual(Ease.OutQuad, recents[0]);
            Assert.AreEqual(Ease.OutBack, recents[1]);
            Assert.AreEqual(2, recents.Count);
        }

        [Test]
        public void RecentsStayShort()
        {
            for (var i = 0; i < TweenEaseCatalog.All.Length; i++)
            {
                TweenEaseCatalog.PushRecent(TweenEaseCatalog.All[i]);
            }

            Assert.LessOrEqual(TweenEaseCatalog.Recents.Count, 6);
        }

        [Test]
        public void FavouritesSurviveBeingWrittenAndReadBack()
        {
            // Stored by name rather than by ordinal, so the list survives the enum changing.
            TweenEaseCatalog.ToggleFavourite(Ease.InOutElastic);
            TweenEaseCatalog.ToggleFavourite(Ease.Linear);

            var favourites = TweenEaseCatalog.Favourites;

            Assert.Contains(Ease.InOutElastic, favourites);
            Assert.Contains(Ease.Linear, favourites);
        }
    }
}
