using System;
using System.Collections.Generic;
using UnityEngine;

namespace Exerussus.Outline
{
    /// <summary>
    /// Временные эффекты: запускаются, отыгрывают анимацию и снимаются сами, возвращая объект как был.
    /// <list type="bullet">
    /// <item><see cref="DissolveOut"/> — объект растворяется и остаётся скрытым (ничего не стоит), пока не вызвано
    /// <see cref="DissolveIn"/> или <see cref="Restore"/>.</item>
    /// <item><see cref="DissolveIn"/> — обратная анимация из растворённого состояния; по окончании эффект снят.</item>
    /// <item><see cref="Pulse"/>, <see cref="Flash"/> — одна вспышка подсветки или засветки объекта.</item>
    /// </list>
    /// Эффект — отдельная запись OutlineApi: пока он идёт, подсветка объекта из OutlineApi не рисуется
    /// (рендерером владеет последняя запись) и возвращается по окончании. Время — <see cref="OutlineClock"/>.
    /// </summary>
    public static class OutlineFx
    {
        private enum Kind
        {
            DissolveOut,
            DissolveIn,
            Pulse,
        }

        private struct Active
        {
            public Kind Kind;
            public GameObject Target;
            public OutlineHandle Handle;
            public float End;
            public float FadeOutAt;    // пульс: когда начать гаснуть (-1 — уже)
            public float FadeOutTime;
            public Action OnComplete;
        }

        private struct HiddenState
        {
            public bool ForceOff;
        }

        private static readonly List<Active> s_Active = new(16);
        private static readonly Dictionary<Renderer, HiddenState> s_Dissolved = new(32);
        private static readonly List<Renderer> s_RendererScratch = new(16);
        private static readonly List<GameObject> s_PendingHide = new(8);
        private static readonly List<Action> s_PendingCallbacks = new(8);
        private static readonly Dictionary<Color, OutlineStyle> s_FlashStyles = new();
        private static OutlineStyle s_DissolveStyle;
        private static OutlineStyle s_PulseStyle;
        private static int s_GroupCounter;

        /// <summary>Идут ли эффекты (для отладки).</summary>
        public static int ActiveCount => s_Active.Count;

        /// <summary>Слой временных подсветок (пульс, вспышка): поверх обычной подсветки в слое 0.</summary>
        public const int OverlayLayer = 1;
        /// <summary>Слой растворения: верхний — эффект объекта рисуется поверх остальных слоёв.</summary>
        public const int ObjectLayer = OutlineApi.MaxLayers - 1;

        /// <summary>Скрыт ли рендерер растворением (для рендера: такие не рисуются ни в одном слое).</summary>
        internal static bool IsRendererHidden(Renderer r) => s_Dissolved.Count > 0 && s_Dissolved.ContainsKey(r);

        /// <summary>Растворён ли объект эффектом <see cref="DissolveOut"/>.</summary>
        public static bool IsDissolved(GameObject target)
        {
            if (target == null)
                return false;
            target.GetComponentsInChildren(true, s_RendererScratch);
            foreach (var r in s_RendererScratch)
            {
                if (s_Dissolved.ContainsKey(r))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Растворить объект за duration. По окончании объект скрыт, пока не вызван DissolveIn/Restore.
        /// style — стиль кромки и шума (нужен seeThrough-совместимый: по умолчанию встроенный).
        /// </summary>
        public static OutlineHandle DissolveOut(GameObject target, float duration = 0.6f, OutlineStyle style = null, Action onComplete = null)
        {
            if (target == null)
                return OutlineHandle.Invalid;
            float from = TakeOverDissolve(target, 0f);
            var h = Show(target, style != null ? style : DissolveStyle, 0f, ObjectLayer);
            OutlineApi.SetDissolve(h, from, 1f, duration * (1f - from));
            Add(new Active { Kind = Kind.DissolveOut, Target = target, Handle = h, End = OutlineClock.Now + duration * (1f - from), FadeOutAt = -1f, OnComplete = onComplete });
            return h;
        }

        /// <summary>Проявить объект из растворённого состояния за duration; по окончании эффект снят.</summary>
        public static OutlineHandle DissolveIn(GameObject target, float duration = 0.6f, OutlineStyle style = null, Action onComplete = null)
        {
            if (target == null)
                return OutlineHandle.Invalid;
            float from = TakeOverDissolve(target, 1f);
            Unhide(target);
            var h = Show(target, style != null ? style : DissolveStyle, 0f, ObjectLayer);
            OutlineApi.SetDissolve(h, from, 0f, duration * from);
            Add(new Active { Kind = Kind.DissolveIn, Target = target, Handle = h, End = OutlineClock.Now + duration * from, FadeOutAt = -1f, OnComplete = onComplete });
            return h;
        }

        /// <summary>Одна вспышка подсветки style (по умолчанию — встроенная): нарастает на rise доле длительности и гаснет.</summary>
        public static OutlineHandle Pulse(GameObject target, OutlineStyle style = null, float duration = 0.45f, float rise = 0.3f,
            Action onComplete = null, int layer = OverlayLayer)
        {
            if (target == null)
                return OutlineHandle.Invalid;
            rise = Mathf.Clamp01(rise);
            float up = duration * rise;
            var h = Show(target, style != null ? style : PulseStyle, up, layer);
            float now = OutlineClock.Now;
            Add(new Active { Kind = Kind.Pulse, Target = target, Handle = h, End = now + duration, FadeOutAt = now + up, FadeOutTime = duration - up, OnComplete = onComplete });
            return h;
        }

        /// <summary>Короткая засветка объекта цветом color (HDR, альфа — сила).</summary>
        public static OutlineHandle Flash(GameObject target, Color color, float duration = 0.2f, Action onComplete = null,
            int layer = OverlayLayer) =>
            Pulse(target, FlashStyle(color), duration, 0.15f, onComplete, layer);

        /// <summary>Сразу снять эффекты с объекта и вернуть его (в том числе из растворённого состояния).</summary>
        public static void Restore(GameObject target)
        {
            if (target == null)
                return;
            for (int i = s_Active.Count - 1; i >= 0; i--)
            {
                if (s_Active[i].Target == target)
                {
                    s_Active[i].Handle.Hide();
                    s_Active.RemoveAt(i);
                }
            }
            Unhide(target);
        }

        // ------------------------------------------------------------------ Внутреннее (зовётся перед рендером кадра)

        internal static void Tick(float now)
        {
            for (int i = s_Active.Count - 1; i >= 0; i--)
            {
                var a = s_Active[i];
                if (a.Target == null || !a.Handle.IsAlive)
                {
                    // пульс снимается сам по FadeOutAndHide — колбэк всё равно положен
                    if (a.Target != null && a.Kind == Kind.Pulse && now >= a.End - 0.05f && a.OnComplete != null)
                        s_PendingCallbacks.Add(a.OnComplete);
                    s_Active.RemoveAt(i);
                    continue;
                }
                if (a.Kind == Kind.Pulse && a.FadeOutAt >= 0f && now >= a.FadeOutAt)
                {
                    a.Handle.FadeOutAndHide(a.FadeOutTime);
                    a.FadeOutAt = -1f;
                    s_Active[i] = a;
                }
                if (now < a.End)
                    continue;

                if (a.Kind != Kind.Pulse)
                    a.Handle.Hide();
                if (a.Kind == Kind.DissolveOut)
                    s_PendingHide.Add(a.Target);
                if (a.OnComplete != null)
                    s_PendingCallbacks.Add(a.OnComplete);
                s_Active.RemoveAt(i);
            }
        }

        /// <summary>После синхронизации прозрачности: скрыть растворённые объекты, вызвать колбэки.</summary>
        internal static void PostSync()
        {
            for (int i = 0; i < s_PendingHide.Count; i++)
                HideRenderers(s_PendingHide[i]);
            s_PendingHide.Clear();

            if (s_PendingCallbacks.Count == 0)
                return;
            // колбэк может запустить новый эффект — работаем с копией
            var callbacks = s_PendingCallbacks.ToArray();
            s_PendingCallbacks.Clear();
            foreach (var cb in callbacks)
            {
                try
                {
                    cb();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        internal static void RestoreAll()
        {
            foreach (var pair in s_Dissolved)
            {
                if (pair.Key != null)
                    pair.Key.forceRenderingOff = pair.Value.ForceOff;
            }
            s_Dissolved.Clear();
            s_Active.Clear();
            s_PendingHide.Clear();
            s_PendingCallbacks.Clear();
        }

        // ------------------------------------------------------------------ Помощники

        private static OutlineHandle Show(GameObject target, OutlineStyle style, float fadeIn, int layer)
        {
            // своя группа на каждый эффект: силуэты разных объектов не сливаются
            s_GroupCounter = (s_GroupCounter + 1) % 1000;
            var o = OutlineOptions.InLayer(layer, 100000 + s_GroupCounter, fadeIn);
            return OutlineApi.Show(target, style, o);
        }

        private static void Add(in Active a)
        {
            if (a.Handle.IsAlive)
                s_Active.Add(a);
        }

        /// <summary>Снять идущее растворение объекта; вернуть его текущую долю (или fallback).</summary>
        private static float TakeOverDissolve(GameObject target, float fallback)
        {
            float value = fallback;
            for (int i = s_Active.Count - 1; i >= 0; i--)
            {
                var a = s_Active[i];
                if (a.Target != target || a.Kind == Kind.Pulse)
                    continue;
                float v = OutlineApi.EvaluateDissolve(a.Handle);
                if (v >= 0f)
                    value = v;
                a.Handle.Hide();
                s_Active.RemoveAt(i);
            }
            if (fallback >= 1f && !IsDissolved(target) && value >= 1f)
                value = 1f; // DissolveIn видимого объекта — полная анимация появления
            return value;
        }

        private static void HideRenderers(GameObject target)
        {
            if (target == null)
                return;
            target.GetComponentsInChildren(true, s_RendererScratch);
            foreach (var r in s_RendererScratch)
            {
                if (s_Dissolved.ContainsKey(r))
                    continue;
                s_Dissolved[r] = new HiddenState { ForceOff = r.forceRenderingOff };
                r.forceRenderingOff = true;
            }
        }

        private static void Unhide(GameObject target)
        {
            if (target == null)
                return;
            target.GetComponentsInChildren(true, s_RendererScratch);
            foreach (var r in s_RendererScratch)
            {
                if (!s_Dissolved.TryGetValue(r, out var saved))
                    continue;
                r.forceRenderingOff = saved.ForceOff;
                s_Dissolved.Remove(r);
            }
        }

        private static OutlineStyle DissolveStyle
        {
            get
            {
                if (s_DissolveStyle != null)
                    return s_DissolveStyle;
                var s = CreateStyle("OutlineFx Dissolve");
                s.outerColor = new Color(0f, 0f, 0f, 0f);
                s.outerWidth = 0f;
                s.occludedMode = OutlineOccludedMode.Hidden;
                s.patternSpace = OutlinePatternSpace.SurfaceObject;
                s.dissolveScale = 0.22f;
                s.dissolveEdgeWidth = 0.09f;
                s.dissolveEdgeColor = new Color(3f, 1.3f, 0.3f, 1f);
                s.seeThroughShadows = true;
                return s_DissolveStyle = s;
            }
        }

        private static OutlineStyle PulseStyle
        {
            get
            {
                if (s_PulseStyle != null)
                    return s_PulseStyle;
                var s = CreateStyle("OutlineFx Pulse");
                // чисто аддитивное свечение: подсветка слоя 0 под пульсом остаётся видна и лишь ярче вспыхивает
                s.outerColor = new Color(0.9f, 1.1f, 1.5f, 0.8f);
                s.outerWidth = 18f;
                s.outerCurve = new AnimationCurve(new Keyframe(0f, 1f, 0f, -1.8f), new Keyframe(1f, 0f, 0f, 0f));
                s.additive = 1f;
                s.occludedMode = OutlineOccludedMode.Same;
                return s_PulseStyle = s;
            }
        }

        private static OutlineStyle FlashStyle(Color color)
        {
            if (s_FlashStyles.TryGetValue(color, out var cached) && cached != null)
                return cached;
            var s = CreateStyle("OutlineFx Flash");
            // только заливка: слою без контура не нужно поле расстояний — вспышка почти бесплатна
            s.outerColor = new Color(color.r, color.g, color.b, 0f);
            s.outerWidth = 0f;
            s.fillColor = color;
            s.additive = 1f;
            s.occludedMode = OutlineOccludedMode.Hidden;
            if (s_FlashStyles.Count > 32)
                s_FlashStyles.Clear();
            s_FlashStyles[color] = s;
            return s;
        }

        private static OutlineStyle CreateStyle(string name)
        {
            var s = ScriptableObject.CreateInstance<OutlineStyle>();
            s.name = name;
            s.hideFlags = HideFlags.HideAndDontSave;
            return s;
        }
    }
}
