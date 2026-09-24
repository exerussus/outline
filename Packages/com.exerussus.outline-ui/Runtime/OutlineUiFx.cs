using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Exerussus.Outline.UI
{
    /// <summary>
    /// Временные эффекты элементов UI: пульс, вспышка, растворение и проявление. Эффект — отдельная подсветка
    /// поверх уже висящих (фильтры элемента идут цепочкой), по окончании снимается сама.
    /// </summary>
    public static class OutlineUiFx
    {
        private static OutlineUiStyle s_PulseStyle;
        private static OutlineUiStyle s_DissolveStyle;
        private static readonly HashSet<VisualElement> s_Dissolved = new();

        /// <summary>Одна вспышка подсветки: нарастает за duration·rise, гаснет за остаток.</summary>
        public static OutlineUiHandle Pulse(VisualElement element, OutlineUiStyle style = null, float duration = 0.6f,
            float rise = 0.35f, Action onComplete = null)
        {
            if (element == null)
                return OutlineUiHandle.Invalid;
            duration = Mathf.Max(0.02f, duration);
            rise = Mathf.Clamp01(rise);
            var h = OutlineUi.Show(element, style != null ? style : PulseStyle(), OutlineUiOptions.FadeIn(duration * rise));
            float fall = duration * (1f - rise);
            element.schedule.Execute(() => h.FadeOutAndHide(fall)).StartingIn(Ms(duration * rise));
            if (onComplete != null)
                element.schedule.Execute(onComplete).StartingIn(Ms(duration));
            return h;
        }

        /// <summary>Засветка элемента цветом (заливка поверх содержимого), гаснет за duration.</summary>
        public static OutlineUiHandle Flash(VisualElement element, Color color, float duration = 0.35f, Action onComplete = null)
        {
            if (element == null)
                return OutlineUiHandle.Invalid;
            var style = ScriptableObject.CreateInstance<OutlineUiStyle>();
            style.hideFlags = HideFlags.HideAndDontSave;
            style.name = "OutlineUi Flash";
            style.outerColor = new Color(color.r, color.g, color.b, 0f);
            style.innerColor = new Color(color.r, color.g, color.b, 0f);
            style.fillColor = color;
            style.additive = 1f;
            var h = OutlineUi.Show(element, style);
            h.FadeOutAndHide(Mathf.Max(0.02f, duration));
            element.schedule.Execute(() =>
            {
                DestroyStyle(style);
                onComplete?.Invoke();
            }).StartingIn(Ms(duration) + 50);
            return h;
        }

        /// <summary>Растворить элемент: шумовой порог съедает содержимое с горящей кромкой, после — элемент скрыт.</summary>
        public static OutlineUiHandle DissolveOut(VisualElement element, float duration = 0.8f, OutlineUiStyle style = null,
            Action onComplete = null)
        {
            if (element == null)
                return OutlineUiHandle.Invalid;
            duration = Mathf.Max(0.02f, duration);
            var h = OutlineUi.Show(element, style != null ? style : DissolveStyle());
            OutlineUi.SetDissolve(h, 0f, 1f, duration);
            element.schedule.Execute(() =>
            {
                // эффект сняли раньше (Restore, HideAll) — элемент не трогаем
                if (!h.IsAlive)
                    return;
                element.style.visibility = Visibility.Hidden;
                s_Dissolved.Add(element);
                h.Hide();
                onComplete?.Invoke();
            }).StartingIn(Ms(duration));
            return h;
        }

        /// <summary>Проявить растворённый (или любой) элемент обратным растворением.</summary>
        public static OutlineUiHandle DissolveIn(VisualElement element, float duration = 0.8f, OutlineUiStyle style = null,
            Action onComplete = null)
        {
            if (element == null)
                return OutlineUiHandle.Invalid;
            duration = Mathf.Max(0.02f, duration);
            s_Dissolved.Remove(element);
            var h = OutlineUi.Show(element, style != null ? style : DissolveStyle());
            OutlineUi.SetDissolve(h, 1f, 0f, duration);
            element.style.visibility = StyleKeyword.Null;
            element.schedule.Execute(() =>
            {
                h.Hide();
                onComplete?.Invoke();
            }).StartingIn(Ms(duration));
            return h;
        }

        public static bool IsDissolved(VisualElement element) => element != null && s_Dissolved.Contains(element);

        /// <summary>Вернуть элемент как был: снять все подсветки и эффекты, показать.</summary>
        public static void Restore(VisualElement element)
        {
            if (element == null)
                return;
            OutlineUi.HideAll(element);
            s_Dissolved.Remove(element);
            element.style.visibility = StyleKeyword.Null;
        }

        private static long Ms(float seconds) => (long)(Mathf.Max(0f, seconds) * 1000f);

        private static OutlineUiStyle PulseStyle()
        {
            if (s_PulseStyle != null)
                return s_PulseStyle;
            var s = ScriptableObject.CreateInstance<OutlineUiStyle>();
            s.hideFlags = HideFlags.HideAndDontSave;
            s.name = "OutlineUi Pulse";
            s.outerColor = new Color(0.9f, 1.1f, 1.5f, 0.8f);
            s.outerWidth = 8f;
            s.innerColor = new Color(1f, 1f, 1f, 0f);
            s.additive = 1f;
            s_PulseStyle = s;
            return s;
        }

        private static OutlineUiStyle DissolveStyle()
        {
            if (s_DissolveStyle != null)
                return s_DissolveStyle;
            var s = ScriptableObject.CreateInstance<OutlineUiStyle>();
            s.hideFlags = HideFlags.HideAndDontSave;
            s.name = "OutlineUi Dissolve";
            s.outerColor = new Color(1f, 0.5f, 0.1f, 0f);
            s.innerColor = new Color(1f, 0.5f, 0.1f, 0f);
            s.dissolveScale = 14f;
            s.dissolveEdgeWidth = 0.08f;
            s.dissolveEdgeColor = new Color(1f, 0.55f, 0.15f, 1f);
            s_DissolveStyle = s;
            return s;
        }

        private static void DestroyStyle(OutlineUiStyle s)
        {
            if (s == null)
                return;
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(s);
            else
                UnityEngine.Object.DestroyImmediate(s);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => s_Dissolved.Clear();
    }
}
