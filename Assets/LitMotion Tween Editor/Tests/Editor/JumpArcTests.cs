using NUnit.Framework;
using UnityEngine;

namespace LitMotion.TweenEditor.Tests
{
    /// <summary>
    /// Covers the jump arc, which is this package's own maths rather than LitMotion's.
    /// </summary>
    public sealed class JumpArcTests
    {
        const float Tolerance = 1e-4f;

        [Test]
        public void ArcIsZeroAtBothEnds()
        {
            // The whole point: a jump must land exactly on its end value, never slightly above it.
            Assert.AreEqual(0f, JumpHelper.EvaluateArc(0f, 1, 5f, 0.5f), Tolerance);
            Assert.AreEqual(0f, JumpHelper.EvaluateArc(1f, 1, 5f, 0.5f), Tolerance);
        }

        [Test]
        public void ArcIsZeroAtBothEndsForEveryJumpCount()
        {
            for (var count = 1; count <= 5; count++)
            {
                Assert.AreEqual(0f, JumpHelper.EvaluateArc(0f, count, 3f, 0.5f), Tolerance,
                    $"start of {count}-jump arc");
                Assert.AreEqual(0f, JumpHelper.EvaluateArc(1f, count, 3f, 0.5f), Tolerance,
                    $"end of {count}-jump arc");
            }
        }

        [Test]
        public void SingleJumpPeaksAtJumpPowerInTheMiddle()
        {
            Assert.AreEqual(2.5f, JumpHelper.EvaluateArc(0.5f, 1, 2.5f, 0.5f), Tolerance);
        }

        [Test]
        public void ArcIsNeverNegativeForAPositivePower()
        {
            for (var i = 0; i <= 100; i++)
            {
                var t = i / 100f;
                Assert.GreaterOrEqual(JumpHelper.EvaluateArc(t, 3, 2f, 0.5f), 0f, $"at t={t}");
            }
        }

        [Test]
        public void SuccessiveJumpsDecay()
        {
            // Peaks of a 3-jump arc sit at the midpoint of each third.
            var first = JumpHelper.EvaluateArc(1f / 6f, 3, 4f, 0.5f);
            var second = JumpHelper.EvaluateArc(3f / 6f, 3, 4f, 0.5f);
            var third = JumpHelper.EvaluateArc(5f / 6f, 3, 4f, 0.5f);

            Assert.AreEqual(4f, first, Tolerance, "first peak should be full power");
            Assert.AreEqual(2f, second, Tolerance, "second peak should be halved");
            Assert.AreEqual(1f, third, Tolerance, "third peak should be quartered");
        }

        [Test]
        public void DecayOfOneKeepsEveryJumpTheSameHeight()
        {
            var first = JumpHelper.EvaluateArc(1f / 4f, 2, 3f, 1f);
            var second = JumpHelper.EvaluateArc(3f / 4f, 2, 3f, 1f);

            Assert.AreEqual(first, second, Tolerance);
        }

        [Test]
        public void JumpCountBelowOneIsTreatedAsOne()
        {
            var zero = JumpHelper.EvaluateArc(0.5f, 0, 2f, 0.5f);
            var negative = JumpHelper.EvaluateArc(0.5f, -4, 2f, 0.5f);
            var one = JumpHelper.EvaluateArc(0.5f, 1, 2f, 0.5f);

            Assert.AreEqual(one, zero, Tolerance);
            Assert.AreEqual(one, negative, Tolerance);
        }

        [Test]
        public void ProgressOutsideZeroToOneIsClamped()
        {
            Assert.AreEqual(0f, JumpHelper.EvaluateArc(-0.5f, 1, 2f, 0.5f), Tolerance);
            Assert.AreEqual(0f, JumpHelper.EvaluateArc(1.5f, 1, 2f, 0.5f), Tolerance);
        }

        [Test]
        public void AdapterLandsExactlyOnTheEndValue()
        {
            var adapter = new Vector3JumpMotionAdapter();
            var from = new Vector3(1f, 2f, 3f);
            var to = new Vector3(7f, 2f, -1f);
            var options = new JumpOptions { JumpCount = 2, JumpPower = 5f, Decay = 0.5f };

            var start = adapter.Evaluate(ref from, ref to, ref options,
                new MotionEvaluationContext { Progress = 0f, Time = 0d });
            var end = adapter.Evaluate(ref from, ref to, ref options,
                new MotionEvaluationContext { Progress = 1f, Time = 1d });

            Assert.AreEqual(from.x, start.x, Tolerance);
            Assert.AreEqual(from.y, start.y, Tolerance);
            Assert.AreEqual(from.z, start.z, Tolerance);

            Assert.AreEqual(to.x, end.x, Tolerance);
            Assert.AreEqual(to.y, end.y, Tolerance);
            Assert.AreEqual(to.z, end.z, Tolerance);
        }

        [Test]
        public void AdapterRaisesYAboveTheStraightLineMidway()
        {
            var adapter = new Vector3JumpMotionAdapter();
            var from = Vector3.zero;
            var to = new Vector3(10f, 0f, 0f);
            var options = new JumpOptions { JumpCount = 1, JumpPower = 4f, Decay = 0.5f };

            var mid = adapter.Evaluate(ref from, ref to, ref options,
                new MotionEvaluationContext { Progress = 0.5f, Time = 0.5d });

            Assert.AreEqual(5f, mid.x, Tolerance, "horizontal travel should be a plain lerp");
            Assert.AreEqual(4f, mid.y, Tolerance, "vertical offset should be the jump power");
        }
    }
}
