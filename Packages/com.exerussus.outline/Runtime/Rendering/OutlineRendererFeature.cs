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
        [SerializeField] private Shader resolveShader;

        private OutlinePass _pass;
        private bool _subscribed;

        /// <summary>Статистика последнего отрисованного кадра (для HUD и замеров).</summary>
        public OutlineStats Stats => _pass != null ? _pass.Stats : default;

        public const string MaskShaderName = "Hidden/Exerussus/Outline/Mask";
        public const string JumpFloodShaderName = "Hidden/Exerussus/Outline/JumpFlood";
        public const string CompositeShaderName = "Hidden/Exerussus/Outline/Composite";
        public const string ResolveShaderName = "Hidden/Exerussus/Outline/Resolve";

        public override void Create()
        {
            _pass?.Dispose();
            _pass = null;

            if (maskShader == null) maskShader = Shader.Find(MaskShaderName);
            if (jumpFloodShader == null) jumpFloodShader = Shader.Find(JumpFloodShaderName);
            if (compositeShader == null) compositeShader = Shader.Find(CompositeShaderName);
            if (resolveShader == null) resolveShader = Shader.Find(ResolveShaderName);

            if (maskShader == null || jumpFloodShader == null || compositeShader == null)
            {
                Debug.LogWarning("[Outline] Не найдены шейдеры подсветки — фича выключена.");
                return;
            }

            // резолв нужен только для сглаживания края; без него фича работает с резкой маской
            _pass = new OutlinePass(settings, maskShader, jumpFloodShader, compositeShader, resolveShader);
            if (!_subscribed)
            {
                OutlineSeeThrough.Subscribe();
                _subscribed = true;
            }
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
            if (_subscribed)
            {
                OutlineSeeThrough.Unsubscribe();
                _subscribed = false;
            }
        }
    }
}
