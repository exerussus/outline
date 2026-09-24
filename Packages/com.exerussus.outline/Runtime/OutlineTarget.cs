using UnityEngine;

namespace Exerussus.Outline
{
    /// <summary>
    /// Авторинг-обёртка над OutlineApi для сцены: компонент включён — объект подсвечен.
    /// Работает и в редакторе (ExecuteAlways), чтобы крутить стиль в Scene View.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class OutlineTarget : MonoBehaviour
    {
        [SerializeField] private OutlineStyle style;
        [SerializeField] private int group;
        [SerializeField] private int priority;
        [SerializeField, Range(0, OutlineApi.MaxLayers - 1)] private int layer;
        [SerializeField] private OutlineAlphaMode alphaMode = OutlineAlphaMode.Auto;
        [SerializeField, Range(0f, 1f)] private float alphaThreshold = 0.5f;
        [SerializeField] private bool includeChildren = true;
        [SerializeField, Min(0f)] private float fadeIn = 0.15f;
        [SerializeField, Min(0f)] private float fadeOut = 0.15f;
        [Tooltip("Длительность плавной смены стиля через свойство Style (в Play Mode), с")]
        [SerializeField, Min(0f)] private float styleTransition = 0.2f;

        private OutlineHandle _handle;
        private bool _dirty;

        public OutlineHandle Handle => _handle;

        public OutlineStyle Style
        {
            get => style;
            set
            {
                style = value;
                _handle.SetStyle(value, Application.isPlaying ? styleTransition : 0f);
            }
        }

        private void OnEnable()
        {
            _dirty = false;
            Show();
        }

        private void OnDisable()
        {
            // в редакторе и при выгрузке — снимаем сразу, иначе бит слоя останется на рендерере
            if (Application.isPlaying && fadeOut > 0f && gameObject.activeInHierarchy)
                _handle.FadeOutAndHide(fadeOut);
            else
                _handle.Hide();
            _handle = OutlineHandle.Invalid;
        }

        // группа/режим альфы/набор рендереров меняются только пересозданием записи;
        // в OnValidate трогать рендереры нельзя — откладываем до Update
        private void OnValidate() => _dirty = true;

        private void Update()
        {
            if (!_dirty)
                return;
            _dirty = false;
            _handle.Hide();
            Show();
        }

        private void Show()
        {
            if (style == null)
                return;
            var o = OutlineOptions.Default;
            o.group = group;
            o.priority = priority;
            o.layer = layer;
            o.alphaMode = alphaMode;
            o.alphaThreshold = alphaThreshold;
            o.includeChildren = includeChildren;
            o.fadeIn = Application.isPlaying ? fadeIn : 0f;
            _handle = OutlineApi.Show(gameObject, style, o);
        }
    }
}
