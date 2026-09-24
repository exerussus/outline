using System;
using System.Collections.Generic;
using Exerussus.Outline.UI.Internal;
using UnityEngine;
using UnityEngine.UIElements;

namespace Exerussus.Outline.UI
{
    /// <summary>
    /// Подсветка элементов UI Toolkit. Каждая подсветка — фильтр UI Toolkit на элементе: поддерево элемента
    /// растеризуется, контур строится по реальной альфе (текст, картинки, скругления, дети). Стили — те же
    /// <see cref="OutlineStyle"/>, ширины и масштабы — в пунктах UI.
    /// Состояние — SoA-буферы слотов, наружу только <see cref="OutlineUiHandle"/>.
    /// </summary>
    public static class OutlineUi
    {
        /// <summary>Одновременных подсветок UI (слот 0 — «нет записи»).</summary>
        public const int SlotCount = 128;
        public const int MaxEntries = SlotCount - 1;

        // --- SoA-буферы слотов ---
        private static readonly bool[] s_Alive = new bool[SlotCount];
        private static readonly int[] s_Version = new int[SlotCount];
        private static readonly VisualElement[] s_Element = new VisualElement[SlotCount];
        private static readonly OutlineStyle[] s_Style = new OutlineStyle[SlotCount];
        private static readonly OutlineStyle[] s_PrevStyle = new OutlineStyle[SlotCount];
        private static readonly float[] s_StyleStart = new float[SlotCount];
        private static readonly float[] s_StyleDuration = new float[SlotCount];
        private static readonly float[] s_FadeFrom = new float[SlotCount];
        private static readonly float[] s_FadeTo = new float[SlotCount];
        private static readonly float[] s_FadeStart = new float[SlotCount];
        private static readonly float[] s_FadeDuration = new float[SlotCount];
        private static readonly float[] s_HideAt = new float[SlotCount];
        // растворение от временного эффекта (OutlineUiFx): s_DisDuration < 0 — нет
        private static readonly float[] s_DisFrom = new float[SlotCount];
        private static readonly float[] s_DisTo = new float[SlotCount];
        private static readonly float[] s_DisStart = new float[SlotCount];
        private static readonly float[] s_DisDuration = new float[SlotCount];
        private static readonly FilterFunction[] s_Filter = new FilterFunction[SlotCount];
        private static readonly IVisualElementScheduledItem[] s_Ticker = new IVisualElementScheduledItem[SlotCount];
        private static readonly Action[] s_TickAction = CreateTickActions();

        // исходные фильтры элемента (до наших), чтобы вернуть их после снятия последней подсветки
        private static readonly Dictionary<VisualElement, List<FilterFunction>> s_BaseFilters = new(32);
        private static int s_AliveCount;

        public static int AliveCount => s_AliveCount;

        // ------------------------------------------------------------------ Показ

        /// <summary>Подсветить элемент (вместе с детьми). Несколько подсветок одного элемента накладываются по порядку.</summary>
        public static OutlineUiHandle Show(VisualElement element, OutlineStyle style) =>
            Show(element, style, OutlineUiOptions.Default);

        public static OutlineUiHandle Show(VisualElement element, OutlineStyle style, OutlineUiOptions options)
        {
            if (element == null || style == null)
                return OutlineUiHandle.Invalid;
            int slot = FindFreeSlot();
            if (slot < 0)
            {
                Debug.LogWarning($"[OutlineUi] Превышен лимит одновременных подсветок ({MaxEntries}).");
                return OutlineUiHandle.Invalid;
            }

            float now = OutlineClock.Now;
            s_Alive[slot] = true;
            s_Element[slot] = element;
            s_Style[slot] = style;
            s_PrevStyle[slot] = null;
            s_FadeFrom[slot] = options.fadeIn > 0f ? 0f : 1f;
            s_FadeTo[slot] = 1f;
            s_FadeStart[slot] = now;
            s_FadeDuration[slot] = Mathf.Max(0f, options.fadeIn);
            s_HideAt[slot] = -1f;
            s_DisDuration[slot] = -1f;
            s_AliveCount++;

            AssignFilter(slot, style);
            RebuildFilters(element);
            var ticker = s_Ticker[slot];
            if (ticker == null || !ReferenceEquals(TickerOwner(slot), element))
            {
                ticker?.Pause();
                s_Ticker[slot] = element.schedule.Execute(s_TickAction[slot]).Every(0);
                s_TickerOwner[slot] = element;
            }
            else
            {
                ticker.Resume();
            }
            element.MarkDirtyRepaint();
            return new OutlineUiHandle(slot, s_Version[slot]);
        }

        // ------------------------------------------------------------------ Управление

        public static bool IsAlive(in OutlineUiHandle h) =>
            h.Slot > 0 && h.Slot < SlotCount && s_Alive[h.Slot] && s_Version[h.Slot] == h.Version;

        /// <summary>Сменить стиль: сразу или плавно за duration секунд (смешиваются цвета, ширины, кривые, эффекты).</summary>
        public static void SetStyle(in OutlineUiHandle h, OutlineStyle style, float duration = 0f)
        {
            if (!IsAlive(h) || style == null)
                return;
            int s = h.Slot;
            var current = s_Style[s];
            if (ReferenceEquals(current, style))
                return;
            float now = OutlineClock.Now;
            if (duration > 0f)
            {
                var prev = s_PrevStyle[s];
                bool prevDominates = prev != null && EvaluateStyleBlend(s, now, out _) < 0.5f;
                s_PrevStyle[s] = prevDominates ? prev : current;
                s_StyleStart[s] = now;
                s_StyleDuration[s] = duration;
            }
            else
            {
                s_PrevStyle[s] = null;
            }
            s_Style[s] = style;
            // дальность поля могла вырасти — новые поля фильтра
            AssignFilter(s, style);
            RebuildFilters(s_Element[s]);
        }

        public static void SetFade(in OutlineUiHandle h, float value)
        {
            if (!IsAlive(h))
                return;
            value = Mathf.Clamp01(value);
            s_FadeFrom[h.Slot] = value;
            s_FadeTo[h.Slot] = value;
            s_FadeDuration[h.Slot] = 0f;
            s_HideAt[h.Slot] = -1f;
            s_Element[h.Slot].MarkDirtyRepaint();
        }

        public static void FadeTo(in OutlineUiHandle h, float target, float duration)
        {
            if (!IsAlive(h))
                return;
            int s = h.Slot;
            float now = OutlineClock.Now;
            s_FadeFrom[s] = EvaluateFade(s, now);
            s_FadeTo[s] = Mathf.Clamp01(target);
            s_FadeStart[s] = now;
            s_FadeDuration[s] = Mathf.Max(0f, duration);
            s_HideAt[s] = -1f;
        }

        public static void FadeOutAndHide(in OutlineUiHandle h, float duration)
        {
            if (!IsAlive(h))
                return;
            if (duration <= 0f)
            {
                Hide(h);
                return;
            }
            FadeTo(h, 0f, duration);
            s_HideAt[h.Slot] = OutlineClock.Now + duration;
        }

        public static void Hide(in OutlineUiHandle h)
        {
            if (IsAlive(h))
                Release(h.Slot);
        }

        /// <summary>Снять все подсветки элемента.</summary>
        public static void HideAll(VisualElement element)
        {
            for (int s = 1; s < SlotCount; s++)
                if (s_Alive[s] && ReferenceEquals(s_Element[s], element))
                    Release(s);
        }

        public static void HideAll()
        {
            for (int s = 1; s < SlotCount; s++)
                if (s_Alive[s])
                    Release(s);
        }

        // ------------------------------------------------------------------ Для фильтра и эффектов

        internal static bool IsSlotAlive(int slot) => slot > 0 && slot < SlotCount && s_Alive[slot];
        internal static OutlineStyle GetStyle(int slot) => s_Style[slot];
        internal static VisualElement GetElement(int slot) => s_Element[slot];

        internal static float EvaluateFade(int slot, float now)
        {
            float d = s_FadeDuration[slot];
            if (d <= 0f)
                return s_FadeTo[slot];
            float t = Mathf.Clamp01((now - s_FadeStart[slot]) / d);
            return Mathf.Lerp(s_FadeFrom[slot], s_FadeTo[slot], t);
        }

        /// <summary>Доля целевого стиля 0..1 (smoothstep) и прежний стиль; 1 и null — перехода нет.</summary>
        internal static float EvaluateStyleBlend(int slot, float now, out OutlineStyle prev)
        {
            prev = s_PrevStyle[slot];
            if (prev == null)
                return 1f;
            float t = Mathf.Clamp01((now - s_StyleStart[slot]) / Mathf.Max(1e-4f, s_StyleDuration[slot]));
            if (t >= 1f)
            {
                prev = null;
                return 1f;
            }
            return t * t * (3f - 2f * t);
        }

        /// <summary>Анимация растворения записи from → to за duration (smoothstep).</summary>
        internal static void SetDissolve(in OutlineUiHandle h, float from, float to, float duration)
        {
            if (!IsAlive(h))
                return;
            int s = h.Slot;
            s_DisFrom[s] = Mathf.Clamp01(from);
            s_DisTo[s] = Mathf.Clamp01(to);
            s_DisStart[s] = OutlineClock.Now;
            s_DisDuration[s] = Mathf.Max(0f, duration);
        }

        /// <summary>Доля растворения от эффекта; -1 — нет.</summary>
        internal static float EvaluateDissolve(int slot, float now)
        {
            float d = s_DisDuration[slot];
            if (d < 0f)
                return -1f;
            if (d <= 0f)
                return s_DisTo[slot];
            float t = Mathf.Clamp01((now - s_DisStart[slot]) / d);
            t = t * t * (3f - 2f * t);
            return Mathf.Lerp(s_DisFrom[slot], s_DisTo[slot], t);
        }

        // ------------------------------------------------------------------ Внутреннее

        private static readonly VisualElement[] s_TickerOwner = new VisualElement[SlotCount];
        private static VisualElement TickerOwner(int slot) => s_TickerOwner[slot];

        private static Action[] CreateTickActions()
        {
            var a = new Action[SlotCount];
            for (int i = 0; i < SlotCount; i++)
            {
                int slot = i;
                a[i] = () => Tick(slot);
            }
            return a;
        }

        // Каждый кадр панели: снятие по окончании затухания, перерисовка анимированных подсветок
        private static void Tick(int slot)
        {
            if (!s_Alive[slot])
            {
                s_Ticker[slot]?.Pause();
                return;
            }
            float now = OutlineClock.Now;
            if (s_HideAt[slot] >= 0f && now >= s_HideAt[slot])
            {
                Release(slot);
                return;
            }
            bool fading = s_FadeDuration[slot] > 0f && now - s_FadeStart[slot] <= s_FadeDuration[slot] + 0.05f;
            bool blending = s_PrevStyle[slot] != null;
            if (blending && now - s_StyleStart[slot] >= s_StyleDuration[slot] + 0.05f)
            {
                s_PrevStyle[slot] = null;
                blending = true; // последний кадр перехода — перерисовать с чистым стилем
            }
            bool dissolving = s_DisDuration[slot] >= 0f && now - s_DisStart[slot] <= s_DisDuration[slot] + 0.05f;
            if (fading || blending || dissolving || OutlineUiFilter.IsAnimated(s_Style[slot]))
                s_Element[slot].MarkDirtyRepaint();
        }

        private static void AssignFilter(int slot, OutlineStyle style)
        {
            float reach = OutlineUiFilter.ReachPoints(style, s_PrevStyle[slot]);
            var f = new FilterFunction(OutlineUiFilter.GetDefinition());
            f.AddParameter(new FilterParameter(slot));
            f.AddParameter(new FilterParameter(reach));
            s_Filter[slot] = f;
        }

        // Фильтры элемента: исходные пользовательские + наши подсветки по порядку слотов
        private static void RebuildFilters(VisualElement e)
        {
            if (e == null)
                return;
            bool any = false;
            for (int s = 1; s < SlotCount && !any; s++)
                any = s_Alive[s] && ReferenceEquals(s_Element[s], e);

            if (!s_BaseFilters.TryGetValue(e, out var baseList))
            {
                if (!any)
                    return;
                var inline = e.style.filter;
                baseList = inline.keyword == StyleKeyword.Undefined && inline.value != null
                    ? new List<FilterFunction>(inline.value)
                    : new List<FilterFunction>();
                s_BaseFilters.Add(e, baseList);
            }

            if (!any)
            {
                e.style.filter = baseList.Count > 0 ? new StyleList<FilterFunction>(baseList) : new StyleList<FilterFunction>(StyleKeyword.Null);
                s_BaseFilters.Remove(e);
                e.MarkDirtyRepaint();
                return;
            }

            var list = new List<FilterFunction>(baseList);
            for (int s = 1; s < SlotCount; s++)
                if (s_Alive[s] && ReferenceEquals(s_Element[s], e))
                    list.Add(s_Filter[s]);
            e.style.filter = new StyleList<FilterFunction>(list);
        }

        private static int FindFreeSlot()
        {
            for (int s = 1; s < SlotCount; s++)
                if (!s_Alive[s])
                    return s;
            return -1;
        }

        private static void Release(int slot)
        {
            var e = s_Element[slot];
            s_Alive[slot] = false;
            s_Version[slot]++;
            s_Style[slot] = null;
            s_PrevStyle[slot] = null;
            s_Filter[slot] = default;
            s_AliveCount--;
            s_Ticker[slot]?.Pause();
            RebuildFilters(e);
            s_Element[slot] = null;
        }

        // Сброс статики при выключенной перезагрузке домена (Enter Play Mode Options).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            for (int s = 0; s < SlotCount; s++)
            {
                s_Alive[s] = false;
                s_Version[s]++;
                s_Style[s] = null;
                s_PrevStyle[s] = null;
                s_Element[s] = null;
                s_Ticker[s] = null;
                s_TickerOwner[s] = null;
            }
            s_BaseFilters.Clear();
            s_AliveCount = 0;
        }
    }
}
