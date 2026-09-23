using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Exerussus.Outline.Lab
{
    /// <summary>
    /// Вторая фаза бенчмарка — витрина: камера переезжает к отдельному объекту, подсветки сцены снимаются,
    /// и на объект по очереди накладываются все стили из <see cref="showcaseStyles"/> и все типы паттернов.
    /// Для каждого — прогрев, замер кадра и снимок. База — тот же кадр без подсветки (в начале и в конце).
    /// </summary>
    public sealed partial class OutlineBenchmark
    {
        [Header("Витрина")]
        [SerializeField, Tooltip("Прогнать витрину стилей после общей сцены.")]
        private bool runShowcase = true;
        [SerializeField, Tooltip("Объект витрины (вращается во время показа).")]
        private Transform showcaseObject;
        [SerializeField, Tooltip("Точка обзора витрины.")]
        private Transform showcaseView;
        [SerializeField, Tooltip("Стили по очереди. Типы паттернов добавляются автоматически.")]
        private OutlineStyle[] showcaseStyles = Array.Empty<OutlineStyle>();
        [SerializeField, Min(0.5f), Tooltip("Замер одного стиля, с.")]
        private float showcaseSeconds = 2f;
        [SerializeField, Min(0f)] private float showcaseWarmup = 1f;
        [SerializeField, Tooltip("Скорость вращения объекта витрины, град/с.")]
        private float showcaseSpin = 20f;

        private struct ShowcaseItem
        {
            public string Name;
            public OutlineStyle Style; // null — база без подсветки
            public Action<Rendering.OutlineSettings> Tweak; // правка настроек фичи на время замера
            public float Avg;
            public float P95;
            public float Cpu;
            public int Samples;
            public Rendering.OutlineStats Stats;
        }

        private readonly List<ShowcaseItem> _items = new(48);
        private readonly List<OutlineStyle> _tempStyles = new(16);
        private readonly List<OutlineTarget> _disabledTargets = new(64);
        private MonoBehaviour _disabledDemo;
        private bool _showcaseActive;
        private bool _showcaseRan;
        private int _itemIndex;
        private float _itemStart;
        private OutlineHandle _itemHandle;

        private string ShowcaseStatus => _itemIndex < _items.Count
            ? $"витрина {_itemIndex + 1}/{_items.Count}: {_items[_itemIndex].Name}"
            : "витрина";

        private bool BeginShowcase()
        {
            if (!runShowcase || _showcaseRan || showcaseObject == null || showcaseView == null || viewCamera == null)
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

            JsonUtility.FromJsonOverwrite(_savedSettings, feature.settings);
            feature.settings.debugView = Rendering.OutlineDebugView.None;
            feature.SetActive(true);
            ApplyView(false);
            var tr = viewCamera.transform;
            _savedCamPos = tr.position;
            _savedCamRot = tr.rotation;
            _camMoved = true;
            tr.SetPositionAndRotation(showcaseView.position, showcaseView.rotation);

            BuildItems();
            // прогрев вариантов шейдеров: каждый стиль по паре кадров до замеров, иначе компиляция попадает в первый замер
            StartCoroutine(PrewarmThenStart());
            return true;
        }

        private IEnumerator PrewarmThenStart()
        {
            _capturing = true; // Update витрины ждёт
            foreach (var it in _items)
            {
                if (it.Style == null)
                    continue;
                _itemHandle.Hide();
                _itemHandle = OutlineApi.Show(showcaseObject.gameObject, it.Style, OutlineOptions.InGroup(1, 0f));
                yield return null;
                yield return null;
            }
            _itemHandle.Hide();
            _capturing = false;
            _showcaseActive = true;
            StartItem(0);
        }

        private void BuildItems()
        {
            _items.Clear();
            _items.Add(new ShowcaseItem { Name = "без подсветки" });
            foreach (var s in showcaseStyles)
            {
                if (s != null)
                    _items.Add(new ShowcaseItem { Name = s.name.Replace("Style_", ""), Style = s });
            }

            // каждый тип паттерна на одном базовом стиле: заливка + свечение, поверхность объекта
            Texture2D tex = null;
            foreach (var s in showcaseStyles)
            {
                if (s != null && s.patternTexture != null)
                {
                    tex = s.patternTexture;
                    break;
                }
            }
            for (int type = 1; type <= (int)OutlinePatternType.Texture; type++)
            {
                var p = (OutlinePatternType)type;
                if (p == OutlinePatternType.Texture && tex == null)
                    continue;
                var st = ScriptableObject.CreateInstance<OutlineStyle>();
                st.hideFlags = HideFlags.HideAndDontSave;
                st.name = $"Pattern {p}";
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
                _tempStyles.Add(st);
                _items.Add(new ShowcaseItem { Name = $"паттерн {p}", Style = st });
            }
            // разложение постоянной цены: пустой стиль (запись есть, рисовать нечего) и варианты настроек
            var empty = ScriptableObject.CreateInstance<OutlineStyle>();
            empty.hideFlags = HideFlags.HideAndDontSave;
            empty.name = "Empty";
            empty.outerColor = new Color(1f, 1f, 1f, 0f);
            empty.outerWidth = 1f;
            empty.innerColor = new Color(1f, 1f, 1f, 0f);
            _tempStyles.Add(empty);
            _items.Add(new ShowcaseItem { Name = "пустой стиль", Style = empty });
            _items.Add(new ShowcaseItem { Name = "пустой, без сглаживания", Style = empty,
                Tweak = st => { st.edgeAntialiasing = Rendering.OutlineEdgeAA.Off; st.edgeAntialiasingWebGL = Rendering.OutlineEdgeAA.Off; } });
            _items.Add(new ShowcaseItem { Name = "пустой, без scissor", Style = empty, Tweak = st => st.scissor = false });

            OutlineStyle probe = null;
            foreach (var s in showcaseStyles)
            {
                if (s != null && s.name.EndsWith("Selected"))
                    probe = s;
            }
            if (probe != null)
            {
                _items.Add(new ShowcaseItem { Name = "Selected, без сглаживания", Style = probe,
                    Tweak = st => { st.edgeAntialiasing = Rendering.OutlineEdgeAA.Off; st.edgeAntialiasingWebGL = Rendering.OutlineEdgeAA.Off; } });
                _items.Add(new ShowcaseItem { Name = "Selected, поле ×0.25", Style = probe,
                    Tweak = st => { st.autoFieldScale = false; st.fieldScale = 0.25f; st.fieldScaleWebGL = 0.25f; } });
                _items.Add(new ShowcaseItem { Name = "Selected, поле ×1", Style = probe,
                    Tweak = st => { st.autoFieldScale = false; st.fieldScale = 1f; st.fieldScaleWebGL = 1f; } });
                _items.Add(new ShowcaseItem { Name = "Selected, без стыка", Style = probe, Tweak = st => st.seamBlend = 0f });
                _items.Add(new ShowcaseItem { Name = "Selected, без scissor", Style = probe, Tweak = st => st.scissor = false });
            }

            _items.Add(new ShowcaseItem { Name = "без подсветки (повтор)" });
        }

        private void StartItem(int index)
        {
            _itemIndex = index;
            _count = 0;
            _lastStats = default;
            _itemHandle.Hide();
            JsonUtility.FromJsonOverwrite(_savedSettings, feature.settings);
            feature.settings.debugView = Rendering.OutlineDebugView.None;
            _items[index].Tweak?.Invoke(feature.settings);
            var style = _items[index].Style;
            if (style != null)
                _itemHandle = OutlineApi.Show(showcaseObject.gameObject, style, OutlineOptions.InGroup(1, 0f));
            _itemStart = Time.realtimeSinceStartup;
            Debug.Log($"[OutlineBenchmark] {ShowcaseStatus}");
        }

        private void UpdateShowcase()
        {
            showcaseObject.Rotate(0f, showcaseSpin * Time.unscaledDeltaTime, 0f, Space.World);

            float t = Time.realtimeSinceStartup - _itemStart;
            if (t >= showcaseWarmup && _count < MaxSamples)
            {
                int i = _count++;
                _frame[i] = Time.unscaledDeltaTime * 1000f;
                // без подсветки фича не рисует и статистика остаётся от прошлого стиля — не берём её
                var s = _items[_itemIndex].Style != null ? feature.Stats : default;
                _cpu[i] = s.CpuMs;
                if (s.Rendered)
                    _lastStats = s;
            }
            if (t < showcaseWarmup + showcaseSeconds)
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

            if (_shotDir != null && item.Style != null)
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
                    string name = _items[index].Name.Replace(' ', '_').Replace(",", "").Replace('×', 'x');
                    File.WriteAllBytes(Path.Combine(_shotDir, $"S{index:00}_{name}.png"), tex.EncodeToPNG());
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

        /// <summary>Вернуть сцену: снять подсветку витрины, включить цели и демо, удалить временные стили.</summary>
        private void EndShowcase()
        {
            if (!_showcaseActive)
                return;
            _showcaseActive = false;
            _itemHandle.Hide();
            _itemHandle = OutlineHandle.Invalid;
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

        private void AppendShowcaseLog()
        {
            if (_items.Count == 0)
                return;
            var inv = System.Globalization.CultureInfo.InvariantCulture;

            float baseSum = 0f;
            int baseCount = 0;
            foreach (var it in _items)
            {
                if (it.Style == null && it.Samples > 0)
                {
                    baseSum += it.Avg;
                    baseCount++;
                }
            }
            float baseline = baseCount > 0 ? baseSum / baseCount : 0f;

            _log.Append("#\n# Витрина: один объект крупным планом, ")
                .Append(showcaseWarmup.ToString(inv)).Append(" с прогрев + ")
                .Append(showcaseSeconds.ToString(inv)).Append(" с замер на стиль\n");
            _log.Append("# колонки: кадр avg/p95 мс | прирост к «без подсветки», мс | CPU записи, мс | масштаб, кроп, проходы, стоимость, aa\n");
            for (int i = 0; i < _items.Count; i++)
            {
                var it = _items[i];
                if (it.Samples == 0)
                    continue;
                _log.Append("[S").Append(i.ToString("00")).Append("] ").Append(it.Name.PadRight(24)).Append(" | ")
                    .Append(it.Avg.ToString("0.00", inv)).Append('/').Append(it.P95.ToString("0.00", inv));
                if (it.Style != null && baseCount > 0)
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
            if (baseCount >= 2)
            {
                _log.Append("# дрейф базы витрины: ").Append(_items[0].Avg.ToString("0.00", inv)).Append(" → ")
                    .Append(_items[_items.Count - 1].Avg.ToString("0.00", inv)).Append(" мс\n");
            }
        }
    }
}
