using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Exerussus.Outline.Lab
{
    /// <summary>
    /// Вторая фаза бенчмарка — витрина: камера переезжает к отдельной площадке, подсветки сцены снимаются, и все
    /// стили, типы паттернов и временные эффекты по очереди накладываются на ярусы объектов: 1 куб крупным планом,
    /// 3 разные фигуры, 21 фигура. Каждый объект — своя подсветка. Для каждого сочетания — прогрев, замер кадра
    /// к базе своего яруса (тот же кадр без подсветки) и снимок. В конце — сводная таблица «стиль × ярус».
    /// </summary>
    public sealed partial class OutlineBenchmark
    {
        [Serializable]
        public struct ShowcaseTier
        {
            public string name;
            [Tooltip("Точка обзора яруса.")]
            public Transform view;
            [Tooltip("Объекты яруса (остальные объекты витрины на время яруса выключаются).")]
            public Transform[] objects;
        }

        [Header("Витрина")]
        [SerializeField, Tooltip("Прогнать витрину стилей после общей сцены.")]
        private bool runShowcase = true;
        [SerializeField, Tooltip("Ярусы витрины: обычно 1, 3 и 21 объект.")]
        private ShowcaseTier[] showcaseTiers = Array.Empty<ShowcaseTier>();
        [SerializeField, Tooltip("Стили по очереди. Типы паттернов и временные эффекты добавляются автоматически.")]
        private OutlineStyle[] showcaseStyles = Array.Empty<OutlineStyle>();
        [SerializeField, Min(0.5f), Tooltip("Замер одного стиля, с.")]
        private float showcaseSeconds = 2f;
        [SerializeField, Min(0f)] private float showcaseWarmup = 1f;
        [SerializeField, Min(0f), Tooltip("Прогрев витрины до первого замера, с: частоты GPU успевают устояться, база первого яруса не завышена.")]
        private float showcasePreheat = 8f;
        [SerializeField, Tooltip("Скорость вращения объектов витрины, град/с.")]
        private float showcaseSpin = 20f;
        [SerializeField, Tooltip("Разложение постоянной цены (пустой стиль, без сглаживания, …) — только на первом ярусе.")]
        private bool showcaseBreakdown = true;
        [SerializeField, Tooltip("Этап «движение»: объект первого яруса вращается и ездит влево-вправо — видно, как ведут себя " +
                                 "текстура и паттерн в каждом пространстве. Снимки в крайних точках (_a, _b) и в центре.")]
        private bool showcaseMotion = true;
        [SerializeField, Min(0f), Tooltip("Размах движения влево-вправо от исходной точки, м.")]
        private float showcaseMotionAmplitude = 0.8f;
        [SerializeField, Min(0.5f), Tooltip("Период движения (и длительность замера одного пункта этапа), с.")]
        private float showcaseMotionPeriod = 4f;

        private struct ShowcaseItem
        {
            public int Tier;
            public string Name;
            public OutlineStyle Style; // null — база без подсветки
            public Action<Rendering.OutlineSettings> Tweak; // правка настроек фичи на время замера
            public Action<GameObject> Begin; // временный эффект: запуск на каждом объекте в начале замера
            public bool Motion; // этап «движение»: объекты яруса ездят влево-вправо, своя база
            public Action<float> Tick; // каждый кадр замера: время с начала пункта, с
            public bool IsBase => Style == null && Begin == null;
            public float Avg;
            public float P95;
            public float Cpu;
            public int Samples;
            public Rendering.OutlineStats Stats;
        }

        private readonly List<ShowcaseItem> _items = new(160);
        private readonly List<OutlineStyle> _tempStyles = new(16);
        private readonly List<OutlineTarget> _disabledTargets = new(64);
        private readonly List<OutlineHandle> _itemHandles = new(32);
        private readonly List<Transform> _allShowcase = new(32);
        private MonoBehaviour _disabledDemo;
        private bool _showcaseActive;
        private bool _showcaseRan;
        private int _itemIndex;
        private int _activeTier = -1;
        private float _itemStart;
        private readonly List<Vector3> _motionBase = new(4);
        private Transform[] _motionObjects;
        private bool _motionShotA;
        private bool _motionShotB;
        private int _skipSamples;

        private string ShowcaseStatus => _itemIndex < _items.Count
            ? $"витрина {_itemIndex + 1}/{_items.Count}: {TierName(_items[_itemIndex].Tier)} · {_items[_itemIndex].Name}"
            : "витрина";

        private string TierName(int tier) =>
            tier >= 0 && tier < showcaseTiers.Length && !string.IsNullOrEmpty(showcaseTiers[tier].name)
                ? showcaseTiers[tier].name
                : $"ярус {tier + 1}";

        private bool BeginShowcase()
        {
            if (!runShowcase || _showcaseRan || showcaseTiers == null || showcaseTiers.Length == 0 || viewCamera == null)
                return false;
            _showcaseRan = true;

            // сцена без подсветок: компоненты целей и демо выключаем, вернём в конце
            _disabledTargets.Clear();
            foreach (var t in FindObjectsByType<OutlineTarget>(FindObjectsInactive.Exclude))
            {
                if (t.enabled)
                {
                    t.enabled = false;
                    _disabledTargets.Add(t);
                }
            }
            var demo = FindAnyObjectByType<OutlineLabDemo>();
            if (demo != null && demo.enabled)
            {
                demo.enabled = false;
                _disabledDemo = demo;
            }

            _allShowcase.Clear();
            foreach (var tier in showcaseTiers)
            {
                if (tier.objects == null)
                    continue;
                foreach (var o in tier.objects)
                {
                    if (o != null && !_allShowcase.Contains(o))
                        _allShowcase.Add(o);
                }
            }

            JsonUtility.FromJsonOverwrite(_savedSettings, feature.settings);
            feature.settings.debugView = Rendering.OutlineDebugView.None;
            feature.SetActive(true);
            ApplyView(false);
            var tr = viewCamera.transform;
            _savedCamPos = tr.position;
            _savedCamRot = tr.rotation;
            _camMoved = true;
            _activeTier = -1;

            BuildItems();
            StartCoroutine(PrewarmThenStart());
            return true;
        }

        /// <summary>Прогрев вариантов шейдеров: каждый стиль по паре кадров до замеров.</summary>
        private IEnumerator PrewarmThenStart()
        {
            _capturing = true; // Update витрины ждёт
            ActivateTier(0);
            foreach (var it in _items)
            {
                if (it.Style == null || it.Tier != 0)
                    continue;
                HideItemHandles();
                ShowOnTier(0, it.Style);
                yield return null;
                yield return null;
            }
            HideItemHandles();
            // прогрев площадки: кадр без подсветки крутится до стабильных частот
            float until = Time.realtimeSinceStartup + showcasePreheat;
            while (Time.realtimeSinceStartup < until)
                yield return null;
            _capturing = false;
            _showcaseActive = true;
            StartItem(0);
        }

        private void BuildItems()
        {
            _items.Clear();

            // стили для каждого яруса: пресеты, типы паттернов, временные эффекты
            var styles = new List<(string name, OutlineStyle style)>();
            foreach (var s in showcaseStyles)
            {
                if (s != null)
                    styles.Add((s.name.Replace("Style_", ""), s));
            }
            Texture2D tex = null;
            foreach (var s in showcaseStyles)
            {
                if (s != null && s.patternTexture != null)
                {
                    tex = s.patternTexture;
                    break;
                }
            }
            OutlineStyle surfaceProbe = null;
            Texture2D motionTex = tex;
            for (int type = 1; type <= (int)OutlinePatternType.Texture; type++)
            {
                var p = (OutlinePatternType)type;
                if (p == OutlinePatternType.Texture && tex == null)
                    continue;
                var st = TempStyle($"Pattern {p}");
                st.outerColor = new Color(0.6f, 1.6f, 2.2f, 1f);
                st.outerWidth = 10f;
                st.fillColor = new Color(0.3f, 0.8f, 1f, 0.45f);
                st.pattern = p;
                st.patternTexture = tex;
                st.patternLayers = OutlinePatternLayers.All;
                st.patternSpace = OutlinePatternSpace.SurfaceObject;
                st.patternWorldScale = 0.1f;
                st.patternSpeed = 0.2f;
                st.patternFill = 0.5f;
                st.additive = 0.3f;
                styles.Add(($"паттерн {p}", st));
                if (p == OutlinePatternType.Hex)
                    surfaceProbe = st;
            }

            float span = showcaseWarmup + showcaseSeconds;
            var fx = new (string name, Action<GameObject> begin)[]
            {
                // растворение и появление — снимок на середине анимации (длительность ×2)
                ("эффект растворение", go => OutlineFx.DissolveOut(go, span * 2f)),
                ("эффект появление", go => OutlineFx.DissolveIn(go, span * 2f)),
                ("эффект пульс", go => OutlineFx.Pulse(go, null, span * 1.6f, 0.6f)),
                ("эффект вспышка", go => OutlineFx.Flash(go, new Color(2.5f, 2.5f, 2.5f, 0.8f), span * 1.6f)),
            };

            OutlineStyle selected = null;
            OutlineStyle enemy = null;
            foreach (var st in showcaseStyles)
            {
                if (st != null && st.name.EndsWith("Selected"))
                    selected = st;
                if (st != null && st.name.EndsWith("Enemy"))
                    enemy = st;
            }

            for (int tier = 0; tier < showcaseTiers.Length; tier++)
            {
                _items.Add(new ShowcaseItem { Tier = tier, Name = "без подсветки" });
                foreach (var (name, style) in styles)
                    _items.Add(new ShowcaseItem { Tier = tier, Name = name, Style = style });
                foreach (var (name, begin) in fx)
                    _items.Add(new ShowcaseItem { Tier = tier, Name = name, Begin = begin });
                // два слоя сразу: выделение в слое 0 и пульс поверх в слое 1
                if (selected != null)
                    _items.Add(new ShowcaseItem { Tier = tier, Name = "Selected + пульс (2 слоя)", Style = selected,
                        Begin = go => OutlineFx.Pulse(go, null, span * 1.6f, 0.6f) });
                if (enemy != null && selected != null)
                    AddStyleSwap(tier, selected, enemy);
                if (tier == 0 && showcaseBreakdown)
                    AddBreakdown(tier);
                if (showcaseBreakdown && surfaceProbe != null && (tier == 0 || tier == showcaseTiers.Length - 1))
                    AddSurfaceBreakdown(tier, surfaceProbe);
            }
            if (showcaseMotion)
                AddMotionStage(motionTex);
            _items.Add(new ShowcaseItem { Tier = 0, Name = "без подсветки (повтор)" });
        }

        /// <summary>
        /// Этап «движение»: один объект вращается и ездит влево-вправо. Заливка текстурой и паттерн в каждом
        /// пространстве, пресеты с поверхностью или текстурой.
        /// </summary>
        private void AddMotionStage(Texture2D tex)
        {
            _items.Add(new ShowcaseItem { Tier = 0, Name = "движение: без подсветки", Motion = true });
            var spaces = new[] { OutlinePatternSpace.Screen, OutlinePatternSpace.Object,
                OutlinePatternSpace.SurfaceObject, OutlinePatternSpace.SurfaceWorld };
            if (tex != null)
            {
                foreach (var space in spaces)
                {
                    var st = TempStyle($"Motion Fill {space}");
                    st.outerColor = new Color(1.4f, 1.2f, 2f, 1f);
                    st.outerWidth = 10f;
                    st.fillColor = new Color(0.8f, 0.7f, 1.2f, 0.7f);
                    st.fillTexture = tex;
                    // экран — тайл в px, остальные пространства — в мировых единицах
                    st.fillTextureTiling = space == OutlinePatternSpace.Screen ? 48f : 0.25f;
                    st.fillTextureStrength = 1f;
                    st.patternSpace = space;
                    _items.Add(new ShowcaseItem { Tier = 0, Name = $"движение: заливка {space}", Style = st, Motion = true });
                }
            }
            foreach (var space in spaces)
            {
                var st = TempStyle($"Motion Hex {space}");
                st.outerColor = new Color(0.6f, 1.6f, 2.2f, 1f);
                st.outerWidth = 10f;
                st.fillColor = new Color(0.3f, 0.8f, 1f, 0.45f);
                st.pattern = OutlinePatternType.Hex;
                st.patternLayers = OutlinePatternLayers.All;
                st.patternSpace = space;
                st.patternScale = 14f;
                st.patternWorldScale = 0.1f;
                st.patternFill = 0.5f;
                st.additive = 0.3f;
                _items.Add(new ShowcaseItem { Tier = 0, Name = $"движение: паттерн Hex {space}", Style = st, Motion = true });
            }
            foreach (var st in showcaseStyles)
            {
                if (st == null)
                    continue;
                bool surface = st.patternSpace == OutlinePatternSpace.SurfaceObject || st.patternSpace == OutlinePatternSpace.SurfaceWorld;
                if (surface || (st.fillTexture != null && st.fillTextureStrength > 0f))
                    _items.Add(new ShowcaseItem { Tier = 0, Name = $"движение: {ShortName(st)}", Style = st, Motion = true });
            }
        }

        /// <summary>Плавная смена стиля: каждую секунду Selected ↔ Enemy с переходом 0.5 с (в переходе запись считается дважды).</summary>
        private void AddStyleSwap(int tier, OutlineStyle a, OutlineStyle b)
        {
            int flips = 0;
            _items.Add(new ShowcaseItem { Tier = tier, Name = "смена стиля Selected ↔ Enemy", Style = a,
                Tick = t =>
                {
                    int n = Mathf.FloorToInt(t);
                    if (n == flips)
                        return;
                    flips = n;
                    var target = (n & 1) == 1 ? b : a;
                    foreach (var h in _itemHandles)
                        h.SetStyle(target, 0.5f);
                } });
        }

        /// <summary>
        /// Разложение цены координат поверхности (Surface*): эталон в Object, полный путь и пути с отключёнными частями
        /// (settings.surfaceDiag) и прежний путь через MSAA-цель, два круга подряд — видно разброс. Картинка в диагностических пунктах неверная.
        /// </summary>
        private void AddSurfaceBreakdown(int tier, OutlineStyle surface)
        {
            var obj = Instantiate(surface);
            obj.hideFlags = HideFlags.HideAndDontSave;
            obj.name = "Pattern Hex Object";
            obj.patternSpace = OutlinePatternSpace.Object;
            _tempStyles.Add(obj);
            Action<Rendering.OutlineSettings> none = st => { };
            Action<Rendering.OutlineSettings> noAA = st => { st.edgeAntialiasing = Rendering.OutlineEdgeAA.Off; st.edgeAntialiasingWebGL = Rendering.OutlineEdgeAA.Off; };
            for (int round = 1; round <= 2; round++)
            {
                string r = round == 1 ? "" : " (2)";
                _items.Add(new ShowcaseItem { Tier = tier, Name = "поверхность: эталон Object" + r, Style = obj, Tweak = none });
                _items.Add(new ShowcaseItem { Tier = tier, Name = "поверхность: полная" + r, Style = surface, Tweak = none });
                _items.Add(new ShowcaseItem { Tier = tier, Name = "поверхность: без чтения" + r, Style = surface,
                    Tweak = st => st.surfaceDiag = Rendering.OutlineSurfaceDiag.NoRead });
                _items.Add(new ShowcaseItem { Tier = tier, Name = "поверхность: без записи" + r, Style = surface,
                    Tweak = st => st.surfaceDiag = Rendering.OutlineSurfaceDiag.NoWrite });
                _items.Add(new ShowcaseItem { Tier = tier, Name = "поверхность: прежняя MSAA-цель" + r, Style = surface,
                    Tweak = st => st.surfaceDiag = Rendering.OutlineSurfaceDiag.MsaaTarget });
            }
            _items.Add(new ShowcaseItem { Tier = tier, Name = "поверхность: эталон Object, без сглаж.", Style = obj, Tweak = noAA });
            _items.Add(new ShowcaseItem { Tier = tier, Name = "поверхность: полная, без сглаж.", Style = surface, Tweak = noAA });
        }

        private float ItemSeconds(in ShowcaseItem item) => item.Motion ? showcaseMotionPeriod : showcaseSeconds;

        // смещение объекта этапа «движение»: фаза 0 — начало замера, 0.25 и 0.75 — крайние точки
        private void ApplyMotion(float t)
        {
            if (_motionObjects == null)
                return;
            float phase = (t - showcaseWarmup) / showcaseMotionPeriod;
            var right = viewCamera.transform.right;
            right.y = 0f;
            right = right.sqrMagnitude > 1e-6f ? right.normalized : Vector3.right;
            var offset = right * (showcaseMotionAmplitude * Mathf.Sin(phase * 2f * Mathf.PI));
            for (int i = 0; i < _motionObjects.Length && i < _motionBase.Count; i++)
            {
                if (_motionObjects[i] != null)
                    _motionObjects[i].position = _motionBase[i] + offset;
            }
        }

        private void RestoreMotion()
        {
            if (_motionObjects == null)
                return;
            for (int i = 0; i < _motionObjects.Length && i < _motionBase.Count; i++)
            {
                if (_motionObjects[i] != null)
                    _motionObjects[i].position = _motionBase[i];
            }
            _motionObjects = null;
            _motionBase.Clear();
        }

        private static string ShortName(OutlineStyle st)
        {
            string n = st.name;
            int dot = n.LastIndexOf('_');
            return dot >= 0 && dot + 1 < n.Length ? n.Substring(dot + 1) : n;
        }

        /// <summary>Разложение постоянной цены: пустой стиль (запись есть, рисовать нечего) и варианты настроек.</summary>
        private void AddBreakdown(int tier)
        {
            var empty = TempStyle("Empty");
            empty.outerColor = new Color(1f, 1f, 1f, 0f);
            empty.outerWidth = 1f;
            empty.innerColor = new Color(1f, 1f, 1f, 0f);
            Action<Rendering.OutlineSettings> noAA = st => { st.edgeAntialiasing = Rendering.OutlineEdgeAA.Off; st.edgeAntialiasingWebGL = Rendering.OutlineEdgeAA.Off; };
            _items.Add(new ShowcaseItem { Tier = tier, Name = "пустой стиль", Style = empty });
            _items.Add(new ShowcaseItem { Tier = tier, Name = "пустой, без сглаживания", Style = empty, Tweak = noAA });
            _items.Add(new ShowcaseItem { Tier = tier, Name = "пустой, без scissor", Style = empty, Tweak = st => st.scissor = false });

            OutlineStyle probe = null;
            foreach (var s in showcaseStyles)
            {
                if (s != null && s.name.EndsWith("Selected"))
                    probe = s;
            }
            if (probe == null)
                return;
            _items.Add(new ShowcaseItem { Tier = tier, Name = "Selected, без сглаживания", Style = probe, Tweak = noAA });
            _items.Add(new ShowcaseItem { Tier = tier, Name = "Selected, поле ×0.25", Style = probe,
                Tweak = st => { st.autoFieldScale = false; st.fieldScale = 0.25f; st.fieldScaleWebGL = 0.25f; } });
            _items.Add(new ShowcaseItem { Tier = tier, Name = "Selected, поле ×1", Style = probe,
                Tweak = st => { st.autoFieldScale = false; st.fieldScale = 1f; st.fieldScaleWebGL = 1f; } });
            _items.Add(new ShowcaseItem { Tier = tier, Name = "Selected, без стыка", Style = probe, Tweak = st => st.seamBlend = 0f });
            _items.Add(new ShowcaseItem { Tier = tier, Name = "Selected, без scissor", Style = probe, Tweak = st => st.scissor = false });
        }

        private OutlineStyle TempStyle(string name)
        {
            var st = ScriptableObject.CreateInstance<OutlineStyle>();
            st.hideFlags = HideFlags.HideAndDontSave;
            st.name = name;
            _tempStyles.Add(st);
            return st;
        }

        /// <summary>Включить объекты яруса, выключить остальные, переставить камеру.</summary>
        private void ActivateTier(int tier)
        {
            if (_activeTier == tier)
                return;
            _activeTier = tier;
            var t = showcaseTiers[tier];
            foreach (var o in _allShowcase)
            {
                bool on = false;
                if (t.objects != null)
                {
                    foreach (var x in t.objects)
                        on |= x == o;
                }
                o.gameObject.SetActive(on);
            }
            if (t.view != null)
                viewCamera.transform.SetPositionAndRotation(t.view.position, t.view.rotation);
        }

        private void ShowOnTier(int tier, OutlineStyle style)
        {
            var objs = showcaseTiers[tier].objects;
            if (objs == null)
                return;
            for (int i = 0; i < objs.Length; i++)
            {
                if (objs[i] != null)
                    _itemHandles.Add(OutlineApi.Show(objs[i].gameObject, style, OutlineOptions.InGroup(1 + i, 0f)));
            }
        }

        private void HideItemHandles()
        {
            foreach (var h in _itemHandles)
                h.Hide();
            _itemHandles.Clear();
        }

        private void RestoreFx()
        {
            foreach (var o in _allShowcase)
            {
                if (o != null)
                    OutlineFx.Restore(o.gameObject);
            }
        }

        private void StartItem(int index)
        {
            _itemIndex = index;
            _count = 0;
            _lastStats = default;
            HideItemHandles();
            RestoreFx();
            RestoreMotion();

            var item = _items[index];
            ActivateTier(item.Tier);
            _motionShotA = false;
            _motionShotB = false;
            _skipSamples = 0;
            if (item.Motion)
            {
                _motionObjects = showcaseTiers[item.Tier].objects;
                _motionBase.Clear();
                foreach (var o in _motionObjects)
                    _motionBase.Add(o != null ? o.position : Vector3.zero);
            }
            JsonUtility.FromJsonOverwrite(_savedSettings, feature.settings);
            feature.settings.debugView = Rendering.OutlineDebugView.None;
            item.Tweak?.Invoke(feature.settings);
            if (item.Style != null)
                ShowOnTier(item.Tier, item.Style);
            if (item.Begin != null)
            {
                foreach (var o in showcaseTiers[item.Tier].objects)
                {
                    if (o != null)
                        item.Begin(o.gameObject);
                }
            }
            _itemStart = Time.realtimeSinceStartup;
            Debug.Log($"[OutlineBenchmark] {ShowcaseStatus}");
        }

        private void UpdateShowcase()
        {
            var objs = showcaseTiers[_items[_itemIndex].Tier].objects;
            if (objs != null)
            {
                foreach (var o in objs)
                {
                    if (o != null)
                        o.Rotate(0f, showcaseSpin * Time.unscaledDeltaTime, 0f, Space.World);
                }
            }

            float t = Time.realtimeSinceStartup - _itemStart;
            var current = _items[_itemIndex];
            current.Tick?.Invoke(t);
            if (current.Motion)
            {
                ApplyMotion(t);
                // снимки в крайних точках — без остановки замера; кадры со снимком в замер не идут
                float phase = (t - showcaseWarmup) / showcaseMotionPeriod;
                if (_shotDir != null && !current.IsBase)
                {
                    if (!_motionShotA && phase >= 0.25f)
                    {
                        _motionShotA = true;
                        StartCoroutine(CaptureMotionShot(_itemIndex, "a"));
                    }
                    if (!_motionShotB && phase >= 0.75f)
                    {
                        _motionShotB = true;
                        StartCoroutine(CaptureMotionShot(_itemIndex, "b"));
                    }
                }
            }
            if (_skipSamples > 0)
            {
                _skipSamples--;
            }
            else if (t >= showcaseWarmup && _count < MaxSamples)
            {
                int i = _count++;
                _frame[i] = Time.unscaledDeltaTime * 1000f;
                // без подсветки фича не рисует и статистика остаётся от прошлого стиля — не берём её
                var s = !_items[_itemIndex].IsBase ? feature.Stats : default;
                _cpu[i] = s.CpuMs;
                if (s.Rendered)
                    _lastStats = s;
            }
            if (t < showcaseWarmup + ItemSeconds(current))
                return;

            var item = _items[_itemIndex];
            item.Avg = Avg(_frame, _count);
            item.P95 = P95(_frame, _count);
            item.Cpu = Avg(_cpu, _count);
            item.Samples = _count;
            item.Stats = _lastStats;
            _items[_itemIndex] = item;
            BuildLog(false);
            AppendShowcaseLog();
            Flush();

            if (_shotDir != null && !item.IsBase)
                StartCoroutine(CaptureShowcase(_itemIndex));
            else
                AdvanceShowcase();
        }

        private void AdvanceShowcase()
        {
            if (_itemIndex + 1 < _items.Count)
            {
                StartItem(_itemIndex + 1);
                return;
            }
            Finish("готово");
            if (stopPlayOnFinish)
                StopPlay();
        }

        private IEnumerator CaptureShowcase(int index)
        {
            _capturing = true;
            yield return null;
            yield return new WaitForEndOfFrame();
            if (_running && _shotDir != null)
            {
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                try
                {
                    File.WriteAllBytes(Path.Combine(_shotDir, ShotName(index, null)), tex.EncodeToPNG());
                    _shotCount++;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[OutlineBenchmark] Не удалось сохранить снимок: {e.Message}");
                    _shotDir = null;
                }
                finally
                {
                    Destroy(tex);
                }
                GC.Collect();
            }
            _capturing = false;
            if (_running)
                AdvanceShowcase();
        }

        private string ShotName(int index, string suffix)
        {
            var it = _items[index];
            int count = showcaseTiers[it.Tier].objects?.Length ?? 0;
            string name = it.Name.Replace(' ', '_').Replace(",", "").Replace(":", "").Replace('×', 'x');
            return suffix == null ? $"S{index:000}_x{count}_{name}.png" : $"S{index:000}_x{count}_{name}_{suffix}.png";
        }

        /// <summary>Снимок посреди замера (этап «движение»): Update не останавливается, пара кадров выбрасывается.</summary>
        private IEnumerator CaptureMotionShot(int index, string suffix)
        {
            yield return new WaitForEndOfFrame();
            if (!_running || _shotDir == null || _itemIndex != index)
                yield break;
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            try
            {
                File.WriteAllBytes(Path.Combine(_shotDir, ShotName(index, suffix)), tex.EncodeToPNG());
                _shotCount++;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[OutlineBenchmark] Не удалось сохранить снимок: {e.Message}");
            }
            finally
            {
                Destroy(tex);
            }
            _skipSamples = 2;
        }

        /// <summary>Вернуть сцену: снять подсветки и эффекты витрины, включить объекты, цели и демо.</summary>
        private void EndShowcase()
        {
            if (!_showcaseActive)
                return;
            _showcaseActive = false;
            HideItemHandles();
            RestoreFx();
            RestoreMotion();
            foreach (var o in _allShowcase)
            {
                if (o != null)
                    o.gameObject.SetActive(true);
            }
            _activeTier = -1;
            foreach (var t in _disabledTargets)
            {
                if (t != null)
                    t.enabled = true;
            }
            _disabledTargets.Clear();
            if (_disabledDemo != null)
                _disabledDemo.enabled = true;
            _disabledDemo = null;
            foreach (var s in _tempStyles)
            {
                if (s != null)
                    Destroy(s);
            }
            _tempStyles.Clear();
        }

        /// <summary>База яруса — среднее по его замерам «без подсветки».</summary>
        private float TierBaseline(int tier, out bool has) => TierBaseline(tier, false, out has);

        private float TierBaseline(int tier, bool motion, out bool has)
        {
            float sum = 0f;
            int n = 0;
            foreach (var it in _items)
            {
                if (it.Tier == tier && it.Motion == motion && it.IsBase && it.Samples > 0)
                {
                    sum += it.Avg;
                    n++;
                }
            }
            has = n > 0;
            return n > 0 ? sum / n : 0f;
        }

        private void AppendShowcaseLog()
        {
            if (_items.Count == 0)
                return;
            var inv = CultureInfo.InvariantCulture;

            _log.Append("#\n# Витрина: ").Append(showcaseWarmup.ToString(inv)).Append(" с прогрев + ")
                .Append(showcaseSeconds.ToString(inv)).Append(" с замер на стиль; прирост — к базе своего яруса\n");
            _log.Append("# колонки: кадр avg/p95 мс | прирост, мс | CPU записи, мс | масштаб, кроп, проходы, стоимость, aa, записей\n");
            int lastSection = -1;
            for (int i = 0; i < _items.Count; i++)
            {
                var it = _items[i];
                if (it.Samples == 0)
                    continue;
                int section = it.Motion ? 1000 : it.Tier;
                if (section != lastSection)
                {
                    lastSection = section;
                    int count = showcaseTiers[it.Tier].objects?.Length ?? 0;
                    if (it.Motion)
                        _log.Append("## движение: ").Append(TierName(it.Tier)).Append(", влево-вправо ±")
                            .Append(showcaseMotionAmplitude.ToString("0.##", inv)).Append(" м за ")
                            .Append(showcaseMotionPeriod.ToString("0.##", inv)).Append(" с; снимки _a и _b — крайние точки, без суффикса — центр\n");
                    else
                        _log.Append("## ").Append(TierName(it.Tier)).Append(" (объектов: ").Append(count).Append(")\n");
                }
                float baseline = TierBaseline(it.Tier, it.Motion, out bool hasBase);
                _log.Append("[S").Append(i.ToString("000")).Append("] ").Append(it.Name.PadRight(26)).Append(" | ")
                    .Append(it.Avg.ToString("0.00", inv)).Append('/').Append(it.P95.ToString("0.00", inv));
                if (!it.IsBase && hasBase)
                    _log.Append(" | +").Append((it.Avg - baseline).ToString("0.00", inv))
                        .Append(" | cpu ").Append(it.Cpu.ToString("0.000", inv));
                else
                    _log.Append(" | —");
                var s = it.Stats;
                if (s.Rendered)
                {
                    _log.Append(" | x").Append(s.FieldScale.ToString("0.###", inv))
                        .Append(s.DualField ? " +inner" : "")
                        .Append(" cov ").Append((s.Coverage * 100f).ToString("0", inv)).Append('%')
                        .Append(" passes ").Append(s.JfaPasses)
                        .Append(" cost ").Append(s.FieldCost)
                        .Append(" aa ").Append(s.EdgeSamples)
                        .Append(" entries ").Append(s.ActiveEntries);
                }
                _log.Append(" | n=").Append(it.Samples).Append('\n');
            }

            AppendSummary(inv);
            AppendSurfaceBreakdown(inv);

            int first = 0, last = _items.Count - 1;
            if (_items[first].Samples > 0 && _items[last].Samples > 0 && _items[last].IsBase)
            {
                _log.Append("# дрейф базы витрины: ").Append(_items[first].Avg.ToString("0.00", inv)).Append(" → ")
                    .Append(_items[last].Avg.ToString("0.00", inv)).Append(" мс\n");
            }
        }

        /// <summary>Цена частей пути координат поверхности по ярусам — разности пунктов «поверхность: …», среднее двух кругов.</summary>
        private void AppendSurfaceBreakdown(CultureInfo inv)
        {
            bool header = false;
            for (int t = 0; t < showcaseTiers.Length; t++)
            {
                float obj = SurfaceAvg(t, "поверхность: эталон Object");
                float full = SurfaceAvg(t, "поверхность: полная");
                float noRead = SurfaceAvg(t, "поверхность: без чтения");
                float noWrite = SurfaceAvg(t, "поверхность: без записи");
                float legacy = SurfaceAvg(t, "поверхность: прежняя MSAA-цель");
                if (obj < 0f || full < 0f || noRead < 0f || noWrite < 0f || legacy < 0f)
                    continue;
                if (!header)
                {
                    _log.Append("#\n# Поверхность: цена частей, мс (полная − эталон Object = чтение + запись + прочее; прежняя — MSAA-цель маски)\n");
                    header = true;
                }
                _log.Append("  ").Append(TierName(t).PadRight(12))
                    .Append(" | всего ").Append((full - obj).ToString("+0.00;-0.00", inv))
                    .Append(" | чтение ").Append((full - noRead).ToString("+0.00;-0.00", inv))
                    .Append(" | запись ").Append((full - noWrite).ToString("+0.00;-0.00", inv))
                    .Append(" | прочее ").Append((noWrite - obj).ToString("+0.00;-0.00", inv))
                    .Append(" | прежняя всего ").Append((legacy - obj).ToString("+0.00;-0.00", inv)).Append('\n');
            }
        }

        // среднее кадра по пунктам яруса с этим именем (оба круга); -1 — нет замеров
        private float SurfaceAvg(int tier, string name)
        {
            float sum = 0f;
            int n = 0;
            foreach (var it in _items)
            {
                if (it.Tier == tier && !it.Motion && it.Samples > 0 && (it.Name == name || it.Name == name + " (2)"))
                {
                    sum += it.Avg;
                    n++;
                }
            }
            return n > 0 ? sum / n : -1f;
        }

        /// <summary>Сводка: прирост каждого стиля по ярусам в одной строке.</summary>
        private void AppendSummary(CultureInfo inv)
        {
            int tiers = showcaseTiers.Length;
            var baselines = new float[tiers];
            var hasBase = new bool[tiers];
            for (int t = 0; t < tiers; t++)
                baselines[t] = TierBaseline(t, out hasBase[t]);

            _log.Append("#\n# Сводка: прирост к кадру, мс, по ярусам (объектов:");
            for (int t = 0; t < tiers; t++)
                _log.Append(' ').Append(showcaseTiers[t].objects?.Length ?? 0);
            _log.Append(")\n");

            var seen = new HashSet<string>();
            foreach (var it in _items)
            {
                if (it.IsBase || it.Tweak != null || it.Motion || !seen.Add(it.Name))
                    continue;
                _log.Append("  ").Append(it.Name.PadRight(26));
                for (int t = 0; t < tiers; t++)
                {
                    string cell = "—";
                    foreach (var x in _items)
                    {
                        if (x.Tier == t && x.Name == it.Name && x.Tweak == null && !x.Motion && x.Samples > 0 && hasBase[t])
                        {
                            cell = "+" + (x.Avg - baselines[t]).ToString("0.00", inv);
                            break;
                        }
                    }
                    _log.Append(" | ").Append(cell.PadLeft(6));
                }
                _log.Append('\n');
            }
        }
    }
}
