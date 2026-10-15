using UnityEngine;

namespace CelToon.ProceduralController.Runtime
{
    /// <summary>Compact pelvis-height cycles fitted from the credited reference clips.</summary>
    internal static class MotionFitProfile
    {
        // See Docs/IKRebuild/MotionReferences/fit_motion_refs.py for the extraction.
        private static readonly float[] HumanWalkHeight =
        { -0.152f, -0.533f, -0.549f, -0.108f, 0.379f, 0.424f, 0.296f, 0.314f,
           0.128f, -0.422f, -0.576f, -0.260f, 0.154f, 0.252f, 0.250f, 0.404f };

        private static readonly float[] DogTrotHeight =
        { -0.524f, -0.493f, -0.405f, -0.267f, -0.097f, 0.082f, 0.248f, 0.383f,
           0.466f, 0.476f, 0.407f, 0.277f, 0.112f, -0.061f, -0.225f, -0.378f };

        public static float Frequency(TestMorphology shape)
        {
            switch (shape)
            {
                case TestMorphology.Humanoid: return 0.9f; // 133 capture frames at 120 Hz.
                case TestMorphology.Quadruped: return 1.7f; // 33 capture frames at about 60 Hz.
                case TestMorphology.Hexapod: return 1.35f;
                case TestMorphology.Octopod: return 1.15f;
                default: return 1.2f;
            }
        }

        // Reference travel per gait cycle stays fixed when the speed setting changes.
        // Otherwise increasing speed would redefine the stride and never raise cadence.
        public static float SpeedScale(TestMorphology shape)
        {
            return shape == TestMorphology.Humanoid ? .55f : shape == TestMorphology.Octopod ? .75f : 1f;
        }

        public static float ReferenceCycleDistance(TestMorphology shape)
        {
            return 2f * SpeedScale(shape) / Frequency(shape);
        }

        public static float SwingFraction(TestMorphology shape)
        {
            switch (shape)
            {
                case TestMorphology.Humanoid: return 0.40f;
                case TestMorphology.Quadruped: return 0.42f;
                case TestMorphology.Hexapod: return 0.38f;
                default: return 0.22f;
            }
        }

        public static float PhaseOffset(TestMorphology shape, int index, int count)
        {
            switch (shape)
            {
                case TestMorphology.Humanoid: return (index % 2) * 0.5f;
                case TestMorphology.Quadruped: return ((index / 2 + index % 2) % 2) * 0.5f;
                case TestMorphology.Hexapod: return ((index / 2 + index % 2) % 2) * 0.5f;
                default: return (float)index / Mathf.Max(1, count);
            }
        }

        public static float BodyHeightOffset(TestMorphology shape, float phase, float motionWeight)
        {
            if (motionWeight <= 0f) return 0f;
            float[] samples = shape == TestMorphology.Humanoid ? HumanWalkHeight : DogTrotHeight;
            // The trot clip has two vertical body oscillations per left-right stride cycle.
            float samplePosition = Mathf.Repeat(phase * (shape == TestMorphology.Quadruped ? 2f : 1f), 1f) * samples.Length;
            int a = Mathf.FloorToInt(samplePosition);
            int b = (a + 1) % samples.Length;
            float t = Mathf.SmoothStep(0f, 1f, samplePosition - a);
            float amplitude = shape == TestMorphology.Humanoid ? 0.052f : 0.037f;
            return Mathf.Lerp(samples[a], samples[b], t) * amplitude * Mathf.Clamp01(motionWeight);
        }
    }
}
