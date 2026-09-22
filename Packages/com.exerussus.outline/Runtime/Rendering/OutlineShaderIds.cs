using UnityEngine;

namespace Exerussus.Outline.Rendering
{
    /// <summary>Идентификаторы свойств шейдеров (stateless-константы).</summary>
    internal static class OutlineShaderIds
    {
        public static readonly int Mask = Shader.PropertyToID("_OutlineMask");
        public static readonly int Seeds = Shader.PropertyToID("_OutlineSeeds");
        public static readonly int SeedsInner = Shader.PropertyToID("_OutlineSeedsInner");
        public static readonly int Params2 = Shader.PropertyToID("_OutlineParams2");
        public static readonly int Rect = Shader.PropertyToID("_OutlineRect");
        public static readonly int Entries = Shader.PropertyToID("_OutlineEntries");
        public static readonly int Data = Shader.PropertyToID("_OutlineData");
        public static readonly int Lut = Shader.PropertyToID("_OutlineLut");
        public static readonly int MaskSize = Shader.PropertyToID("_OutlineMaskSize");
        public static readonly int SeedSize = Shader.PropertyToID("_OutlineSeedSize");
        public static readonly int Params = Shader.PropertyToID("_OutlineParams");
        public static readonly int MaskGlobals = Shader.PropertyToID("_OutlineMaskGlobals");

        // проходы шейдера JumpFlood
        public const int PassInit = 0;
        public const int PassStep = 1;
        public const int PassInitDual = 2;
        public const int PassStepDual = 3;
    }
}
