using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Exerussus.Outline.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace Exerussus.Outline.Lab
{
    /// <summary>
    /// Бенчмарк подсветки UI Toolkit: ярусы 1 / 10 / 50 элементов (кнопки, иконки с альфой, текст, карточки);
    /// на каждом — все стили, разложение цены (пустой фильтр, ширины → число шагов JFA), временные эффекты,
    /// плавная смена стиля; в конце — движение одного элемента. Замер кадра к базе своего яруса, снимок на пункт.
    /// Лог — Temp/outline-ui.log, снимки — Temp/OutlineUiShots (в билде — persistentDataPath).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class OutlineUiBenchmark : MonoBehaviour
    {
        [SerializeField] private bool runOnStart = true;
        [SerializeField] private bool stopPlayOnFinish = true;
        [SerializeField] private bool captureScreenshots = true;
        [SerializeField, Min(0f)] private float startDelaySeconds = 3f;
        [SerializeField, Min(0f), Tooltip("Прогрев до первого замера: частоты GPU устаканиваются.")]
        private float preheatSeconds = 6f;
        [SerializeField, Min(0f)] private float warmupSeconds = 0.75f;
        [SerializeField, Min(0.5f)] private float measureSeconds = 2f;
        [SerializeField, Tooltip("Стили по очереди (служебные и пресеты).")]
        private OutlineUiStyle[] styles = Array.Empty<OutlineUiStyle>();
        [SerializeField] private Texture2D icon;
        [SerializeField, Tooltip("Разложение цены на первом и последнем ярусах.")]
        private bool breakdown = true;
        [SerializeField, Tooltip("Этап «движение»: один элемент ездит влево-вправо и масштабируется.")]
        private bool motion = true;

        private static readonly int[] TierSizes = { 1, 10, 50 };
        private const int MaxSamples = 20000;

        private struct Item
        {
            public int Tier;
            public string Name;
            public OutlineUiStyle Style;              // null и Begin == null — база без подсветки
            public Action<VisualElement> Begin;     // временный эффект на каждом элементе
            public Action<float> Tick;              // каждый кадр замера, время с начала пункта
            public bool Motion;
            public bool Diag;                       // разложение — вне сводки
            public OutlineUiDebugView DebugView;    // отладочный вид фильтра на время пункта
            public bool IsBase => Style == null && Begin == null;
            public float Avg;
            public float P95;
            public int Samples;
        }

        private readonly List<Item> _items = new(128);
        private readonly List<OutlineUiStyle> _temp = new(16);
        private readonly List<OutlineUiHandle> _handles = new(64);
        private readonly List<VisualElement>[] _tierElements = { new(), new(), new() };
        private readonly VisualElement[] _tierRoots = new VisualElement[3];
        private readonly float[] _frame = new float[MaxSamples];
        private readonly float[] _sort = new float[MaxSamples];
        private readonly StringBuilder _log = new(16 * 1024);
        private VisualElement _root;
        private string _path;
        private string _shotDir;
        private int _shots;
        private bool _running;

        public bool IsRunning => _running;

        // UI строится в Start: rootVisualElement готов после OnEnable самого UIDocument
        private void BuildUi()
        {
            if (_root != null)
                return;
            var doc = GetComponent<UIDocument>();
            _root = new VisualElement { name = "OutlineUiBench" };
            _root.style.position = Position.Absolute;
            _root.style.left = 0;
            _root.style.top = 0;
            _root.style.right = 0;
            _root.style.bottom = 0;
            _root.style.backgroundColor = new Color(0.09f, 0.1f, 0.13f);
            doc.rootVisualElement.Add(_root);
            for (int t = 0; t < TierSizes.Length; t++)
                _tierRoots[t] = BuildTier(t);
            ShowTier(0);
        }

        private void OnDisable()
        {
            OutlineUi.HideAll();
            _root?.RemoveFromHierarchy();
            _root = null;
            for (int t = 0; t < _tierElements.Length; t++)
                _tierElements[t].Clear();
            foreach (var s in _temp)
            {
                if (s != null)
                    Destroy(s);
            }
            _temp.Clear();
        }

        private IEnumerator Start()
        {
            BuildUi();
            if (!runOnStart)
                yield break;
            _running = true;
            _path = ResolvePath("outline-ui.log");
            _shotDir = captureScreenshots ? PrepareShotDir() : null;
            BuildItems();

            float until = Time.realtimeSinceStartup + startDelaySeconds + preheatSeconds;
            while (Time.realtimeSinceStartup < until)
                yield return null;

            for (int i = 0; i < _items.Count; i++)
            {
                yield return RunItem(i);
                WriteLog(false);
            }
            WriteLog(true);
            _running = false;
            Debug.Log($"[OutlineUiBenchmark] готово: {_path}");
            if (stopPlayOnFinish)
                StopPlay();
        }

        // ------------------------------------------------------------------ Сцена

        private VisualElement BuildTier(int tier)
        {
            var container = new VisualElement();
            container.style.position = Position.Absolute;
            container.style.left = 0;
            container.style.top = 0;
            container.style.right = 0;
            container.style.bottom = 0;
            container.style.flexDirection = FlexDirection.Row;
            container.style.flexWrap = Wrap.Wrap;
            container.style.justifyContent = Justify.Center;
            container.style.alignContent = Align.Center;
            container.style.alignItems = Align.Center;
            _root.Add(container);

            int count = TierSizes[tier];
            float scale = tier == 0 ? 2.2f : tier == 1 ? 1f : 0.62f;
            for (int i = 0; i < count; i++)
            {
                var e = CreateElement(tier == 0 ? 0 : i % 4, scale);
                var slot = new VisualElement();
                // место под свечение вокруг элемента
                float margin = 26f * Mathf.Max(0.7f, scale);
                slot.style.marginLeft = margin;
                slot.style.marginRight = margin;
                slot.style.marginTop = margin;
                slot.style.marginBottom = margin;
                slot.Add(e);
                container.Add(slot);
                _tierElements[tier].Add(e);
            }
            return container;
        }

        private VisualElement CreateElement(int kind, float scale)
        {
            switch (kind)
            {
                case 1:
                {
                    var img = new Image { image = icon, scaleMode = ScaleMode.ScaleToFit };
                    img.style.width = 64 * scale;
                    img.style.height = 64 * scale;
                    return img;
                }
                case 2:
                {
                    var label = new Label("Текст");
                    label.style.fontSize = 30 * scale;
                    label.style.unityFontStyleAndWeight = FontStyle.Bold;
                    label.style.color = new Color(0.95f, 0.95f, 1f);
                    return label;
                }
                case 3:
                {
                    var card = new VisualElement();
                    card.style.width = 96 * scale;
                    card.style.height = 60 * scale;
                    card.style.backgroundColor = new Color(0.2f, 0.35f, 0.7f, 0.95f);
                    float r = 14 * scale;
                    card.style.borderTopLeftRadius = r;
                    card.style.borderTopRightRadius = r;
                    card.style.borderBottomLeftRadius = r;
                    card.style.borderBottomRightRadius = r;
                    return card;
                }
                default:
                {
                    var button = new Button { text = "Кнопка" };
                    button.style.width = 130 * scale;
                    button.style.height = 42 * scale;
                    button.style.fontSize = 16 * scale;
                    return button;
                }
            }
        }

        private void ShowTier(int tier)
        {
            for (int t = 0; t < _tierRoots.Length; t++)
                _tierRoots[t].style.display = t == tier ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ------------------------------------------------------------------ Пункты

        private void BuildItems()
        {
            _items.Clear();
            OutlineUiStyle selected = null, enemy = null, textured = null, fire = null;
            foreach (var s in styles)
            {
                if (s == null)
                    continue;
                string n = ShortName(s);
                if (n == "Selected") selected = s;
                if (n == "Enemy") enemy = s;
                if (n == "Textured") textured = s;
                if (n == "Fire") fire = s;
            }
            float span = warmupSeconds + measureSeconds;

            for (int tier = 0; tier < TierSizes.Length; tier++)
            {
                _items.Add(new Item { Tier = tier, Name = "без подсветки" });
                foreach (var s in styles)
                {
                    if (s != null)
                        _items.Add(new Item { Tier = tier, Name = ShortName(s), Style = s });
                }
                _items.Add(new Item { Tier = tier, Name = "эффект пульс", Begin = e => OutlineUiFx.Pulse(e, null, span * 1.6f, 0.6f) });
                _items.Add(new Item { Tier = tier, Name = "эффект вспышка", Begin = e => OutlineUiFx.Flash(e, new Color(2.5f, 2.5f, 2.5f, 0.8f), span * 1.6f) });
                _items.Add(new Item { Tier = tier, Name = "эффект растворение", Begin = e => OutlineUiFx.DissolveOut(e, span * 2f) });
                _items.Add(new Item { Tier = tier, Name = "эффект появление", Begin = e => OutlineUiFx.DissolveIn(e, span * 2f) });
                if (selected != null && enemy != null)
                    AddStyleSwap(tier, selected, enemy);
                if (breakdown && (tier == 0 || tier == TierSizes.Length - 1))
                    AddBreakdown(tier);
            }
            if (motion)
            {
                _items.Add(new Item { Tier = 0, Name = "движение: без подсветки", Motion = true });
                foreach (var s in new[] { selected, textured, fire })
                {
                    if (s != null)
                        _items.Add(new Item { Tier = 0, Name = "движение: " + ShortName(s), Style = s, Motion = true });
                }
            }
            _items.Add(new Item { Tier = 0, Name = "без подсветки (повтор)" });
        }

        private void AddStyleSwap(int tier, OutlineUiStyle a, OutlineUiStyle b)
        {
            int flips = 0;
            _items.Add(new Item
            {
                Tier = tier, Name = "смена стиля Selected ↔ Enemy", Style = a,
                Tick = t =>
                {
                    int n = Mathf.FloorToInt(t);
                    if (n == flips)
                        return;
                    flips = n;
                    var target = (n & 1) == 1 ? b : a;
                    foreach (var h in _handles)
                        h.SetStyle(target, 0.5f);
                },
            });
        }

        /// <summary>Цена самого фильтра (пустой стиль) и числа шагов JFA (ширина свечения).</summary>
        private void AddBreakdown(int tier)
        {
            var empty = TempStyle("Empty");
            empty.outerColor = new Color(1f, 1f, 1f, 0f);
            empty.innerColor = new Color(1f, 1f, 1f, 0f);
            _items.Add(new Item { Tier = tier, Name = "разложение: пустой фильтр", Style = empty, Diag = true });
            foreach (float w in new[] { 4f, 16f, 60f, 200f })
            {
                var s = TempStyle($"Width {w}");
                s.outerColor = new Color(1f, 0.78f, 0.25f, 1f);
                s.outerWidth = w;
                s.innerColor = new Color(1f, 1f, 1f, 0f);
                _items.Add(new Item { Tier = tier, Name = $"разложение: ширина {w:0} пт", Style = s, Diag = true });
            }
            var inner = TempStyle("Inner");
            inner.outerColor = new Color(1f, 1f, 1f, 0f);
            inner.innerColor = new Color(1f, 0.78f, 0.25f, 1f);
            inner.innerWidth = 4f;
            _items.Add(new Item { Tier = tier, Name = "разложение: только внутренний", Style = inner, Diag = true });
            if (tier != 0)
                return;
            // отладка внутреннего контура: что видит сборка внутри силуэта
            _items.Add(new Item { Tier = tier, Name = "отладка: расстояние внутрь", Style = inner, Diag = true, DebugView = OutlineUiDebugView.InsideDistance });
        }

        private OutlineUiStyle TempStyle(string name)
        {
            var s = ScriptableObject.CreateInstance<OutlineUiStyle>();
            s.hideFlags = HideFlags.HideAndDontSave;
            s.name = name;
            _temp.Add(s);
            return s;
        }

        private IEnumerator RunItem(int index)
        {
            var item = _items[index];
            ShowTier(item.Tier);
            var elements = _tierElements[item.Tier];
            ResetElements(elements);
            _handles.Clear();
            OutlineUi.DebugView = item.DebugView;
            if (item.Style != null)
            {
                foreach (var e in elements)
                    _handles.Add(OutlineUi.Show(e, item.Style));
            }
            if (item.Begin != null)
            {
                foreach (var e in elements)
                    item.Begin(e);
            }
            Debug.Log($"[OutlineUiBenchmark] {index + 1}/{_items.Count}: x{TierSizes[item.Tier]} · {item.Name}");

            float start = Time.realtimeSinceStartup;
            int count = 0;
            bool shotA = false;
            int skip = 0;
            while (true)
            {
                yield return null;
                float t = Time.realtimeSinceStartup - start;
                item.Tick?.Invoke(t);
                if (item.Motion)
                {
                    ApplyMotion(elements[0], t);
                    if (!shotA && t >= warmupSeconds + measureSeconds * 0.25f)
                    {
                        shotA = true;
                        yield return Capture(index, "a");
                        skip = 2; // кадры со снимком в замер не идут
                        continue;
                    }
                }
                if (skip > 0)
                    skip--;
                else if (t >= warmupSeconds && count < MaxSamples)
                    _frame[count++] = Time.unscaledDeltaTime * 1000f;
                if (t >= warmupSeconds + measureSeconds)
                    break;
            }
            item.Avg = Avg(_frame, count);
            item.P95 = P95(_frame, count);
            item.Samples = count;
            _items[index] = item;

            if (!item.IsBase)
                yield return Capture(index, null);

            foreach (var h in _handles)
                h.Hide();
            _handles.Clear();
            OutlineUi.DebugView = OutlineUiDebugView.None;
            ResetElements(elements);
            GC.Collect();
        }

        private static void ResetElements(List<VisualElement> elements)
        {
            foreach (var e in elements)
            {
                OutlineUiFx.Restore(e);
                e.style.translate = StyleKeyword.Null;
                e.style.scale = StyleKeyword.Null;
                e.style.rotate = StyleKeyword.Null;
            }
        }

        // влево-вправо ±120 пт за 4 с, лёгкий масштаб и наклон
        private static void ApplyMotion(VisualElement e, float t)
        {
            float ph = t / 4f * 2f * Mathf.PI;
            e.style.translate = new Translate(Mathf.Sin(ph) * 120f, 0f);
            float k = 1f + 0.15f * Mathf.Sin(ph * 2f);
            e.style.scale = new Scale(new Vector2(k, k));
            e.style.rotate = new Rotate(Mathf.Sin(ph * 0.5f) * 8f);
        }

        private IEnumerator Capture(int index, string suffix)
        {
            if (_shotDir == null)
                yield break;
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            try
            {
                var it = _items[index];
                string name = it.Name.Replace(' ', '_').Replace(":", "").Replace(",", "").Replace('↔', '-');
                string file = suffix == null
                    ? $"U{index:000}_x{TierSizes[it.Tier]}_{name}.png"
                    : $"U{index:000}_x{TierSizes[it.Tier]}_{name}_{suffix}.png";
                File.WriteAllBytes(Path.Combine(_shotDir, file), tex.EncodeToPNG());
                _shots++;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[OutlineUiBenchmark] Не удалось сохранить снимок: {e.Message}");
                _shotDir = null;
            }
            finally
            {
                Destroy(tex);
            }
        }

        // ------------------------------------------------------------------ Лог

        private float Baseline(int tier, bool motionStage)
        {
            float sum = 0f;
            int n = 0;
            foreach (var it in _items)
            {
                if (it.Tier == tier && it.Motion == motionStage && it.IsBase && it.Samples > 0)
                {
                    sum += it.Avg;
                    n++;
                }
            }
            return n > 0 ? sum / n : -1f;
        }

        private void WriteLog(bool final)
        {
            var inv = CultureInfo.InvariantCulture;
            _log.Clear();
            _log.Append("# Outline UI benchmark ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", inv)).Append('\n');
            _log.Append("# gpu: ").Append(SystemInfo.graphicsDeviceName).Append(" | ").Append(SystemInfo.graphicsDeviceType)
                .Append(" | cpu: ").Append(SystemInfo.processorType).Append('\n');
            _log.Append("# unity ").Append(Application.unityVersion).Append(" | screen ").Append(Screen.width).Append('x')
                .Append(Screen.height).Append(" | ").Append(warmupSeconds.ToString(inv)).Append(" с прогрев + ")
                .Append(measureSeconds.ToString(inv)).Append(" с замер\n");
            _log.Append("# колонки: кадр avg/p95 мс | прирост к базе своего яруса, мс | n\n");

            int lastSection = -1;
            for (int i = 0; i < _items.Count; i++)
            {
                var it = _items[i];
                if (it.Samples == 0)
                    continue;
                int section = it.Motion ? 100 : it.Tier;
                if (section != lastSection)
                {
                    lastSection = section;
                    _log.Append(it.Motion ? "## движение: 1 элемент, ±120 пт за 4 с, масштаб и наклон\n"
                        : $"## ярус: {TierSizes[it.Tier]} элем.\n");
                }
                float b = Baseline(it.Tier, it.Motion);
                _log.Append("[U").Append(i.ToString("000", inv)).Append("] ").Append(it.Name.PadRight(30)).Append(" | ")
                    .Append(it.Avg.ToString("0.00", inv)).Append('/').Append(it.P95.ToString("0.00", inv));
                if (!it.IsBase && b > 0f)
                    _log.Append(" | ").Append((it.Avg - b).ToString("+0.00;-0.00", inv));
                else
                    _log.Append(" | —");
                _log.Append(" | n=").Append(it.Samples).Append('\n');
            }

            // сводка: стиль × ярус
            _log.Append("#\n# Сводка: прирост к кадру, мс, по ярусам (элементов: 1 10 50)\n");
            var seen = new HashSet<string>();
            foreach (var it in _items)
            {
                if (it.IsBase || it.Motion || it.Diag || !seen.Add(it.Name))
                    continue;
                _log.Append("  ").Append(it.Name.PadRight(30));
                for (int t = 0; t < TierSizes.Length; t++)
                {
                    string cell = "—";
                    float b = Baseline(t, false);
                    foreach (var x in _items)
                    {
                        if (x.Tier == t && x.Name == it.Name && !x.Motion && x.Samples > 0 && b > 0f)
                        {
                            cell = (x.Avg - b).ToString("+0.00;-0.00", inv);
                            break;
                        }
                    }
                    _log.Append(" | ").Append(cell.PadLeft(6));
                }
                _log.Append('\n');
            }

            if (_items.Count > 0 && _items[0].Samples > 0 && _items[_items.Count - 1].Samples > 0)
                _log.Append("# дрейф базы: ").Append(_items[0].Avg.ToString("0.00", inv)).Append(" → ")
                    .Append(_items[_items.Count - 1].Avg.ToString("0.00", inv)).Append(" мс\n");
            if (_shotDir != null)
                _log.Append("# снимки: ").Append(_shots).Append(" в ").Append(_shotDir).Append('\n');
            if (final)
                _log.Append("# конец: готово ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", inv)).Append('\n');

            if (_path == null)
                return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                File.WriteAllText(_path, _log.ToString(), Encoding.UTF8);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[OutlineUiBenchmark] Не удалось записать лог {_path}: {e.Message}");
                _path = null;
            }
        }

        // ------------------------------------------------------------------ Утилиты

        private static string ShortName(OutlineUiStyle s)
        {
            string n = s.name;
            int i = n.LastIndexOf('_');
            return i >= 0 && i + 1 < n.Length ? n.Substring(i + 1) : n;
        }

        private static string ResolvePath(string file)
        {
            if (Application.platform == RuntimePlatform.WebGLPlayer)
                return null;
            return Application.isEditor
                ? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", file))
                : Path.Combine(Application.persistentDataPath, file);
        }

        private static string PrepareShotDir()
        {
            if (Application.platform == RuntimePlatform.WebGLPlayer)
                return null;
            string dir = Application.isEditor
                ? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "OutlineUiShots"))
                : Path.Combine(Application.persistentDataPath, "OutlineUiShots");
            try
            {
                Directory.CreateDirectory(dir);
                foreach (var f in Directory.GetFiles(dir, "*.png"))
                    File.Delete(f);
                return dir;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[OutlineUiBenchmark] Папка снимков недоступна: {e.Message}");
                return null;
            }
        }

        private static float Avg(float[] a, int n)
        {
            if (n <= 0)
                return 0f;
            double sum = 0;
            for (int i = 0; i < n; i++)
                sum += a[i];
            return (float)(sum / n);
        }

        private float P95(float[] a, int n)
        {
            if (n <= 0)
                return 0f;
            Array.Copy(a, _sort, n);
            Array.Sort(_sort, 0, n);
            return _sort[Mathf.Clamp((int)(n * 0.95f), 0, n - 1)];
        }

        private static void StopPlay()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            if (Application.platform != RuntimePlatform.WebGLPlayer)
                Application.Quit();
#endif
        }
    }
}
