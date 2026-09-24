using UnityEngine.UIElements;

namespace Exerussus.Outline.UI
{
    /// <summary>
    /// Контейнер для разметки: всё, что внутри, подсвечивается стилем, пока элемент на панели и
    /// <see cref="highlighted"/> включено. Смена стиля — плавная (<see cref="styleTransition"/>).
    /// </summary>
    [UxmlElement]
    public partial class OutlineUiTarget : VisualElement
    {
        private OutlineUiStyle _outlineStyle;
        private bool _highlighted = true;
        private OutlineUiHandle _handle;

        /// <summary>Стиль подсветки.</summary>
        [UxmlAttribute]
        public OutlineUiStyle outlineStyle
        {
            get => _outlineStyle;
            set
            {
                _outlineStyle = value;
                if (_handle.IsAlive && value != null)
                    _handle.SetStyle(value, styleTransition);
                else
                    Refresh();
            }
        }

        /// <summary>Подсветка включена.</summary>
        [UxmlAttribute]
        public bool highlighted
        {
            get => _highlighted;
            set
            {
                if (_highlighted == value)
                    return;
                _highlighted = value;
                Refresh();
            }
        }

        /// <summary>Плавное появление, с.</summary>
        [UxmlAttribute]
        public float fadeIn { get; set; } = 0.15f;

        /// <summary>Плавное исчезновение, с.</summary>
        [UxmlAttribute]
        public float fadeOut { get; set; } = 0.15f;

        /// <summary>Длительность плавной смены стиля, с.</summary>
        [UxmlAttribute]
        public float styleTransition { get; set; } = 0.2f;

        public OutlineUiHandle Handle => _handle;

        public OutlineUiTarget()
        {
            RegisterCallback<AttachToPanelEvent>(_ => Refresh());
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                // фильтр снимается сразу — элемент уходит с панели
                _handle.Hide();
                _handle = OutlineUiHandle.Invalid;
            });
        }

        private void Refresh()
        {
            bool want = _highlighted && _outlineStyle != null && panel != null;
            if (want && !_handle.IsAlive)
            {
                _handle = OutlineUi.Show(this, _outlineStyle, OutlineUiOptions.FadeIn(fadeIn));
            }
            else if (!want && _handle.IsAlive)
            {
                _handle.FadeOutAndHide(panel != null ? fadeOut : 0f);
                _handle = OutlineUiHandle.Invalid;
            }
        }
    }
}
