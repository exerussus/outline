using System.Collections.Generic;
using Exerussus.Outline.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace Exerussus.Outline.Lab
{
    /// <summary>
    /// Витрина подсветки UI: страницы по четыре стиля — у каждого колонка из кнопки, иконки с альфой, текста и
    /// скруглённой карточки; страницы сменяются сами (плавный переход стиля). Последняя страница — временные
    /// эффекты, смена стиля, fade и движение. Без замеров, по кругу.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class OutlineUiShowcase : MonoBehaviour
    {
        private const int Columns = 4;
        private const int FxColumns = 6;

        [SerializeField] private OutlineUiStyle[] styles = System.Array.Empty<OutlineUiStyle>();
        [SerializeField] private OutlineUiStyle swapA;
        [SerializeField] private OutlineUiStyle swapB;
        [SerializeField] private OutlineUiStyle fadeStyle;
        [SerializeField] private OutlineUiStyle motionStyle;
        [SerializeField] private Texture2D icon;
        [SerializeField, Min(1f)] private float pageSeconds = 5f;
        [SerializeField, Min(0f)] private float styleTransition = 0.35f;

        private VisualElement _root;
        private Label _header;
        private readonly List<Label> _titles = new();
        private readonly List<List<VisualElement>> _columns = new();
        private readonly List<Label> _fxTitles = new();
        private readonly List<List<VisualElement>> _fxColumns = new();
        private VisualElement _stylesView;
        private VisualElement _fxView;
        private readonly List<OutlineUiHandle> _handles = new();
        private OutlineUiHandle _swapHandle;
        private OutlineUiHandle _fadeHandle;
        private float _fadeOutAt = -1f;
        private int _page = -1;
        private float _pageStart;
        private float _nextFx;
        private int _fxStep;

        private int StylePages => (styles.Length + Columns - 1) / Columns;
        private int PageCount => StylePages + 1;
        private bool IsFxPage => _page == StylePages;

        private void Start()
        {
            var doc = GetComponent<UIDocument>();
            _root = new VisualElement();
            _root.style.position = Position.Absolute;
            _root.style.left = 0;
            _root.style.top = 0;
            _root.style.right = 0;
            _root.style.bottom = 0;
            _root.style.backgroundColor = new Color(0.09f, 0.1f, 0.13f);
            doc.rootVisualElement.Add(_root);

            _header = new Label();
            _header.style.fontSize = 22;
            _header.style.color = new Color(0.9f, 0.92f, 1f);
            _header.style.unityTextAlign = TextAnchor.MiddleCenter;
            _header.style.marginTop = 16;
            _root.Add(_header);

            _stylesView = BuildView(Columns, _titles, _columns, 4);
            _fxView = BuildView(FxColumns, _fxTitles, _fxColumns, 2);
            string[] fxNames = { "растворение ↔ появление", "пульс", "вспышка", "смена стиля (плавно)", "fade", "движение" };
            for (int i = 0; i < _fxTitles.Count; i++)
                _fxTitles[i].text = fxNames[i];
            ShowPage(0);
        }

        private void OnDisable()
        {
            OutlineUi.HideAll();
            _root?.RemoveFromHierarchy();
            _root = null;
        }

        private VisualElement BuildView(int columns, List<Label> titles, List<List<VisualElement>> cols, int kinds)
        {
            var view = new VisualElement();
            view.style.flexGrow = 1;
            view.style.flexDirection = FlexDirection.Row;
            view.style.justifyContent = Justify.SpaceAround;
            view.style.alignItems = Align.Center;
            _root.Add(view);
            for (int c = 0; c < columns; c++)
            {
                var col = new VisualElement();
                col.style.alignItems = Align.Center;
                var title = new Label();
                title.style.fontSize = 17;
                title.style.color = new Color(0.85f, 0.88f, 0.95f);
                title.style.marginBottom = 18;
                col.Add(title);
                titles.Add(title);
                var list = new List<VisualElement>();
                for (int k = 0; k < kinds; k++)
                {
                    var slot = new VisualElement();
                    slot.style.marginTop = 16;
                    slot.style.marginBottom = 16;
                    var e = CreateElement(k);
                    slot.Add(e);
                    col.Add(slot);
                    list.Add(e);
                }
                cols.Add(list);
                view.Add(col);
            }
            return view;
        }

        private VisualElement CreateElement(int kind)
        {
            switch (kind)
            {
                case 1:
                {
                    var img = new Image { image = icon, scaleMode = ScaleMode.ScaleToFit };
                    img.style.width = 72;
                    img.style.height = 72;
                    return img;
                }
                case 2:
                {
                    var label = new Label("Текст");
                    label.style.fontSize = 30;
                    label.style.unityFontStyleAndWeight = FontStyle.Bold;
                    label.style.color = new Color(0.95f, 0.95f, 1f);
                    return label;
                }
                case 3:
                {
                    var card = new VisualElement();
                    card.style.width = 110;
                    card.style.height = 64;
                    card.style.backgroundColor = new Color(0.2f, 0.35f, 0.7f, 0.95f);
                    card.style.borderTopLeftRadius = 16;
                    card.style.borderTopRightRadius = 16;
                    card.style.borderBottomLeftRadius = 16;
                    card.style.borderBottomRightRadius = 16;
                    return card;
                }
                default:
                {
                    var button = new Button { text = "Кнопка" };
                    button.style.width = 140;
                    button.style.height = 44;
                    button.style.fontSize = 17;
                    return button;
                }
            }
        }

        private void Update()
        {
            float now = Time.time;
            if (_page >= 0 && now - _pageStart >= (IsFxPage ? pageSeconds * 2f : pageSeconds))
                ShowPage((_page + 1) % PageCount);
            if (IsFxPage)
                TickFx(now);
        }

        private void ShowPage(int page)
        {
            bool wasStyles = _page >= 0 && _page < StylePages;
            _page = page;
            _pageStart = Time.time;
            _stylesView.style.display = IsFxPage ? DisplayStyle.None : DisplayStyle.Flex;
            _fxView.style.display = IsFxPage ? DisplayStyle.Flex : DisplayStyle.None;
            _header.text = IsFxPage
                ? $"Временные эффекты · {PageCount}/{PageCount}"
                : $"Стили {page * Columns + 1}–{Mathf.Min(styles.Length, (page + 1) * Columns)} из {styles.Length} · {page + 1}/{PageCount}";

            if (IsFxPage)
            {
                Clear();
                _fxStep = 0;
                _nextFx = Time.time;
                if (swapA != null)
                    ShowColumn(_fxColumns[3], swapA, out _swapHandle);
                if (motionStyle != null)
                    ShowColumn(_fxColumns[5], motionStyle, out _);
                return;
            }

            // страница стилей: колонка — стиль; при смене страницы — плавный переход у тех же элементов
            bool reuse = wasStyles && _handles.Count == Columns * 4;
            if (!reuse)
                Clear();
            for (int c = 0; c < Columns; c++)
            {
                int idx = page * Columns + c;
                var style = idx < styles.Length ? styles[idx] : null;
                _titles[c].text = style != null ? ShortName(style) : "";
                var col = _columns[c];
                for (int k = 0; k < col.Count; k++)
                {
                    int hi = c * 4 + k;
                    if (reuse)
                    {
                        var h = _handles[hi];
                        if (style == null)
                            h.FadeOutAndHide(styleTransition);
                        else if (h.IsAlive)
                            h.SetStyle(style, styleTransition);
                        else
                            _handles[hi] = OutlineUi.Show(col[k], style, OutlineUiOptions.FadeIn(styleTransition));
                    }
                    else
                    {
                        _handles.Add(style != null ? OutlineUi.Show(col[k], style, OutlineUiOptions.FadeIn(styleTransition)) : OutlineUiHandle.Invalid);
                    }
                }
            }
        }

        private void ShowColumn(List<VisualElement> col, OutlineUiStyle style, out OutlineUiHandle first)
        {
            first = OutlineUiHandle.Invalid;
            for (int k = 0; k < col.Count; k++)
            {
                var h = OutlineUi.Show(col[k], style);
                if (k == 0)
                    first = h;
                _handles.Add(h);
            }
        }

        private void Clear()
        {
            foreach (var h in _handles)
                h.Hide();
            _handles.Clear();
            foreach (var col in _fxColumns)
            {
                foreach (var e in col)
                {
                    OutlineUiFx.Restore(e);
                    e.style.translate = StyleKeyword.Null;
                }
            }
        }

        // эффекты по кругу раз в 1.5 с; движение — непрерывно
        private void TickFx(float now)
        {
            float x = Mathf.Sin((now - _pageStart) * 1.4f) * 60f;
            foreach (var e in _fxColumns[5])
                e.style.translate = new Translate(x, 0f);
            if (_fadeHandle.IsAlive && _fadeOutAt > 0f && now >= _fadeOutAt)
            {
                FadeColumn(4, false);
                _fadeOutAt = -1f;
            }
            if (now < _nextFx)
                return;
            _nextFx = now + 1.5f;
            _fxStep++;
            foreach (var e in _fxColumns[0])
            {
                if (OutlineUiFx.IsDissolved(e))
                    OutlineUiFx.DissolveIn(e, 1.2f);
                else
                    OutlineUiFx.DissolveOut(e, 1.2f);
            }
            foreach (var e in _fxColumns[1])
                OutlineUiFx.Pulse(e);
            foreach (var e in _fxColumns[2])
                OutlineUiFx.Flash(e, new Color(2f, 2f, 2f, 0.8f));
            if (swapA != null && swapB != null)
            {
                var target = (_fxStep & 1) == 1 ? swapB : swapA;
                foreach (var h in _handles)
                {
                    if (h.IsAlive && OutlineUi.GetElementOf(h) is { } el && _fxColumns[3].Contains(el))
                        h.SetStyle(target, 0.5f);
                }
            }
            _handles.RemoveAll(h => !h.IsAlive);
            if (fadeStyle != null && (_fxStep & 1) == 1)
            {
                FadeColumn(4, true);
                _fadeOutAt = now + 0.75f;
            }
        }

        private void FadeColumn(int column, bool show)
        {
            foreach (var e in _fxColumns[column])
            {
                if (show)
                {
                    var h = OutlineUi.Show(e, fadeStyle, OutlineUiOptions.FadeIn(0.6f));
                    _handles.Add(h);
                    _fadeHandle = h;
                }
            }
            if (!show)
            {
                foreach (var h in _handles)
                {
                    if (h.IsAlive && OutlineUi.GetElementOf(h) is { } el && _fxColumns[column].Contains(el))
                        h.FadeOutAndHide(0.6f);
                }
            }
        }

        private static string ShortName(OutlineUiStyle s)
        {
            string n = s.name;
            int i = n.LastIndexOf('_');
            return i >= 0 && i + 1 < n.Length ? n.Substring(i + 1) : n;
        }
    }
}
