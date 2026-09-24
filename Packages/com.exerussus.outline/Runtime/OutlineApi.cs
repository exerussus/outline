using System.Collections.Generic;
using UnityEngine;

namespace Exerussus.Outline
{
    /// <summary>
    /// Статический handle-фасад подсветки. Состояние (слоты, рендереры, fade) спрятано в private static
    /// и выделяется один раз при загрузке типа; наружу — только OutlineHandle.
    ///
    /// Рендереры рисуются в маску поштучно (DrawRenderer) — это работает и с GPU Resident Drawer,
    /// и за краем кадра (куллинг камеры не участвует). Если рендерер в нескольких подсветках,
    /// маску получает последняя (владелец).
    /// Поддерживаются MeshRenderer и SkinnedMeshRenderer.
    /// </summary>
    public static class OutlineApi
    {
        /// <summary>Максимум одновременных подсветок (id 1..63; 0 — «пусто»).</summary>
        public const int MaxEntries = 63;

        internal const int SlotCount = MaxEntries + 1;
        /// <summary>Число слоёв подсветки (OutlineOptions.layer: 0..MaxLayers-1).</summary>
        public const int MaxLayers = 4;
        private const int RenderersPerSlot = 8;

        private struct RendererRef
        {
            public int Count;
            public int OwnerSlot;
        }

        // --- SoA-буферы слотов (индекс = id записи в маске) ---
        private static readonly bool[] s_Alive = new bool[SlotCount];
        private static readonly int[] s_Version = new int[SlotCount];
        private static readonly OutlineStyle[] s_Style = new OutlineStyle[SlotCount];
        private static readonly int[] s_Group = new int[SlotCount];
        private static readonly int[] s_Priority = new int[SlotCount];
        private static readonly int[] s_Layer = new int[SlotCount];
        private static readonly OutlineAlphaMode[] s_AlphaMode = new OutlineAlphaMode[SlotCount];
        private static readonly float[] s_AlphaThreshold = new float[SlotCount];
        private static readonly float[] s_FadeFrom = new float[SlotCount];
        private static readonly float[] s_FadeTo = new float[SlotCount];
        private static readonly float[] s_FadeStart = new float[SlotCount];
        private static readonly float[] s_FadeDuration = new float[SlotCount];
        private static readonly float[] s_HideAt = new float[SlotCount];
        // анимация растворения записи (временные эффекты OutlineFx); s_DisDuration < 0 — нет
        private static readonly float[] s_DisFrom = new float[SlotCount];
        private static readonly float[] s_DisTo = new float[SlotCount];
        private static readonly float[] s_DisStart = new float[SlotCount];
        private static readonly float[] s_DisDuration = new float[SlotCount];
        // плавная смена стиля: прежний стиль и время перехода; s_PrevStyle == null — перехода нет
        private static readonly OutlineStyle[] s_PrevStyle = new OutlineStyle[SlotCount];
        private static readonly float[] s_StyleStart = new float[SlotCount];
        private static readonly float[] s_StyleDuration = new float[SlotCount];
        private static readonly List<Renderer>[] s_Renderers = CreateRendererLists();

        // владение рендерером — отдельно в каждом слое
        private static readonly Dictionary<(Renderer, int), RendererRef> s_RendererRefs = new(128);
        private static readonly List<Renderer> s_Scratch = new(32);
        private static int s_AliveCount;

        /// <summary>Есть ли хоть одна живая подсветка (дёшево, для фичи).</summary>
        public static bool HasAny => s_AliveCount > 0;
        public static int AliveCount => s_AliveCount;

        // ------------------------------------------------------------------ Show

        public static OutlineHandle Show(GameObject root, OutlineStyle style) => Show(root, style, OutlineOptions.Default);

        public static OutlineHandle Show(GameObject root, OutlineStyle style, in OutlineOptions options)
        {
            if (root == null)
                return OutlineHandle.Invalid;

            s_Scratch.Clear();
            if (options.includeChildren)
                root.GetComponentsInChildren(options.includeInactive, s_Scratch);
            else
                root.GetComponents(s_Scratch);

            var handle = Show(s_Scratch, style, options);
            s_Scratch.Clear();
            return handle;
        }

        public static OutlineHandle Show(Renderer renderer, OutlineStyle style, in OutlineOptions options)
        {
            s_Scratch.Clear();
            if (renderer != null)
                s_Scratch.Add(renderer);
            var handle = Show(s_Scratch, style, options);
            s_Scratch.Clear();
            return handle;
        }

        public static OutlineHandle Show(IReadOnlyList<Renderer> renderers, OutlineStyle style, in OutlineOptions options)
        {
            if (renderers == null || style == null)
                return OutlineHandle.Invalid;

            int slot = FindFreeSlot();
            if (slot < 0)
            {
                Debug.LogWarning($"[Outline] Превышен лимит одновременных подсветок ({MaxEntries}).");
                return OutlineHandle.Invalid;
            }

            float now = OutlineClock.Now;
            s_Alive[slot] = true;
            s_Style[slot] = style;
            s_Group[slot] = options.group;
            s_Priority[slot] = options.priority;
            s_Layer[slot] = Mathf.Clamp(options.layer, 0, MaxLayers - 1);
            s_AlphaMode[slot] = options.alphaMode;
            s_AlphaThreshold[slot] = Mathf.Clamp01(options.alphaThreshold);
            s_FadeFrom[slot] = options.fadeIn > 0f ? 0f : 1f;
            s_FadeTo[slot] = 1f;
            s_FadeStart[slot] = now;
            s_FadeDuration[slot] = Mathf.Max(0f, options.fadeIn);
            s_HideAt[slot] = -1f;
            s_DisDuration[slot] = -1f;
            s_PrevStyle[slot] = null;
            s_AliveCount++;

            var list = s_Renderers[slot];
            list.Clear();
            for (int i = 0; i < renderers.Count; i++)
            {
                var r = renderers[i];
                if (!IsSupported(r) || list.Contains(r))
                    continue;
                list.Add(r);
                Acquire(r, slot);
            }

            return new OutlineHandle(slot, s_Version[slot]);
        }

        // ------------------------------------------------------------------ Управление

        public static bool IsAlive(in OutlineHandle h) =>
            h.Slot > 0 && h.Slot < SlotCount && s_Alive[h.Slot] && s_Version[h.Slot] == h.Version;

        public static void SetStyle(in OutlineHandle h, OutlineStyle style)
        {
            if (IsAlive(h) && style != null)
            {
                s_Style[h.Slot] = style;
                s_PrevStyle[h.Slot] = null;
            }
        }

        /// <summary>
        /// Плавно сменить стиль за duration секунд: цвета, ширины и параметры эффектов смешиваются,
        /// дискретные (тип паттерна, режим перекрытого, текстуры) переключаются на середине.
        /// Смена посреди перехода начинает новый от стиля, который сейчас преобладает.
        /// </summary>
        public static void SetStyle(in OutlineHandle h, OutlineStyle style, float duration)
        {
            if (!IsAlive(h) || style == null)
                return;
            int s = h.Slot;
            var current = s_Style[s];
            if (ReferenceEquals(current, style) && duration > 0f)
                return; // уже идём к этому стилю — переход не сбрасываем
            if (duration <= 0f || current == null)
            {
                SetStyle(h, style);
                return;
            }
            float now = OutlineClock.Now;
            var prev = s_PrevStyle[s];
            if (prev != null && now - s_StyleStart[s] >= s_StyleDuration[s])
                prev = null;
            if (prev != null && ReferenceEquals(prev, style))
            {
                // разворот посреди перехода: продолжаем с той же точки в обратную сторону
                // (smoothstep симметричен: s(1 - x) = 1 - s(x))
                float x = Mathf.Clamp01((now - s_StyleStart[s]) / Mathf.Max(1e-4f, s_StyleDuration[s]));
                s_PrevStyle[s] = current;
                s_Style[s] = style;
                s_StyleDuration[s] = duration;
                s_StyleStart[s] = now - (1f - x) * duration;
                return;
            }
            bool prevDominates = prev != null && EvaluateStyleBlend(s, now, out _) < 0.5f;
            s_PrevStyle[s] = prevDominates ? prev : current;
            s_Style[s] = style;
            s_StyleStart[s] = now;
            s_StyleDuration[s] = duration;
        }

        /// <summary>Идёт ли у подсветки плавная смена стиля.</summary>
        public static bool IsStyleBlending(in OutlineHandle h) =>
            IsAlive(h) && EvaluateStyleBlend(h.Slot, OutlineClock.Now, out _) < 1f;

        /// <summary>
        /// Доля целевого стиля 0..1 (smoothstep) и прежний стиль; 1 и null — перехода нет.
        /// </summary>
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

        public static void SetGroup(in OutlineHandle h, int group)
        {
            if (IsAlive(h))
                s_Group[h.Slot] = group;
        }

        public static void SetPriority(in OutlineHandle h, int priority)
        {
            if (IsAlive(h))
                s_Priority[h.Slot] = priority;
        }

        public static void SetFade(in OutlineHandle h, float value)
        {
            if (!IsAlive(h))
                return;
            value = Mathf.Clamp01(value);
            s_FadeFrom[h.Slot] = value;
            s_FadeTo[h.Slot] = value;
            s_FadeDuration[h.Slot] = 0f;
            s_HideAt[h.Slot] = -1f;
        }

        public static void FadeTo(in OutlineHandle h, float target, float duration)
        {
            if (!IsAlive(h))
                return;
            float now = OutlineClock.Now;
            int s = h.Slot;
            s_FadeFrom[s] = EvaluateFade(s, now);
            s_FadeTo[s] = Mathf.Clamp01(target);
            s_FadeStart[s] = now;
            s_FadeDuration[s] = Mathf.Max(0f, duration);
            s_HideAt[s] = -1f;
        }

        public static void FadeOutAndHide(in OutlineHandle h, float duration)
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

        public static void Hide(in OutlineHandle h)
        {
            if (IsAlive(h))
                Release(h.Slot);
        }

        public static void HideAll()
        {
            for (int s = 1; s < SlotCount; s++)
                if (s_Alive[s])
                    Release(s);
            OutlineSeeThrough.RestoreAll();
        }

        // ------------------------------------------------------------------ Для рендера (internal)

        internal static bool IsSlotAlive(int slot) => s_Alive[slot];
        internal static OutlineStyle GetStyle(int slot) => s_Style[slot];
        internal static int GetGroup(int slot) => s_Group[slot];
        internal static int GetPriority(int slot) => s_Priority[slot];
        internal static OutlineAlphaMode GetAlphaMode(int slot) => s_AlphaMode[slot];
        internal static float GetAlphaThreshold(int slot) => s_AlphaThreshold[slot];
        internal static int GetRendererCount(int slot) => s_Renderers[slot].Count;
        internal static Renderer GetRenderer(int slot, int index) => s_Renderers[slot][index];

        /// <summary>Рисует ли этот слот рендерер в маску (рендерер может быть в нескольких подсветках).</summary>
        internal static bool IsOwner(Renderer r, int slot) =>
            s_RendererRefs.TryGetValue((r, s_Layer[slot]), out var rr) && rr.OwnerSlot == slot;

        internal static int GetLayer(int slot) => s_Layer[slot];

        internal static float EvaluateFade(int slot, float now)
        {
            float d = s_FadeDuration[slot];
            if (d <= 0f)
                return s_FadeTo[slot];
            float t = Mathf.Clamp01((now - s_FadeStart[slot]) / d);
            t = t * t * (3f - 2f * t);
            return Mathf.Lerp(s_FadeFrom[slot], s_FadeTo[slot], t);
        }

        /// <summary>Центр первого живого рендерера (для ширины в мировых единицах).</summary>
        internal static bool TryGetCenter(int slot, out Vector3 center)
        {
            var list = s_Renderers[slot];
            for (int i = 0; i < list.Count; i++)
            {
                var r = list[i];
                if (r != null)
                {
                    center = r.bounds.center;
                    return true;
                }
            }
            center = default;
            return false;
        }

        /// <summary>Задать записи анимацию растворения from → to за duration (плавная, smoothstep).</summary>
        internal static void SetDissolve(in OutlineHandle h, float from, float to, float duration)
        {
            if (!IsAlive(h))
                return;
            int s = h.Slot;
            s_DisFrom[s] = Mathf.Clamp01(from);
            s_DisTo[s] = Mathf.Clamp01(to);
            s_DisStart[s] = OutlineClock.Now;
            s_DisDuration[s] = Mathf.Max(0f, duration);
        }

        /// <summary>Текущая доля растворения записи; -1 — у записи нет анимации растворения.</summary>
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

        internal static float EvaluateDissolve(in OutlineHandle h) =>
            IsAlive(h) ? EvaluateDissolve(h.Slot, OutlineClock.Now) : -1f;

        /// <summary>Первый живой рендерер записи (якорь паттерна в пространстве объекта).</summary>
        internal static Renderer GetFirstRenderer(int slot)
        {
            var list = s_Renderers[slot];
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null)
                    return list[i];
            }
            return null;
        }

        /// <summary>Снимает подсветки, у которых закончился FadeOutAndHide. Идемпотентно, зовётся из рендера.</summary>
        internal static void Tick(float now)
        {
            for (int s = 1; s < SlotCount; s++)
            {
                if (!s_Alive[s])
                    continue;
                if (s_HideAt[s] >= 0f && now >= s_HideAt[s])
                    Release(s);
                else if (s_PrevStyle[s] != null && now - s_StyleStart[s] >= s_StyleDuration[s])
                    s_PrevStyle[s] = null;
            }
        }

        // ------------------------------------------------------------------ Внутреннее

        private static List<Renderer>[] CreateRendererLists()
        {
            var lists = new List<Renderer>[SlotCount];
            for (int i = 0; i < SlotCount; i++)
                lists[i] = new List<Renderer>(RenderersPerSlot);
            return lists;
        }

        private static int FindFreeSlot()
        {
            for (int s = 1; s < SlotCount; s++)
                if (!s_Alive[s])
                    return s;
            return -1;
        }

        private static bool IsSupported(Renderer r) => r is MeshRenderer || r is SkinnedMeshRenderer;

        private static void Acquire(Renderer r, int slot)
        {
            var key = (r, s_Layer[slot]);
            if (s_RendererRefs.TryGetValue(key, out var rr))
            {
                rr.Count++;
                rr.OwnerSlot = slot; // последний Show владеет рендерером в своём слое
                s_RendererRefs[key] = rr;
            }
            else
            {
                s_RendererRefs.Add(key, new RendererRef { Count = 1, OwnerSlot = slot });
            }
        }

        private static void Release(int slot)
        {
            var list = s_Renderers[slot];
            s_Alive[slot] = false;
            s_Version[slot]++;
            s_Style[slot] = null;
            s_PrevStyle[slot] = null;
            s_AliveCount--;

            for (int i = 0; i < list.Count; i++)
            {
                var r = list[i];
                var key = (r, s_Layer[slot]);
                if (ReferenceEquals(r, null) || !s_RendererRefs.TryGetValue(key, out var rr))
                    continue;

                rr.Count--;
                if (rr.Count <= 0)
                {
                    s_RendererRefs.Remove(key);
                    continue;
                }

                if (rr.OwnerSlot == slot)
                    rr.OwnerSlot = FindOtherOwner(r, s_Layer[slot]);
                s_RendererRefs[key] = rr;
            }
            list.Clear();
        }

        private static int FindOtherOwner(Renderer r, int layer)
        {
            for (int s = SlotCount - 1; s >= 1; s--)
                if (s_Alive[s] && s_Layer[s] == layer && s_Renderers[s].Contains(r))
                    return s;
            return 0;
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
                s_Renderers[s].Clear();
            }
            s_RendererRefs.Clear();
            s_Scratch.Clear();
            s_AliveCount = 0;
        }
    }
}
