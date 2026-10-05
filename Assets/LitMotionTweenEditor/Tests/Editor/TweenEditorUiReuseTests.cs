using NUnit.Framework;
using UnityEngine.UIElements;
using LitMotion.TweenEditor.Editor;

namespace LitMotion.TweenEditor.Tests
{
    /// <summary>
    /// Checks that the editor UI updates labels in place instead of recreating them when nothing
    /// they show has changed.
    /// </summary>
    /// <remarks>
    /// These refreshes run on preview start and stop and on most edits, often several times in
    /// one frame. Recreating the labels each time tore down labels whose text Unity 6 had just
    /// queued for layout, and its text system then logged "Trying to access the DPI setting of a
    /// visual element that is not on a panel" and a main-thread exception from a worker thread.
    /// </remarks>
    public sealed class TweenEditorUiReuseTests
    {
        [Test]
        public void ChipsAreRestyledNotRebuiltWhenTheAnimationsAreUnchanged()
        {
            var bar = new TweenAnimationBar();
            var ids = new[] { "Show", "Hide" };

            bar.Refresh(ids, 0, -1);
            var firstLabel = bar.Q<Label>(className: null);
            var labels = bar.Query<Label>().ToList();

            // Starting a preview: same animations, a different one selected and playing.
            bar.Refresh(ids, 1, 1);

            var after = bar.Query<Label>().ToList();
            Assert.AreEqual(labels.Count, after.Count);
            for (var i = 0; i < labels.Count; i++)
            {
                Assert.AreSame(labels[i], after[i], "label " + i + " was recreated");
            }

            Assert.IsNotNull(firstLabel);
        }

        [Test]
        public void ChipsAreRebuiltWhenTheAnimationsChange()
        {
            var bar = new TweenAnimationBar();
            bar.Refresh(new[] { "Show", "Hide" }, 0, -1);
            var before = bar.Query<Label>().ToList();

            bar.Refresh(new[] { "Show", "Pop" }, 0, -1);
            var after = bar.Query<Label>().ToList();

            Assert.IsTrue(after.Exists(label => label.text == "Pop"), "a renamed animation must show its new id");
            Assert.IsFalse(after.Exists(label => label.text == "Hide"));
            Assert.AreNotSame(before[0], after[0]);
        }

        [Test]
        public void RulerLabelsAreReusedAcrossRefreshes()
        {
            var ruler = new TweenTimelineRuler(() => TweenTimelineContext.Default);
            ruler.RefreshLabels();
            var before = ruler.Query<Label>().ToList();
            Assert.Greater(before.Count, 1);

            ruler.RefreshLabels();
            var after = ruler.Query<Label>().ToList();

            Assert.AreEqual(before.Count, after.Count);
            for (var i = 0; i < before.Count; i++)
            {
                Assert.AreSame(before[i], after[i], "tick label " + i + " was recreated");
                Assert.AreEqual(before[i].text, after[i].text);
            }
        }
    }
}
