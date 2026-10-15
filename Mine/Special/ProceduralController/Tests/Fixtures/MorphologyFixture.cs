using System;
using System.Collections.Generic;
using UnityEngine;
using CelToon.ProceduralController.Contracts;

namespace CelToon.ProceduralController.Fixtures
{
    [Serializable]
    public sealed class MorphologyFixture
    {
        public string Name;
        public ModuleId[] Legs;
        public float UpperLength = 0.5f;
        public float LowerLength = 0.5f;
        public float BodyHeight = 0.65f;
        public MorphologyFixture(string name, ModuleId[] legs) { Name = name; Legs = legs; }

        public static MorphologyFixture Four() => new MorphologyFixture("Q4_Crawl", new[] { Id("FL"), Id("FR"), Id("HL"), Id("HR") });
        public static MorphologyFixture Six() => new MorphologyFixture("H6_Tripod", new[] { Id("FL"), Id("FR"), Id("ML"), Id("MR"), Id("HL"), Id("HR") });
        public static MorphologyFixture Eight() => new MorphologyFixture("O8_Wave", new[] { Id("L0"), Id("R0"), Id("L1"), Id("R1"), Id("L2"), Id("R2"), Id("L3"), Id("R3") });
        private static ModuleId Id(string value) => new ModuleId(value, ModuleKind.Limb);

        public bool Validate(out string error)
        {
            error = null;
            if (Legs == null || Legs.Length == 0) { error = "legs.empty"; return false; }
            var ids = new HashSet<string>();
            foreach (var leg in Legs)
                if (string.IsNullOrEmpty(leg.Value) || !ids.Add(leg.Value)) { error = "legs.duplicate"; return false; }
            if (UpperLength <= 0f || LowerLength <= 0f || BodyHeight <= 0f) { error = "dimensions.invalid"; return false; }
            return true;
        }

        public RigMetrics[] CreateMetrics()
        {
            var result = new RigMetrics[Legs.Length];
            for (int i = 0; i < Legs.Length; i++)
            {
                float x = (i % 2 == 0 ? -1f : 1f) * 0.2f;
                float z = Legs.Length == 4 ? (i < 2 ? 0.35f : -0.35f) : (i / 2 - (Legs.Length / 4 - 0.5f)) * 0.3f;
                result[i] = new RigMetrics { BodyHeight = BodyHeight, SupportMargin = 0.02f, Modules = new[] { new ModuleMetric { ModuleId = Legs[i].Value, UpperLength = UpperLength, LowerLength = LowerLength, AttachmentOffsetWS = new Vector3(x, 0, z), ContactOffsetWS = new Vector3(x * 2f, -BodyHeight, z) } } };
            }
            return result;
        }
    }
}
