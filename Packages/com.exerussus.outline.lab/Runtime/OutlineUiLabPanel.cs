using System.Collections.Generic;
using Exerussus.Outline.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace Exerussus.Outline.Lab
{
    /// <summary>
    /// Панель площадки для подсветки UI Toolkit: кнопка, иконка с альфой, текст, скруглённая карточка.
    /// Наведение — стиль ховера; ЛКМ — следующий стиль из списка (плавно); ПКМ — растворить/проявить; колесо — пульс.
    /// </summary>
    public sealed class OutlineUiLabPanel : VisualElement
    {
        private readonly OutlineUiStyle[] _styles;
        private readonly OutlineUiStyle _hover;
        private readonly Dictionary<VisualElement, (OutlineUiHandle handle, int style)> _selected = new();
        private readonly Dictionary<VisualElement, OutlineUiHandle> _hovered = new();

        public OutlineUiLabPanel(OutlineUiStyle hover, OutlineUiStyle[] styles, Texture2D icon)
        {
            _hover = hover;
            _styles = styles ?? System.Array.Empty<OutlineUiStyle>();
            style.position = Position.Absolute;
            style.right = 24;
            style.bottom = 24;
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;

            var button = new Button { text = "Кнопка" };
            button.style.width = 120;
            button.style.height = 40;
            Add(Wrap(button));

            var image = new Image { image = icon, scaleMode = ScaleMode.ScaleToFit };
            image.style.width = 64;
            image.style.height = 64;
            Add(Wrap(image));

            var label = new Label("Текст");
            label.style.fontSize = 28;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.color = new Color(0.95f, 0.95f, 1f);
            Add(Wrap(label));

            var card = new VisualElement();
            card.style.width = 90;
            card.style.height = 56;
            card.style.backgroundColor = new Color(0.2f, 0.35f, 0.7f, 0.9f);
            SetRadius(card, 14);
            Add(Wrap(card));
        }

        // обёртка с полем: подсветка элемента рисуется за его пределами — место под свечение
        private VisualElement Wrap(VisualElement content)
        {
            var slot = new VisualElement();
            slot.style.marginLeft = 16;
            slot.style.marginRight = 16;
            slot.Add(content);
            content.RegisterCallback<PointerEnterEvent>(_ => OnEnter(content));
            content.RegisterCallback<PointerLeaveEvent>(_ => OnLeave(content));
            content.RegisterCallback<PointerDownEvent>(e => OnDown(content, e.button), TrickleDown.TrickleDown);
            return slot;
        }

        private void OnEnter(VisualElement e)
        {
            if (_hover == null || _selected.ContainsKey(e) || _hovered.ContainsKey(e))
                return;
            _hovered[e] = OutlineUi.Show(e, _hover, OutlineUiOptions.FadeIn(0.12f));
        }

        private void OnLeave(VisualElement e)
        {
            if (_hovered.TryGetValue(e, out var h))
            {
                h.FadeOutAndHide(0.12f);
                _hovered.Remove(e);
            }
        }

        private void OnDown(VisualElement e, int button)
        {
            if (button == 1)
            {
                if (OutlineUiFx.IsDissolved(e))
                    OutlineUiFx.DissolveIn(e);
                else
                    OutlineUiFx.DissolveOut(e);
                return;
            }
            if (button == 2)
            {
                OutlineUiFx.Pulse(e);
                return;
            }
            if (_styles.Length == 0)
                return;
            if (_hovered.TryGetValue(e, out var hv))
            {
                hv.Hide();
                _hovered.Remove(e);
            }
            if (_selected.TryGetValue(e, out var sel))
            {
                int next = sel.style + 1;
                if (next >= _styles.Length)
                {
                    sel.handle.FadeOutAndHide(0.15f);
                    _selected.Remove(e);
                    return;
                }
                sel.handle.SetStyle(_styles[next], 0.3f);
                _selected[e] = (sel.handle, next);
                return;
            }
            _selected[e] = (OutlineUi.Show(e, _styles[0], OutlineUiOptions.FadeIn(0.15f)), 0);
        }

        private static void SetRadius(VisualElement e, float r)
        {
            e.style.borderTopLeftRadius = r;
            e.style.borderTopRightRadius = r;
            e.style.borderBottomLeftRadius = r;
            e.style.borderBottomRightRadius = r;
        }
    }
}
