using NUnit.Framework;
using CelToon.ProceduralController.Fixtures;

namespace CelToon.ProceduralController.Tests
{
    public class FixtureTests
    {
        [TestCase(4)] [TestCase(6)] [TestCase(8)]
        public void SupportedMorphologyHasUniqueLegIds(int count)
        {
            var fixture = count == 4 ? MorphologyFixture.Four() : count == 6 ? MorphologyFixture.Six() : MorphologyFixture.Eight();
            Assert.IsTrue(fixture.Validate(out var error), error);
            Assert.AreEqual(count, fixture.Legs.Length);
            Assert.AreEqual(count, fixture.CreateMetrics().Length);
        }
    }
}
