using Exerussus.Outline.Rendering;
using NUnit.Framework;

namespace Exerussus.Outline.Tests
{
    public sealed class OutlineQualityTests
    {
        [Test]
        public void StepPasses_MatchesJfaSchedule()
        {
            // дальность 40 px при ×1: NextPow2(40) = 64 → шаги 32..1 = 6
            Assert.AreEqual(6, OutlineQuality.StepPasses(40f, 0f, 1f, false, false, out _));
            Assert.AreEqual(7, OutlineQuality.StepPasses(40f, 0f, 1f, false, true, out _));
            // при ×0.5: 20 → 32 → шаги 16..1 = 5
            Assert.AreEqual(5, OutlineQuality.StepPasses(40f, 0f, 0.5f, false, false, out _));
        }

        [Test]
        public void StepPasses_DualOnlyOnSmallSteps()
        {
            // внутренний контур 8 px при ×1: двойные шаги 8, 4, 2, 1
            OutlineQuality.StepPasses(40f, 8f, 1f, true, false, out int dual);
            Assert.AreEqual(4, dual);
        }

        [Test]
        public void Cost_DecreasesWithScale()
        {
            long c1 = OutlineQuality.Cost(100000f, 40f, 0f, 1f, false, false);
            long c05 = OutlineQuality.Cost(100000f, 40f, 0f, 0.5f, false, false);
            long c025 = OutlineQuality.Cost(100000f, 40f, 0f, 0.25f, false, false);
            Assert.Greater(c1, c05);
            Assert.Greater(c05, c025);
        }

        [Test]
        public void Choose_SmallAreaGetsFullScale()
        {
            float s = OutlineQuality.Choose(10000f, 20f, 0f, false, false, 1f, 0.25f, 300000, out long cost);
            Assert.AreEqual(1f, s);
            Assert.LessOrEqual(cost, 300000);
        }

        [Test]
        public void Choose_LargeAreaGetsReducedScale()
        {
            float s = OutlineQuality.Choose(170000f, 40f, 8f, true, false, 1f, 0.25f, 300000, out long cost);
            Assert.Less(s, 1f);
            Assert.LessOrEqual(cost, 300000);
        }

        [Test]
        public void Choose_FallsBackToMinScale()
        {
            float s = OutlineQuality.Choose(1e7f, 200f, 0f, false, false, 1f, 0.5f, 1000, out _);
            Assert.AreEqual(0.5f, s);
        }

        [Test]
        public void Choose_RespectsMaxScale()
        {
            float s = OutlineQuality.Choose(100f, 4f, 0f, false, false, 0.5f, 0.25f, 300000, out _);
            Assert.AreEqual(0.5f, s);
        }
    }
}
