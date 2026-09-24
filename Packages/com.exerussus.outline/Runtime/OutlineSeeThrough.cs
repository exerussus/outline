using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Exerussus.Outline
{
    /// <summary>
    /// Скрытие рендереров, чьи подсветки в режиме прозрачности/маскировки: основной проход камеры их не рисует,
    /// фича рисует на их месте фон и (с objectOpacity) сам объект. Синхронизация — перед рендером кадра;
    /// исходные свойства рендереров возвращаются, когда подсветка снята, погасла или стиль сменился.
    /// В Play тень сохраняется через ShadowsOnly, в редакторе — только forceRenderingOff (не сериализуется).
    /// </summary>
    internal static class OutlineSeeThrough
    {
        private struct Saved
        {
            public bool ForceOff;
            public ShadowCastingMode Shadows;
        }

        private static readonly Dictionary<Renderer, Saved> s_Hidden = new(32);
        private static readonly HashSet<Renderer> s_Desired = new();
        private static readonly List<Renderer> s_Scratch = new(32);
        private static int s_Subscribers;

        public static int HiddenCount => s_Hidden.Count;

#if UNITY_EDITOR
        // forceRenderingOff не сериализуется, но переживает перезагрузку домена в памяти объекта —
        // возвращаем рендереры до перезагрузки и смены режима, иначе объект останется невидимым
        [UnityEditor.InitializeOnLoadMethod]
        private static void HookEditor()
        {
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += RestoreAll;
            UnityEditor.EditorApplication.playModeStateChanged += state =>
            {
                if (state == UnityEditor.PlayModeStateChange.ExitingEditMode || state == UnityEditor.PlayModeStateChange.ExitingPlayMode)
                    RestoreAll();
            };
        }
#endif

        public static void Subscribe()
        {
            if (s_Subscribers++ == 0)
                RenderPipelineManager.beginContextRendering += OnBeginContext;
        }

        public static void Unsubscribe()
        {
            if (s_Subscribers == 0)
                return;
            if (--s_Subscribers == 0)
            {
                RenderPipelineManager.beginContextRendering -= OnBeginContext;
                RestoreAll();
            }
        }

        private static void OnBeginContext(ScriptableRenderContext ctx, List<Camera> cameras)
        {
            float now = OutlineClock.Now;
            OutlineFx.Tick(now);   // завершившиеся эффекты снимают записи
            Sync(now);             // рендереры записей возвращаются к исходным свойствам
            OutlineFx.PostSync();  // растворённые объекты скрываются, колбэки вызываются
        }

        /// <summary>Привести скрытие рендереров к текущим подсветкам.</summary>
        public static void Sync(float now)
        {
            s_Desired.Clear();
            if (OutlineApi.HasAny)
            {
                for (int id = 1; id < OutlineApi.SlotCount; id++)
                {
                    if (!OutlineApi.IsSlotAlive(id))
                        continue;
                    var style = OutlineApi.GetStyle(id);
                    if (style == null)
                        continue;
                    // при плавной смене стиля объект прозрачен, пока прозрачен хоть один из стилей
                    OutlineApi.EvaluateStyleBlend(id, now, out var prevStyle);
                    bool prevSeeThrough = prevStyle != null && prevStyle.seeThrough;
                    bool seeThrough = style.seeThrough || prevSeeThrough || OutlineApi.EvaluateDissolve(id, now) >= 0f;
                    bool keepShadows = style.seeThrough ? style.seeThroughShadows : prevSeeThrough ? prevStyle.seeThroughShadows : style.seeThroughShadows;
                    if (!seeThrough || OutlineApi.EvaluateFade(id, now) <= 1e-4f)
                        continue;
                    int count = OutlineApi.GetRendererCount(id);
                    for (int i = 0; i < count; i++)
                    {
                        var r = OutlineApi.GetRenderer(id, i);
                        if (r != null && OutlineApi.IsOwner(r, id))
                        {
                            s_Desired.Add(r);
                            if (!s_Hidden.ContainsKey(r))
                                Hide(r, keepShadows && Application.isPlaying);
                        }
                    }
                }
            }

            if (s_Hidden.Count == s_Desired.Count)
                return;
            s_Scratch.Clear();
            foreach (var pair in s_Hidden)
            {
                if (pair.Key == null || !s_Desired.Contains(pair.Key))
                    s_Scratch.Add(pair.Key);
            }
            foreach (var r in s_Scratch)
                Restore(r);
            s_Scratch.Clear();
        }

        public static void RestoreAll()
        {
            OutlineFx.RestoreAll();
            s_Scratch.Clear();
            s_Scratch.AddRange(s_Hidden.Keys);
            foreach (var r in s_Scratch)
                Restore(r);
            s_Scratch.Clear();
            s_Hidden.Clear();
        }

        private static void Hide(Renderer r, bool keepShadow)
        {
            s_Hidden[r] = new Saved { ForceOff = r.forceRenderingOff, Shadows = r.shadowCastingMode };
            if (keepShadow && r.shadowCastingMode != ShadowCastingMode.Off)
                r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            else
                r.forceRenderingOff = true;
        }

        private static void Restore(Renderer r)
        {
            if (!s_Hidden.TryGetValue(r, out var saved))
                return;
            s_Hidden.Remove(r);
            if (r == null)
                return;
            r.forceRenderingOff = saved.ForceOff;
            r.shadowCastingMode = saved.Shadows;
        }
    }
}
