using System;
using System.IO;
using System.Text;
using Exerussus.Outline.Rendering;
using UnityEngine;

namespace Exerussus.Outline.Lab
{
    /// <summary>
    /// Бенчмарк подсветки: по очереди включает режимы фичи (разрешение поля, кроп, scissor, мягкий стык,
    /// JFA+1, наложение на стыке, авто-разрешение, фича выключена), в каждом ждёт прогрев и копит
    /// покадровые замеры, пишет строку в консоль и ПЕРЕЗАПИСЫВАЕТ весь лог после каждого режима:
    /// в редакторе — &lt;проект&gt;/Temp/outline.log, в плеере — persistentDataPath/outline.log
    /// (WebGL — только консоль). Настройки фичи сохраняются в начале и возвращаются в конце.
    /// Буферы замеров выделяются один раз; строки собираются только на границах режимов.
    /// </summary>
    public sealed class OutlineBenchmark : MonoBehaviour
    {
        [SerializeField] private OutlineRendererFeature feature;
        [SerializeField] private bool runOnStart = true;
        [SerializeField, Min(0.5f), Tooltip("Длительность замера одного режима, с.")]
        private float modeSeconds = 4f;
        [SerializeField, Min(0f), Tooltip("Прогрев перед замером режима, с (кадры не учитываются).")]
        private float warmupSeconds = 1f;
        [SerializeField, Tooltip("На время бенчмарка выключить VSync и лимит кадров.")]
        private bool unlockFrameRate = true;
        [SerializeField, Tooltip("По окончании прогона выйти из Play (в плеере — закрыть приложение, кроме WebGL).")]
        private bool stopPlayOnFinish = true;

        private const int MaxSamples = 20000;

        private struct Mode
        {
            public string Name;
            public bool FeatureOn;
            public float Scale;
            public bool Auto;
            public bool Crop;
            public bool Scissor;
            public float Blend;
            public bool Extra;
            public bool Overlay;
        }

        private static readonly Mode[] Modes =
        {
            M("off (фича выключена)", on: false),
            M("base ×1"),
            M("×0.75", scale: 0.75f),
            M("×0.5", scale: 0.5f),
            M("×0.375", scale: 0.375f),
            M("×0.25", scale: 0.25f),
            M("×1 без кропа", crop: false),
            M("×1 кроп без scissor", scissor: false),
            M("×1 без мягкого стыка", blend: 0f),
            M("×1 без JFA+1", extra: false),
            M("×1 без наложения на стыке", overlay: false),
            M("дешёвый: ×0.5, без стыка, без JFA+1", scale: 0.5f, blend: 0f, extra: false),
            M("авто (бюджет 1.5 мс)", auto: true),
        };

        private readonly float[] _gpu = new float[MaxSamples];
        private readonly float[] _mask = new float[MaxSamples];
        private readonly float[] _jfa = new float[MaxSamples];
        private readonly float[] _comp = new float[MaxSamples];
        private readonly float[] _cpu = new float[MaxSamples];
        private readonly float[] _frame = new float[MaxSamples];
        private readonly float[] _sortBuf = new float[MaxSamples];
        private readonly StringBuilder _log = new(8192);

        private bool _running;
        private int _modeIndex;
        private float _modeStart;
        private int _count;
        private OutlineStats _lastStats;
        private float _lastScale;
        private string _savedSettings;
        private int _savedVSync;
        private int _savedTargetFps;
        private string _path;
        private bool _savedActive = true;

        public bool IsRunning => _running;

        /// <summary>Подпись текущего режима для HUD (null — не идёт).</summary>
        public string Status => _running ? $"бенчмарк {_modeIndex + 1}/{Modes.Length}: {Modes[_modeIndex].Name}" : null;

        private static Mode M(string name, bool on = true, float scale = 1f, bool auto = false, bool crop = true,
            bool scissor = true, float blend = 6f, bool extra = true, bool overlay = true) => new()
        {
            Name = name, FeatureOn = on, Scale = scale, Auto = auto, Crop = crop, Scissor = scissor,
            Blend = blend, Extra = extra, Overlay = overlay,
        };

        private void Start()
        {
            if (runOnStart)
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

            _path = ResolvePath();
            _log.Clear();
            WriteHeader();
            _running = true;
            StartMode(0);
        }

        private void Update()
        {
            if (!_running)
                return;

            float t = Time.realtimeSinceStartup - _modeStart;
            if (t >= warmupSeconds && _count < MaxSamples)
            {
                // при выключенной фиче Stats не обновляются — GPU-время считаем нулём
                var s = Modes[_modeIndex].FeatureOn ? feature.Stats : default;
                int i = _count++;
                _gpu[i] = s.TotalGpuMs;
                _mask[i] = s.MaskGpuMs;
                _jfa[i] = s.JfaGpuMs;
                _comp[i] = s.CompositeGpuMs;
                _cpu[i] = s.CpuMs;
                _frame[i] = Time.unscaledDeltaTime * 1000f;
                if (s.Rendered)
                {
                    _lastStats = s;
                    _lastScale = s.FieldScale;
                }
            }

            if (t >= warmupSeconds + modeSeconds)
            {
                AppendModeResult();
                Flush();
                if (_modeIndex + 1 < Modes.Length)
                    StartMode(_modeIndex + 1);
                else
                {
                    Finish("готово");
                    if (stopPlayOnFinish)
                        StopPlay();
                }
            }
        }

        private void StartMode(int index)
        {
            _modeIndex = index;
            _count = 0;
            _lastStats = default;
            _lastScale = 0f;
            _modeStart = Time.realtimeSinceStartup;

            // исходные настройки + поправки режима
            JsonUtility.FromJsonOverwrite(_savedSettings, feature.settings);
            var m = Modes[index];
            var st = feature.settings;
            st.debugView = OutlineDebugView.None;
            st.fieldScale = m.Scale;
            st.fieldScaleWebGL = m.Scale;
            st.autoFieldScale = m.Auto;
            st.cropToBounds = m.Crop;
            st.scissor = m.Scissor;
            st.seamBlend = m.Blend;
            st.extraPass = m.Extra;
            st.extraPassWebGL = m.Extra;
            st.seamOverlay = m.Overlay;
            feature.SetActive(m.FeatureOn);

            Debug.Log($"[OutlineBenchmark] режим {index + 1}/{Modes.Length}: {m.Name}");
        }

        private void Finish(string reason)
        {
            _running = false;
            if (feature != null)
            {
                if (_savedSettings != null)
                    JsonUtility.FromJsonOverwrite(_savedSettings, feature.settings);
                feature.SetActive(_savedActive);
            }
            QualitySettings.vSyncCount = _savedVSync;
            Application.targetFrameRate = _savedTargetFps;

            _log.Append("# конец: ").Append(reason).Append(' ').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append('\n');
            Flush();
            Debug.Log($"[OutlineBenchmark] {reason}. Лог: {(_path ?? "только консоль")}");
        }

        private void WriteHeader()
        {
            _log.Append("# Outline benchmark ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append('\n');
            _log.Append("# gpu: ").Append(SystemInfo.graphicsDeviceName).Append(" | ").Append(SystemInfo.graphicsDeviceType)
                .Append(" | ").Append(SystemInfo.graphicsDeviceVersion).Append('\n');
            _log.Append("# cpu: ").Append(SystemInfo.processorType).Append(" | ram ").Append(SystemInfo.systemMemorySize).Append(" MB\n");
            _log.Append("# unity ").Append(Application.unityVersion).Append(" | ").Append(Application.platform)
                .Append(" | editor ").Append(Application.isEditor)
                .Append(" | screen ").Append(Screen.width).Append('x').Append(Screen.height)
                .Append(" | gpuRecorder ").Append(SystemInfo.supportsGpuRecorder).Append('\n');
#if UNITY_EDITOR
            _log.Append("# scene views open: ").Append(UnityEditor.SceneView.sceneViews.Count)
                .Append(" (GPU-время суммируется по всем камерам)\n");
#endif
            _log.Append("# записей подсветки: ").Append(OutlineApi.AliveCount)
                .Append(" | режим: ").Append(warmupSeconds).Append("с прогрев + ").Append(modeSeconds).Append("с замер\n");
            _log.Append("# колонки: GPU всего avg/p95 | маска | JFA | композит (avg, мс) | кадр avg/p95 мс (fps) | CPU записи | поле, масштаб, кроп, проходы\n");
        }

        private void AppendModeResult()
        {
            var m = Modes[_modeIndex];
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            int n = _count;
            float frameAvg = Avg(_frame, n);
            _log.Append('[').Append((_modeIndex + 1).ToString("00")).Append("] ").Append(m.Name.PadRight(38)).Append(" | ");
            _log.Append("GPU ").Append(Avg(_gpu, n).ToString("0.000", inv)).Append('/').Append(P95(_gpu, n).ToString("0.000", inv));
            _log.Append(" | mask ").Append(Avg(_mask, n).ToString("0.000", inv));
            _log.Append(" | jfa ").Append(Avg(_jfa, n).ToString("0.000", inv));
            _log.Append(" | comp ").Append(Avg(_comp, n).ToString("0.000", inv));
            _log.Append(" | frame ").Append(frameAvg.ToString("0.00", inv)).Append('/').Append(P95(_frame, n).ToString("0.00", inv))
                .Append(" (").Append((frameAvg > 0f ? 1000f / frameAvg : 0f).ToString("0", inv)).Append(" fps)");
            _log.Append(" | cpu ").Append(Avg(_cpu, n).ToString("0.000", inv));
            if (_lastStats.Rendered)
            {
                _log.Append(" | field ").Append(_lastStats.FieldWidth).Append('x').Append(_lastStats.FieldHeight)
                    .Append(" x").Append(_lastScale.ToString("0.###", inv))
                    .Append(_lastStats.DualField ? " +inner" : "")
                    .Append(" cov ").Append((_lastStats.Coverage * 100f).ToString("0", inv)).Append('%')
                    .Append(" passes ").Append(_lastStats.JfaPasses)
                    .Append(" entries ").Append(_lastStats.ActiveEntries);
            }
            else
            {
                _log.Append(" | не рисовалось");
            }
            _log.Append(" | n=").Append(n).Append('\n');
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
