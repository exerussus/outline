using Exerussus.Outline.Rendering;
using UnityEngine;
using UnityEngine.UIElements;

namespace Exerussus.Outline.Lab
{
    /// <summary>
    /// HUD площадки на UI Toolkit: каждые 0.25 с выводит статистику фичи (строки собираются только тогда —
    /// аллокации в пределах отладочного оверлея).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class OutlineLabHud : MonoBehaviour
    {
        [SerializeField] private OutlineRendererFeature feature;
        [SerializeField] private OutlineBenchmark benchmark;
        [SerializeField, Min(0.05f)] private float refreshInterval = 0.25f;
        [Header("Подсветка UI")]
        [SerializeField] private Exerussus.Outline.UI.OutlineUiStyle uiHoverStyle;
        [SerializeField, Tooltip("Стили по клику ЛКМ, по кругу.")] private Exerussus.Outline.UI.OutlineUiStyle[] uiStyles;
        [SerializeField] private Texture2D uiIcon;

        private OutlineStatsView _view;
        private OutlineUiLabPanel _uiPanel;
        private float _nextRefresh;
        private float _frameMsAccum;
        private int _frames;

        private void OnEnable()
        {
            var doc = GetComponent<UIDocument>();
            _view = new OutlineStatsView();
            doc.rootVisualElement.Add(_view);
            _uiPanel = new OutlineUiLabPanel(uiHoverStyle, uiStyles, uiIcon);
            doc.rootVisualElement.Add(_uiPanel);
        }

        private void OnDisable()
        {
            _view?.RemoveFromHierarchy();
            _view = null;
            if (_uiPanel != null)
            {
                Exerussus.Outline.UI.OutlineUi.HideAll();
                _uiPanel.RemoveFromHierarchy();
                _uiPanel = null;
            }
        }

        private void Update()
        {
            if (_view != null)
            {
                bool hide = benchmark != null && benchmark.HideOverlay;
                _view.style.display = hide ? DisplayStyle.None : DisplayStyle.Flex;
                if (_uiPanel != null)
                    _uiPanel.style.display = hide ? DisplayStyle.None : DisplayStyle.Flex;
            }
            _frameMsAccum += Time.unscaledDeltaTime * 1000f;
            _frames++;
            if (_view == null || feature == null || Time.unscaledTime < _nextRefresh)
                return;

            _nextRefresh = Time.unscaledTime + refreshInterval;
            float frameMs = _frames > 0 ? _frameMsAccum / _frames : 0f;
            _frameMsAccum = 0f;
            _frames = 0;
            var st = feature.settings;
            string extra = $"\nкадр {frameMs:0.0} мс ({(frameMs > 0f ? 1000f / frameMs : 0f):0} fps) · кроп {(st.cropToBounds ? "вкл" : "выкл")}{(st.scissor ? "+scissor" : "")} · поле {(st.autoFieldScale ? "авто" : "фикс.")}";
            if (benchmark != null && benchmark.IsRunning)
                extra += "\n" + benchmark.Status;
            _view.SetStats(feature.Stats, extra);
        }
    }
}
