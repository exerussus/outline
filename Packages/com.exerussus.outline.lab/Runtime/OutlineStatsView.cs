using Exerussus.Outline.Rendering;
using UnityEngine;
using UnityEngine.UIElements;

namespace Exerussus.Outline.Lab
{
    /// <summary>Оверлей площадки: статистика подсветки + подсказка по управлению.</summary>
    [UxmlElement]
    public partial class OutlineStatsView : VisualElement
    {
        [UxmlAttribute] public bool ShowHelp { get; set; } = true;

        private readonly Label _stats;
        private readonly Label _help;

        public OutlineStatsView()
        {
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.left = 12;
            style.top = 12;
            style.paddingLeft = 10;
            style.paddingRight = 10;
            style.paddingTop = 8;
            style.paddingBottom = 8;
            style.backgroundColor = new Color(0.05f, 0.06f, 0.08f, 0.78f);
            style.borderTopLeftRadius = 6;
            style.borderTopRightRadius = 6;
            style.borderBottomLeftRadius = 6;
            style.borderBottomRightRadius = 6;

            var font = FontDefinition.FromFont(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            _stats = CreateLabel(font, 13, new Color(0.9f, 0.95f, 1f));
            _help = CreateLabel(font, 11, new Color(0.65f, 0.7f, 0.78f));
            _help.style.marginTop = 6;
            _help.text =
                "Мышь: ховер · ЛКМ — выделить · ПКМ — враг · Backspace — снять\n" +
                "0..3 — отладка (кадр / маска / сиды / поле) · P — паттерн выделения · C — кроп";
            Add(_stats);
            Add(_help);
        }

        public void SetStats(in OutlineStats s, string extra)
        {
            _help.style.display = ShowHelp ? DisplayStyle.Flex : DisplayStyle.None;
            if (!s.Rendered)
            {
                _stats.text = "Outline: нечего рисовать" + extra;
                return;
            }
            _stats.text =
                $"Outline GPU {s.TotalGpuMs:0.000} мс  (маска {s.MaskGpuMs:0.000} · JFA {s.JfaGpuMs:0.000} · композит {s.CompositeGpuMs:0.000})\n" +
                $"CPU запись {s.CpuMs:0.000} мс · записей {s.ActiveEntries} · draw {s.DrawCalls}\n" +
                $"поле {s.FieldWidth}×{s.FieldHeight} (×{s.FieldScale:0.###}) {(s.DualField ? "+ внутр." : "")} · проходов JFA {s.JfaPasses} · кроп {s.Coverage * 100f:0}%" +
                extra;
        }

        private static Label CreateLabel(FontDefinition font, int size, Color color)
        {
            var label = new Label();
            label.pickingMode = PickingMode.Ignore;
            label.style.unityFontDefinition = font;
            label.style.fontSize = size;
            label.style.color = color;
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }
    }
}
