using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Exerussus.Outline.Lab
{
    /// <summary>
    /// Витрина подсветки мира: ряд из шести объектов крупным планом, страницы по шесть стилей сменяются сами
    /// (плавный переход стиля), последняя страница — временные эффекты, смена стиля, fade и движение.
    /// Под каждым объектом — подпись. Без замеров, по кругу.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class OutlineShowcaseScene : MonoBehaviour
    {
        [SerializeField] private Camera viewCamera;
        [SerializeField] private Transform[] slots = System.Array.Empty<Transform>();
        [SerializeField] private OutlineStyle[] styles = System.Array.Empty<OutlineStyle>();
        [SerializeField] private OutlineStyle swapA;
        [SerializeField] private OutlineStyle swapB;
        [SerializeField] private OutlineStyle fadeStyle;
        [SerializeField] private OutlineStyle motionStyle;
        [SerializeField, Min(1f)] private float pageSeconds = 5f;
        [SerializeField, Min(0f)] private float styleTransition = 0.35f;
        [SerializeField] private float spinSpeed = 25f;

        private readonly List<OutlineHandle> _handles = new();
        private readonly List<Label> _labels = new();
        private Vector3[] _home;
        private Label _header;
        private int _page = -1;
        private float _pageStart;
        private float _nextFx;
        private int _fxStep;

        private int StylePages => (styles.Length + slots.Length - 1) / Mathf.Max(1, slots.Length);
        private int PageCount => StylePages + 1;
        private bool IsFxPage => _page == StylePages;

        private void Start()
        {
            if (viewCamera == null)
                viewCamera = Camera.main;
            var root = GetComponent<UIDocument>().rootVisualElement;
            _header = new Label();
            _header.style.position = Position.Absolute;
            _header.style.top = 16;
            _header.style.left = 0;
            _header.style.right = 0;
            _header.style.unityTextAlign = TextAnchor.MiddleCenter;
            _header.style.fontSize = 22;
            _header.style.color = new Color(0.9f, 0.92f, 1f);
            root.Add(_header);
            _home = new Vector3[slots.Length];
            for (int i = 0; i < slots.Length; i++)
            {
                _home[i] = slots[i] != null ? slots[i].position : Vector3.zero;
                var l = new Label();
                l.style.position = Position.Absolute;
                l.style.fontSize = 16;
                l.style.color = new Color(0.85f, 0.88f, 0.95f);
                l.style.unityTextAlign = TextAnchor.UpperCenter;
                l.style.width = 180;
                root.Add(l);
                _labels.Add(l);
            }
            ShowPage(0);
        }

        private void OnDisable()
        {
            ClearPage();
        }

        private void Update()
        {
            float now = Time.time;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] != null)
                    slots[i].Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.World);
            }
            if (now - _pageStart >= PageDuration())
                ShowPage((_page + 1) % PageCount);
            if (IsFxPage)
                TickFx(now);
        }

        private void LateUpdate()
        {
            if (viewCamera == null)
                return;
            for (int i = 0; i < slots.Length && i < _labels.Count; i++)
            {
                var t = slots[i];
                var l = _labels[i];
                if (t == null || l.panel == null)
                    continue;
                var world = _home[i] + Vector3.down * 1.05f;
                var pos = RuntimePanelUtils.CameraTransformWorldToPanel(l.panel, world, viewCamera);
                l.style.left = pos.x - 90f;
                l.style.top = pos.y;
            }
        }

        private float PageDuration() => IsFxPage ? pageSeconds * 2f : pageSeconds;

        private void ShowPage(int page)
        {
            bool wasStyles = _page >= 0 && _page < StylePages;
            _page = page;
            _pageStart = Time.time;
            _header.text = IsFxPage
                ? $"Временные эффекты · {PageCount}/{PageCount}"
                : $"Стили {page * slots.Length + 1}–{Mathf.Min(styles.Length, (page + 1) * slots.Length)} из {styles.Length} · {page + 1}/{PageCount}";

            if (IsFxPage)
            {
                ClearPage();
                _fxStep = 0;
                _nextFx = Time.time;
                string[] names = { "растворение ↔ появление", "пульс", "вспышка", "смена стиля (плавно)", "fade: появление / затухание", "движение" };
                for (int i = 0; i < _labels.Count; i++)
                    _labels[i].text = i < names.Length ? names[i] : "";
                if (slots.Length > 3 && swapA != null)
                {
                    _swapHandle = OutlineApi.Show(slots[3].gameObject, swapA);
                    _handles.Add(_swapHandle);
                }
                if (slots.Length > 5 && motionStyle != null)
                    _handles.Add(OutlineApi.Show(slots[5].gameObject, motionStyle));
                return;
            }

            // страница стилей: у каждого объекта свой стиль; при смене страницы — плавный переход
            bool reuse = wasStyles && _handles.Count == slots.Length;
            if (!reuse)
                ClearPage();
            for (int i = 0; i < slots.Length; i++)
            {
                int idx = page * slots.Length + i;
                var style = idx < styles.Length ? styles[idx] : null;
                _labels[i].text = style != null ? ShortName(style) : "";
                if (reuse)
                {
                    var h = _handles[i];
                    if (style != null && h.IsAlive)
                        h.SetStyle(style, styleTransition);
                    else if (style == null)
                        h.FadeOutAndHide(styleTransition);
                    else
                        _handles[i] = OutlineApi.Show(slots[i].gameObject, style, OutlineOptions.InGroup(i + 1, styleTransition));
                }
                else
                {
                    _handles.Add(style != null
                        ? OutlineApi.Show(slots[i].gameObject, style, OutlineOptions.InGroup(i + 1, styleTransition))
                        : OutlineHandle.Invalid);
                }
            }
        }

        private void ClearPage()
        {
            foreach (var h in _handles)
                h.Hide();
            _handles.Clear();
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] == null)
                    continue;
                OutlineFx.Restore(slots[i].gameObject);
                if (_home != null)
                    slots[i].position = _home[i];
            }
        }

        // эффекты по кругу раз в 1.5 с; движение — непрерывно
        private void TickFx(float now)
        {
            if (slots.Length > 5 && slots[5] != null)
                slots[5].position = _home[5] + Vector3.right * (Mathf.Sin((now - _pageStart) * 1.4f) * 0.9f);
            // fade: появилась за 0.6 с — через 0.75 с гаснет
            if (_fadeHandle.IsAlive && _fadeOutAt > 0f && now >= _fadeOutAt)
            {
                _fadeHandle.FadeOutAndHide(0.6f);
                _fadeOutAt = -1f;
            }
            if (now < _nextFx)
                return;
            _nextFx = now + 1.5f;
            _fxStep++;
            if (slots.Length > 0 && slots[0] != null)
            {
                var go = slots[0].gameObject;
                if (OutlineFx.IsDissolved(go))
                    OutlineFx.DissolveIn(go, 1.2f);
                else
                    OutlineFx.DissolveOut(go, 1.2f);
            }
            if (slots.Length > 1 && slots[1] != null)
                OutlineFx.Pulse(slots[1].gameObject);
            if (slots.Length > 2 && slots[2] != null)
                OutlineFx.Flash(slots[2].gameObject, new Color(2.5f, 2.5f, 2.5f, 0.8f));
            if (_swapHandle.IsAlive && swapA != null && swapB != null)
                _swapHandle.SetStyle((_fxStep & 1) == 1 ? swapB : swapA, 0.5f);
            _handles.RemoveAll(h => !h.IsAlive);
            if (slots.Length > 4 && slots[4] != null && fadeStyle != null && (_fxStep & 1) == 1)
            {
                _fadeHandle.Hide();
                _fadeHandle = OutlineApi.Show(slots[4].gameObject, fadeStyle, OutlineOptions.InGroup(5, 0.6f));
                _handles.Add(_fadeHandle);
                _fadeOutAt = now + 0.75f;
            }
        }

        private OutlineHandle _swapHandle;
        private OutlineHandle _fadeHandle;
        private float _fadeOutAt = -1f;

        private static string ShortName(OutlineStyle s)
        {
            string n = s.name;
            int i = n.LastIndexOf('_');
            return i >= 0 && i + 1 < n.Length ? n.Substring(i + 1) : n;
        }
    }
}
