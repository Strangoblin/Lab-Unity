using UnityEditor;
using UnityEngine;
using CelToon.ProceduralController.Fixtures;

namespace CelToon.ProceduralController.Editor
{
    public static class LocomotionFixtureBuilder
    {
        [MenuItem("CelToon/Locomotion/Validate Morphology Fixtures")]
        public static void ValidateMorphologies()
        {
            var fixtures = new[] { MorphologyFixture.Four(), MorphologyFixture.Six(), MorphologyFixture.Eight() };
            foreach (var fixture in fixtures)
            {
                if (!fixture.Validate(out var error)) Debug.LogError(fixture.Name + ": " + error);
                else Debug.Log(fixture.Name + " valid; legs=" + fixture.Legs.Length);
            }
        }
    }
}
