using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Exerussus.Outline.Rendering
{
    /// <summary>
    /// Фича URP: мягкий аутлайн на Jump Flood. Добавляется в Universal Renderer (PC_Renderer / Mobile_Renderer).
    /// Шейдеры держатся сериализованными ссылками — так они попадают в билд.
    /// </summary>
    [DisallowMultipleRendererFeature("Exerussus Outline")]
    public sealed class OutlineRendererFeature : ScriptableRendererFeature
    {
        public OutlineSettings settings = new();

        [SerializeField] private Shader maskShader;
        [SerializeField] private Shader jumpFloodShader;
        [SerializeField] private Shader compositeShader;

        private OutlinePass _pass;

        /// <summary>Статистика последнего отрисованного кадра (для HUD и замеров).</summary>
        public OutlineStats Stats => _pass != null ? _pass.Stats : default;

        public const string MaskShaderName = "Hidden/Exerussus/Outline/Mask";
        public const string JumpFloodShaderName = "Hidden/Exerussus/Outline/JumpFlood";
        public const string CompositeShaderName = "Hidden/Exerussus/Outline/Composite";

        public override void Create()
        {
            _pass?.Dispose();
            _pass = null;

            if (maskShader == null) maskShader = Shader.Find(MaskShaderName);
            if (jumpFloodShader == null) jumpFloodShader = Shader.Find(JumpFloodShaderName);
            if (compositeShader == null) compositeShader = Shader.Find(CompositeShaderName);

            if (maskShader == null || jumpFloodShader == null || compositeShader == null)
            {
                Debug.LogWarning("[Outline] Не найдены шейдеры подсветки — фича выключена.");
                return;
            }

            _pass = new OutlinePass(settings, maskShader, jumpFloodShader, compositeShader);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (_pass == null || !OutlineApi.HasAny)
                return;
            _pass.renderPassEvent = settings.passEvent;
            renderer.EnqueuePass(_pass);
        }

        protected override void Dispose(bool disposing)
        {
            _pass?.Dispose();
            _pass = null;
        }
    }
}
