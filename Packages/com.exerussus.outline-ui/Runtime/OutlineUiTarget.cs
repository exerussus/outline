using UnityEngine.UIElements;

namespace Exerussus.Outline.UI
{
    /// <summary>
    /// Контейнер для разметки: всё, что внутри, подсвечивается, пока элемент на панели.
    /// Стиль выбирается по состоянию, от сильного к слабому: нажат → <see cref="pressedStyle"/>,
    /// под указателем → <see cref="hoverStyle"/>, выбран → <see cref="selectedStyle"/>,
    /// иначе <see cref="outlineStyle"/>, если <see cref="highlighted"/> включено. Пустой стиль состояния
    /// пропускается — берётся следующий. Смена стиля — плавная (<see cref="styleTransition"/>).
    /// </summary>
    [UxmlElement]
    public partial class OutlineUiTarget : VisualElement
    {
        private OutlineUiStyle _outlineStyle;
        private OutlineUiStyle _hoverStyle;
        private OutlineUiStyle _pressedStyle;
        private OutlineUiStyle _selectedStyle;
        private bool _highlighted = true;
        private bool _selected;
        private bool _hovered;
        private bool _pressed;
        private OutlineUiStyle _shown;
        private OutlineUiHandle _handle;

        /// <summary>Базовый стиль подсветки (горит, пока <see cref="highlighted"/>).</summary>
        [UxmlAttribute]
        public OutlineUiStyle outlineStyle
        {
            get => _outlineStyle;
            set { _outlineStyle = value; Refresh(); }
        }

        /// <summary>Стиль под указателем. Пусто — наведение не меняет подсветку.</summary>
        [UxmlAttribute]
        public OutlineUiStyle hoverStyle
        {
            get => _hoverStyle;
            set { _hoverStyle = value; Refresh(); }
        }

        /// <summary>Стиль, пока указатель нажат на элементе или его детях.</summary>
        [UxmlAttribute]
        public OutlineUiStyle pressedStyle
        {
            get => _pressedStyle;
            set { _pressedStyle = value; Refresh(); }
        }

        /// <summary>Стиль выбранного состояния (<see cref="selected"/>).</summary>
        [UxmlAttribute]
        public OutlineUiStyle selectedStyle
        {
            get => _selectedStyle;
            set { _selectedStyle = value; Refresh(); }
        }

        /// <summary>Базовая подсветка включена.</summary>
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

        /// <summary>Элемент выбран — горит <see cref="selectedStyle"/>.</summary>
        [UxmlAttribute]
        public bool selected
        {
            get => _selected;
            set
            {
                if (_selected == value)
                    return;
                _selected = value;
                Refresh();
            }
        }

        /// <summary>Приоритет при переполнении слотов (<see cref="OutlineUiOptions.priority"/>).</summary>
        [UxmlAttribute]
        public int priority { get; set; }

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

        /// <summary>Стиль, который должен гореть сейчас; null — подсветки нет.</summary>
        public OutlineUiStyle CurrentStyle
        {
            get
            {
                if (_pressed && _pressedStyle != null)
                    return _pressedStyle;
                if (_hovered && _hoverStyle != null)
                    return _hoverStyle;
                if (_selected && _selectedStyle != null)
                    return _selectedStyle;
                return _highlighted ? _outlineStyle : null;
            }
        }

        public OutlineUiTarget()
        {
            RegisterCallback<AttachToPanelEvent>(_ => Refresh());
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                // фильтр снимается сразу — элемент уходит с панели
                _hovered = false;
                _pressed = false;
                _handle.Hide();
                _handle = OutlineUiHandle.Invalid;
                _shown = null;
            });

            RegisterCallback<PointerEnterEvent>(_ => SetPointer(true, _pressed));
            RegisterCallback<PointerLeaveEvent>(_ => SetPointer(false, false));
            // TrickleDown: нажатие на дочернюю кнопку, которая гасит всплытие, тоже считается
            RegisterCallback<PointerDownEvent>(_ => SetPointer(_hovered, true), TrickleDown.TrickleDown);
            RegisterCallback<PointerUpEvent>(_ => SetPointer(_hovered, false), TrickleDown.TrickleDown);
            RegisterCallback<PointerCancelEvent>(_ => SetPointer(_hovered, false), TrickleDown.TrickleDown);
        }

        private void SetPointer(bool hovered, bool pressed)
        {
            if (_hovered == hovered && _pressed == pressed)
                return;
            _hovered = hovered;
            _pressed = pressed;
            // без стилей состояний наведение и нажатие ничего не меняют — не трогаем подсветку
            if (_hoverStyle != null || _pressedStyle != null)
                Refresh();
        }

        private void Refresh()
        {
            var want = panel != null ? CurrentStyle : null;
            if (want != null)
            {
                if (!_handle.IsAlive)
                {
                    _handle = OutlineUi.Show(this, want, OutlineUiOptions.FadeIn(fadeIn).WithPriority(priority));
                    _shown = want;
                }
                else if (!ReferenceEquals(_shown, want))
                {
                    _handle.SetStyle(want, styleTransition);
                    _shown = want;
                }
            }
            else if (_handle.IsAlive)
            {
                _handle.FadeOutAndHide(panel != null ? fadeOut : 0f);
                _handle = OutlineUiHandle.Invalid;
                _shown = null;
            }
        }
    }
}
