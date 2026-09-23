using System;
using System.Collections;
using System.IO;
using System.Text;
using Exerussus.Outline.Rendering;
using UnityEngine;

namespace Exerussus.Outline.Lab
{
    /// <summary>
    /// Бенчмарк подсветки: по очереди включает режимы фичи, в каждом ждёт прогрев и копит время кадра.
    /// Первый и последний режим — фича выключена; прирост считается к их среднему, разница между ними
    /// показывает дрейф. Лог перезаписывается после каждого режима: в редакторе — &lt;проект&gt;/Temp/outline.log,
    /// в билде — persistentDataPath/outline.log, в WebGL — только консоль (весь лог выводится в конце).
    /// Настройки фичи сохраняются в начале и возвращаются в конце.
    /// Снимки: после замера каждого режима с фичей и в визуальных режимах (без замера: качество края, отладочные
    /// виды) кадр сохраняется в PNG — в редакторе &lt;проект&gt;/Temp/OutlineShots, в билде persistentDataPath/OutlineShots.
    /// На время снимка HUD скрывается.
    /// </summary>
    public sealed partial class OutlineBenchmark : MonoBehaviour
    {
        [SerializeField] private OutlineRendererFeature feature;
        [SerializeField] private bool runOnStart = true;
        [SerializeField, Min(0.5f), Tooltip("Длительность замера одного режима, с.")]
        private float modeSeconds = 4f;
        [SerializeField, Min(0f), Tooltip("Прогрев перед замером режима, с (кадры не учитываются).")]
        private float warmupSeconds = 1f;
        [SerializeField, Min(0f), Tooltip("Пауза после входа в Play до начала прогона, с.")]
        private float startDelaySeconds = 3f;
        [SerializeField, Tooltip("Камера сцены: для визуальных режимов с другой точкой обзора.")]
        private Camera viewCamera;
        [SerializeField, Tooltip("Точка обзора второго ряда витрины (эффекты).")]
        private Transform effectsView;
        [SerializeField, Tooltip("На время бенчмарка выключить VSync и лимит кадров.")]
        private bool unlockFrameRate = true;
        [SerializeField, Tooltip("По окончании прогона выйти из Play (в билде — закрыть приложение, кроме WebGL).")]
        private bool stopPlayOnFinish = true;
        [SerializeField, Tooltip("Сохранять снимок кадра после каждого режима и прогонять визуальные режимы.")]
        private bool captureScreenshots = true;
        [SerializeField, Min(0.1f), Tooltip("Ожидание перед снимком в визуальном режиме, с.")]
        private float visualSettleSeconds = 0.5f;

        private const int MaxSamples = 20000;

        private enum ScaleMode { Project, Auto, Fixed }

        private struct Mode
        {
            public string Name;
            public bool FeatureOn;
            public ScaleMode Scale;
            public float FixedScale;
            public bool? Extra;
            public float? Blend;
            public bool? Crop;
            public bool? Scissor;
            public OutlineEdgeAA? EdgeAA;
            public OutlineDebugView View;
            /// <summary>Без замера — только снимок.</summary>
            public bool Visual;
            /// <summary>Снимать с точки обзора эффектов.</summary>
            public bool EffectsView;
            /// <summary>Имя файла снимка (латиница).</summary>
            public string Shot;
        }

        private static readonly Mode[] Modes =
        {
            new() { Name = "off", Shot = "off" },
            new() { Name = "настройки проекта", FeatureOn = true, Scale = ScaleMode.Project, Shot = "project" },
            new() { Name = "авто", FeatureOn = true, Scale = ScaleMode.Auto, Shot = "auto" },
            new() { Name = "×1", FeatureOn = true, Scale = ScaleMode.Fixed, FixedScale = 1f, Shot = "x1" },
            new() { Name = "×0.75", FeatureOn = true, Scale = ScaleMode.Fixed, FixedScale = 0.75f, Shot = "x075" },
            new() { Name = "×0.5", FeatureOn = true, Scale = ScaleMode.Fixed, FixedScale = 0.5f, Shot = "x05" },
            new() { Name = "×0.25", FeatureOn = true, Scale = ScaleMode.Fixed, FixedScale = 0.25f, Shot = "x025" },
            new() { Name = "×1 + JFA+1", FeatureOn = true, Scale = ScaleMode.Fixed, FixedScale = 1f, Extra = true, Shot = "x1_jfa1" },
            new() { Name = "авто без мягкого стыка", FeatureOn = true, Scale = ScaleMode.Auto, Blend = 0f, Shot = "auto_noblend" },
            new() { Name = "авто без scissor", FeatureOn = true, Scale = ScaleMode.Auto, Scissor = false, Shot = "auto_noscissor" },
            new() { Name = "авто без кропа", FeatureOn = true, Scale = ScaleMode.Auto, Crop = false, Shot = "auto_nocrop" },
            new() { Name = "авто без сглаживания", FeatureOn = true, Scale = ScaleMode.Auto, EdgeAA = OutlineEdgeAA.Off, Shot = "auto_aa1" },
            new() { Name = "авто, сглаживание ×2", FeatureOn = true, Scale = ScaleMode.Auto, EdgeAA = OutlineEdgeAA.X2, Shot = "auto_aa2" },
            new() { Name = "авто, сглаживание ×8", FeatureOn = true, Scale = ScaleMode.Auto, EdgeAA = OutlineEdgeAA.X8, Shot = "auto_aa8" },
            new() { Name = "off (повтор)", Shot = "off_repeat" },

            // визуальные режимы: без замера, только снимок
            new() { Name = "вид: ×1 без сглаживания", Visual = true, FeatureOn = true, Scale = ScaleMode.Fixed, FixedScale = 1f, EdgeAA = OutlineEdgeAA.Off, Shot = "v_x1_aa1" },
            new() { Name = "вид: ×1 сглаживание ×4", Visual = true, FeatureOn = true, Scale = ScaleMode.Fixed, FixedScale = 1f, EdgeAA = OutlineEdgeAA.X4, Shot = "v_x1_aa4" },
            new() { Name = "вид: ×1 сглаживание ×8", Visual = true, FeatureOn = true, Scale = ScaleMode.Fixed, FixedScale = 1f, EdgeAA = OutlineEdgeAA.X8, Shot = "v_x1_aa8" },
            new() { Name = "вид: маска ×4", Visual = true, FeatureOn = true, Scale = ScaleMode.Fixed, FixedScale = 1f, EdgeAA = OutlineEdgeAA.X4, View = OutlineDebugView.Mask, Shot = "v_mask" },
            new() { Name = "вид: сиды", Visual = true, FeatureOn = true, Scale = ScaleMode.Auto, View = OutlineDebugView.Seeds, Shot = "v_seeds" },
            new() { Name = "вид: поле", Visual = true, FeatureOn = true, Scale = ScaleMode.Auto, View = OutlineDebugView.Distance, Shot = "v_distance" },
            new() { Name = "вид: цвет объектов", Visual = true, FeatureOn = true, Scale = ScaleMode.Auto, View = OutlineDebugView.ObjectColor, Shot = "v_objcolor" },
        };

        private readonly float[] _frame = new float[MaxSamples];
        private readonly float[] _cpu = new float[MaxSamples];
        private readonly float[] _sortBuf = new float[MaxSamples];
        private readonly float[] _modeFrameAvg = new float[Modes.Length];
        private readonly float[] _modeFrameP95 = new float[Modes.Length];
        private readonly float[] _modeCpu = new float[Modes.Length];
        private readonly OutlineStats[] _modeStats = new OutlineStats[Modes.Length];
        private readonly int[] _modeSamples = new int[Modes.Length];
        private readonly StringBuilder _log = new(8192);

        private bool _running;
        private int _modeIndex;
        private float _modeStart;
        private int _count;
        private OutlineStats _lastStats;
        private int _minSamples;
        private int _maxSamples;
        private readonly int[] _modeAaMin = new int[Modes.Length];
        private readonly int[] _modeAaMax = new int[Modes.Length];
        private bool _capturing;
        private string _shotDir;
        private int _shotCount;
        private Vector3 _savedCamPos;
        private Quaternion _savedCamRot;
        private bool _camMoved;
        private string _savedSettings;
        private bool _savedActive = true;
        private int _savedVSync;
        private int _savedTargetFps;
        private string _path;

        public bool IsRunning => _running;

        /// <summary>Скрыть оверлеи: идёт визуальный режим, витрина или снимок.</summary>
        public bool HideOverlay => _running && (_capturing || _showcaseActive || Modes[_modeIndex].Visual);

        /// <summary>Подпись текущего режима для HUD (null — не идёт).</summary>
        public string Status => !_running ? null
            : _showcaseActive ? ShowcaseStatus
            : $"бенчмарк {_modeIndex + 1}/{Modes.Length}: {Modes[_modeIndex].Name}";

        private IEnumerator Start()
        {
            if (!runOnStart)
                yield break;
            // после входа в Play редактор ещё догружает ассеты и компилирует шейдеры — первый «off» выходил завышенным
            float until = Time.realtimeSinceStartup + startDelaySeconds;
            while (Time.realtimeSinceStartup < until)
                yield return null;
            Begin();
        }

        private void OnDisable()
        {
            if (_running)
                Finish("прервано");
        }

        /// <summary>Запустить прогон всех режимов (повторный вызов перезапускает).</summary>
        public void Begin()
        {
            if (feature == null)
            {
                Debug.LogWarning("[OutlineBenchmark] Не назначена фича — бенчмарк не запущен.", this);
                return;
            }
            if (_running)
                Finish("перезапуск");

            _savedSettings = JsonUtility.ToJson(feature.settings);
            _savedActive = feature.isActive;
            _savedVSync = QualitySettings.vSyncCount;
            _savedTargetFps = Application.targetFrameRate;
            if (unlockFrameRate)
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = -1;
            }

            Array.Clear(_modeSamples, 0, _modeSamples.Length);
            _path = ResolvePath();
            _shotDir = captureScreenshots ? PrepareShotDir() : null;
            _shotCount = 0;
            _showcaseRan = false;
            _items.Clear();
            _running = true;
            StartMode(0);
        }

        private void Update()
        {
            if (!_running || _capturing)
                return;
            if (_showcaseActive)
            {
                UpdateShowcase();
                return;
            }

            var mode = Modes[_modeIndex];
            float t = Time.realtimeSinceStartup - _modeStart;

            if (mode.Visual)
            {
                if (t >= visualSettleSeconds)
                    StartCoroutine(CaptureAndAdvance(_modeIndex));
                return;
            }

            if (t >= warmupSeconds && _count < MaxSamples)
            {
                int i = _count++;
                _frame[i] = Time.unscaledDeltaTime * 1000f;
                var s = mode.FeatureOn ? feature.Stats : default;
                _cpu[i] = s.CpuMs;
                if (s.Rendered)
                {
                    _lastStats = s;
                    _minSamples = Mathf.Min(_minSamples, s.EdgeSamples);
                    _maxSamples = Mathf.Max(_maxSamples, s.EdgeSamples);
                }
            }

            if (t < warmupSeconds + modeSeconds)
                return;

            int m = _modeIndex;
            _modeFrameAvg[m] = Avg(_frame, _count);
            _modeFrameP95[m] = P95(_frame, _count);
            _modeCpu[m] = Avg(_cpu, _count);
            _modeStats[m] = _lastStats;
            _modeAaMin[m] = _minSamples;
            _modeAaMax[m] = _maxSamples;
            _modeSamples[m] = _count;
            BuildLog(false);
            Flush();

            if (_shotDir != null && mode.FeatureOn)
                StartCoroutine(CaptureAndAdvance(m));
            else
                Advance();
        }

        private void Advance()
        {
            if (_modeIndex + 1 < Modes.Length)
            {
                StartMode(_modeIndex + 1);
                return;
            }

            // общая сцена замерена — дальше витрина стилей на одном объекте
            if (BeginShowcase())
                return;

            Finish("готово");
            if (stopPlayOnFinish)
                StopPlay();
        }

        /// <summary>Снимок кадра текущего режима (HUD скрыт на 2 кадра до снимка), затем следующий режим.</summary>
        private IEnumerator CaptureAndAdvance(int index)
        {
            _capturing = true;
            yield return null;
            yield return null;
            yield return new WaitForEndOfFrame();
            if (_running && _shotDir != null)
            {
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                try
                {
                    var file = Path.Combine(_shotDir, $"{index + 1:00}_{Modes[index].Shot}.png");
                    File.WriteAllBytes(file, tex.EncodeToPNG());
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
                // PNG-кодирование оставляет мегабайты мусора — собираем сейчас, а не посреди замера следующего режима
                GC.Collect();
            }
            _capturing = false;
            if (_running)
                Advance();
        }

        private void StartMode(int index)
        {
            _modeIndex = index;
            _count = 0;
            _lastStats = default;
            _minSamples = int.MaxValue;
            _maxSamples = 0;
            _modeStart = Time.realtimeSinceStartup;

            JsonUtility.FromJsonOverwrite(_savedSettings, feature.settings);
            var m = Modes[index];
            var st = feature.settings;
            st.debugView = m.View;
            switch (m.Scale)
            {
                case ScaleMode.Auto:
                    st.autoFieldScale = true;
                    break;
                case ScaleMode.Fixed:
                    st.autoFieldScale = false;
                    st.fieldScale = m.FixedScale;
                    st.fieldScaleWebGL = m.FixedScale;
                    break;
            }
            if (m.Extra.HasValue) { st.extraPass = m.Extra.Value; st.extraPassWebGL = m.Extra.Value; }
            if (m.Blend.HasValue) st.seamBlend = m.Blend.Value;
            if (m.Crop.HasValue) st.cropToBounds = m.Crop.Value;
            if (m.Scissor.HasValue) st.scissor = m.Scissor.Value;
            if (m.EdgeAA.HasValue) { st.edgeAntialiasing = m.EdgeAA.Value; st.edgeAntialiasingWebGL = m.EdgeAA.Value; }
            feature.SetActive(m.FeatureOn);
            ApplyView(m.EffectsView);

            Debug.Log($"[OutlineBenchmark] режим {index + 1}/{Modes.Length}: {m.Name}");
        }

        /// <summary>Переставить камеру на точку обзора эффектов или вернуть исходную.</summary>
        private void ApplyView(bool effects)
        {
            if (viewCamera == null || effectsView == null)
                return;
            var tr = viewCamera.transform;
            if (effects && !_camMoved)
            {
                _savedCamPos = tr.position;
                _savedCamRot = tr.rotation;
                tr.SetPositionAndRotation(effectsView.position, effectsView.rotation);
                _camMoved = true;
            }
            else if (!effects && _camMoved)
            {
                tr.SetPositionAndRotation(_savedCamPos, _savedCamRot);
                _camMoved = false;
            }
        }

        private void Finish(string reason)
        {
            _running = false;
            EndShowcase();
            ApplyView(false);
            if (feature != null)
            {
                if (_savedSettings != null)
                    JsonUtility.FromJsonOverwrite(_savedSettings, feature.settings);
                feature.SetActive(_savedActive);
            }
            QualitySettings.vSyncCount = _savedVSync;
            Application.targetFrameRate = _savedTargetFps;

            BuildLog(true);
            AppendShowcaseLog();
            if (_shotDir != null)
                _log.Append("# снимки: ").Append(_shotCount).Append(" в ").Append(_shotDir).Append('\n');
            _log.Append("# конец: ").Append(reason).Append(' ').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append('\n');
            Flush();
            Debug.Log($"[OutlineBenchmark] {reason}. Лог: {(_path ?? "консоль")}\n{_log}");
        }

        private void BuildLog(bool final)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            _log.Clear();
            _log.Append("# Outline benchmark ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append('\n');
            _log.Append("# gpu: ").Append(SystemInfo.graphicsDeviceName).Append(" | ").Append(SystemInfo.graphicsDeviceType)
                .Append(" | ").Append(SystemInfo.graphicsDeviceVersion).Append('\n');
            _log.Append("# cpu: ").Append(SystemInfo.processorType).Append(" | ram ").Append(SystemInfo.systemMemorySize).Append(" MB\n");
            _log.Append("# unity ").Append(Application.unityVersion).Append(" | ").Append(Application.platform)
                .Append(" | editor ").Append(Application.isEditor)
                .Append(" | screen ").Append(Screen.width).Append('x').Append(Screen.height).Append('\n');
            _log.Append("# записей подсветки: ").Append(OutlineApi.AliveCount)
                .Append(" | режим: ").Append(warmupSeconds.ToString(inv)).Append(" с прогрев + ")
                .Append(modeSeconds.ToString(inv)).Append(" с замер\n");

            // база — среднее по замеренным режимам «off»
            float baseSum = 0f;
            int baseCount = 0;
            for (int i = 0; i < Modes.Length; i++)
            {
                if (!Modes[i].FeatureOn && _modeSamples[i] > 0)
                {
                    baseSum += _modeFrameAvg[i];
                    baseCount++;
                }
            }
            float baseline = baseCount > 0 ? baseSum / baseCount : 0f;

            _log.Append("# колонки: кадр avg/p95 мс (fps) | прирост к off, мс | CPU записи, мс | масштаб, поле, кроп, проходы, стоимость/бюджет\n");
            for (int i = 0; i < Modes.Length; i++)
            {
                if (_modeSamples[i] == 0)
                    continue;
                var m = Modes[i];
                float avg = _modeFrameAvg[i];
                _log.Append('[').Append((i + 1).ToString("00")).Append("] ").Append(m.Name.PadRight(24)).Append(" | ");
                _log.Append(avg.ToString("0.00", inv)).Append('/').Append(_modeFrameP95[i].ToString("0.00", inv))
                    .Append(" (").Append((avg > 0f ? 1000f / avg : 0f).ToString("0", inv)).Append(" fps)");
                if (m.FeatureOn && baseCount > 0)
                    _log.Append(" | +").Append((avg - baseline).ToString("0.00", inv));
                else
                    _log.Append(" | —");
                if (m.FeatureOn)
                    _log.Append(" | cpu ").Append(_modeCpu[i].ToString("0.000", inv));
                var s = _modeStats[i];
                if (s.Rendered)
                {
                    _log.Append(" | x").Append(s.FieldScale.ToString("0.###", inv))
                        .Append(' ').Append(s.FieldWidth).Append('x').Append(s.FieldHeight)
                        .Append(s.DualField ? " +inner" : "")
                        .Append(" cov ").Append((s.Coverage * 100f).ToString("0", inv)).Append('%')
                        .Append(" passes ").Append(s.JfaPasses)
                        .Append(" cost ").Append(s.FieldCost).Append('/').Append(s.FieldBudget)
                        .Append(" aa ").Append(_modeAaMin[i] == _modeAaMax[i]
                            ? _modeAaMax[i].ToString(inv)
                            : $"{_modeAaMin[i]}–{_modeAaMax[i]}");
                }
                _log.Append(" | n=").Append(_modeSamples[i]).Append('\n');
            }

            if (final && baseCount >= 2)
            {
                int firstOff = -1, lastOff = -1;
                for (int i = 0; i < Modes.Length; i++)
                {
                    if (Modes[i].FeatureOn || Modes[i].Visual || _modeSamples[i] == 0)
                        continue;
                    if (firstOff < 0)
                        firstOff = i;
                    lastOff = i;
                }
                float first = _modeFrameAvg[firstOff];
                float last = _modeFrameAvg[lastOff];
                _log.Append("# дрейф off: ").Append(first.ToString("0.00", inv)).Append(" → ")
                    .Append(last.ToString("0.00", inv)).Append(" мс\n");
            }
        }

        private void Flush()
        {
            if (_path == null)
                return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                File.WriteAllText(_path, _log.ToString(), Encoding.UTF8);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[OutlineBenchmark] Не удалось записать лог {_path}: {e.Message}");
                _path = null;
            }
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

        /// <summary>Папка снимков рядом с логом; старые PNG удаляются. null — снимки недоступны (WebGL).</summary>
        private static string PrepareShotDir()
        {
            if (Application.platform == RuntimePlatform.WebGLPlayer)
                return null;
            string dir = Application.isEditor
                ? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "OutlineShots"))
                : Path.Combine(Application.persistentDataPath, "OutlineShots");
            try
            {
                Directory.CreateDirectory(dir);
                foreach (var f in Directory.GetFiles(dir, "*.png"))
                    File.Delete(f);
                return dir;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[OutlineBenchmark] Папка снимков недоступна: {e.Message}");
                return null;
            }
        }

        private static string ResolvePath()
        {
            if (Application.platform == RuntimePlatform.WebGLPlayer)
                return null;
            if (Application.isEditor)
                return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "outline.log"));
            return Path.Combine(Application.persistentDataPath, "outline.log");
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
            Array.Copy(a, _sortBuf, n);
            Array.Sort(_sortBuf, 0, n);
            return _sortBuf[Mathf.Min(n - 1, (int)(n * 0.95f))];
        }
    }
}
