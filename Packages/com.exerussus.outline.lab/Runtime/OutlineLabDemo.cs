using System.Collections.Generic;
using Exerussus.Outline.Rendering;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Exerussus.Outline.Lab
{
    /// <summary>
    /// Демо площадки: ховер мышью — стиль hover (с fade), ЛКМ — выделить/снять (стиль selected, группа 1),
    /// ПКМ — «враг» (стиль enemy, группа 2), Backspace — снять всё, 0..3 — отладочный вид фичи,
    /// P — перебор паттернов стиля выделения, C — кроп поля вкл/выкл. Всё изменённое в ассетах возвращается при выходе.
    /// Цель клика — корень объекта с коллайдером (ищется OutlineLabPickable, иначе сам коллайдер).
    /// </summary>
    public sealed class OutlineLabDemo : MonoBehaviour
    {
        [SerializeField] private Camera targetCamera;
        [SerializeField] private OutlineStyle hoverStyle;
        [SerializeField] private OutlineStyle selectedStyle;
        [SerializeField] private OutlineStyle enemyStyle;
        [SerializeField, Tooltip("Для переключения отладочного вида клавишами 0..3 (меняет ассет рендерера!).")]
        private OutlineRendererFeature feature;
        [SerializeField, Min(0f)] private float hoverFade = 0.12f;
        [SerializeField] private LayerMask pickMask = Physics.DefaultRaycastLayers;

        private readonly Dictionary<GameObject, OutlineHandle> _selected = new(32);
        private readonly List<GameObject> _scratch = new(32);
        private GameObject _hovered;
        private OutlineDebugView _initialDebugView;
        private bool _initialCrop;
        private OutlinePatternType _initialPattern;
        private OutlinePatternLayers _initialPatternLayers;
        private OutlineHandle _hoverHandle;
        private OutlineEdgeAA _initialEdgeAA;
        [SerializeField, Tooltip("Точка обзора второго ряда витрины (клавиша V).")]
        private Transform effectsView;
        private Vector3 _homePos;
        private Quaternion _homeRot;
        private bool _atEffects;
        private OutlinePatternSpace _initialPatternSpace;

        private void Awake()
        {
            if (targetCamera == null)
                targetCamera = Camera.main;
            if (hoverStyle == null || selectedStyle == null || enemyStyle == null)
                Debug.LogWarning("[OutlineLabDemo] Не назначены стили (hover/selected/enemy) — подсветка по мыши работать не будет.", this);
        }

        private void OnEnable()
        {
            if (feature != null)
            {
                _initialDebugView = feature.settings.debugView;
                _initialCrop = feature.settings.cropToBounds;
                _initialEdgeAA = feature.settings.edgeAntialiasing;
            }
            if (selectedStyle != null)
            {
                _initialPattern = selectedStyle.pattern;
                _initialPatternSpace = selectedStyle.patternSpace;
                _initialPatternLayers = selectedStyle.patternLayers;
            }
        }

        private void OnDisable()
        {
            // отладочный вид живёт в ассете рендерера — возвращаем, чтобы не залипал после Play
            if (feature != null)
            {
                feature.settings.debugView = _initialDebugView;
                feature.settings.cropToBounds = _initialCrop;
                feature.settings.edgeAntialiasing = _initialEdgeAA;
            }
            if (selectedStyle != null)
            {
                selectedStyle.pattern = _initialPattern;
                selectedStyle.patternSpace = _initialPatternSpace;
                selectedStyle.patternLayers = _initialPatternLayers;
            }

            _hoverHandle.Hide();
            _hovered = null;
            foreach (var pair in _selected)
                pair.Value.Hide();
            _selected.Clear();
        }

        private void Update()
        {
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            if (mouse == null || targetCamera == null)
                return;

            var picked = Pick(mouse.position.ReadValue());
            UpdateHover(picked);

            if (picked != null && mouse.leftButton.wasPressedThisFrame)
                Toggle(picked, selectedStyle, 1);
            if (picked != null && mouse.rightButton.wasPressedThisFrame)
                Toggle(picked, enemyStyle, 2);

            if (keyboard == null)
                return;
            if (keyboard.backspaceKey.wasPressedThisFrame)
                ClearSelection();

            // временные эффекты на объект под курсором
            if (picked != null)
            {
                if (keyboard.xKey.wasPressedThisFrame)
                {
                    if (OutlineFx.IsDissolved(picked))
                        OutlineFx.DissolveIn(picked, 0.8f);
                    else
                        OutlineFx.DissolveOut(picked, 0.8f);
                }
                if (keyboard.gKey.wasPressedThisFrame)
                    OutlineFx.Pulse(picked);
                if (keyboard.fKey.wasPressedThisFrame)
                    OutlineFx.Flash(picked, new Color(2.5f, 2.5f, 2.5f, 0.8f));
            }
            if (feature != null)
            {
                if (keyboard.digit0Key.wasPressedThisFrame) feature.settings.debugView = OutlineDebugView.None;
                if (keyboard.digit1Key.wasPressedThisFrame) feature.settings.debugView = OutlineDebugView.Mask;
                if (keyboard.digit2Key.wasPressedThisFrame) feature.settings.debugView = OutlineDebugView.Seeds;
                if (keyboard.digit3Key.wasPressedThisFrame) feature.settings.debugView = OutlineDebugView.Distance;
                if (keyboard.cKey.wasPressedThisFrame) feature.settings.cropToBounds = !feature.settings.cropToBounds;
                if (keyboard.vKey.wasPressedThisFrame)
                    ToggleView();
                if (keyboard.aKey.wasPressedThisFrame) feature.settings.edgeAntialiasing = NextEdgeAA(feature.settings.edgeAntialiasing);
            }
            if (selectedStyle != null && keyboard.pKey.wasPressedThisFrame)
            {
                // перебор паттернов выделения (на время Play; при выходе вернётся исходный)
                int next = ((int)selectedStyle.pattern + 1) % 9;
                selectedStyle.pattern = (OutlinePatternType)next;
                selectedStyle.patternLayers = OutlinePatternLayers.All;
            }
            if (selectedStyle != null && keyboard.oKey.wasPressedThisFrame)
            {
                // перебор пространства паттерна выделения: экран → объект → поверхность объекта → поверхность мира
                selectedStyle.patternSpace = (OutlinePatternSpace)(((int)selectedStyle.patternSpace + 1) % 4);
                if (selectedStyle.pattern == OutlinePatternType.None)
                {
                    selectedStyle.pattern = OutlinePatternType.Stripes;
                    selectedStyle.patternLayers = OutlinePatternLayers.All;
                }
                Debug.Log($"[OutlineLabDemo] пространство паттерна: {selectedStyle.patternSpace}");
            }
        }

        private GameObject Pick(Vector2 screen)
        {
            var ray = targetCamera.ScreenPointToRay(screen);
            if (!Physics.Raycast(ray, out var hit, 1000f, pickMask, QueryTriggerInteraction.Ignore))
                return null;
            var pickable = hit.collider.GetComponentInParent<OutlineLabPickable>();
            return pickable != null ? pickable.gameObject : hit.collider.gameObject;
        }

        private void UpdateHover(GameObject picked)
        {
            if (picked == _hovered)
                return;

            _hoverHandle.FadeOutAndHide(hoverFade);
            _hoverHandle = OutlineHandle.Invalid;
            _hovered = picked;

            // выделенное не ховерим повторно — у него уже есть подсветка
            if (picked == null || hoverStyle == null || _selected.ContainsKey(picked))
                return;
            _hoverHandle = OutlineApi.Show(picked, hoverStyle, OutlineOptions.InGroup(0, hoverFade));
        }

        private void Toggle(GameObject go, OutlineStyle style, int group)
        {
            if (style == null)
                return;
            if (_selected.TryGetValue(go, out var handle))
            {
                handle.FadeOutAndHide(hoverFade);
                _selected.Remove(go);
                return;
            }

            _hoverHandle.Hide();
            _hoverHandle = OutlineHandle.Invalid;
            _selected.Add(go, OutlineApi.Show(go, style, OutlineOptions.InGroup(group, hoverFade)));
        }

        private void ClearSelection()
        {
            _scratch.Clear();
            foreach (var pair in _selected)
            {
                pair.Value.FadeOutAndHide(hoverFade);
                _scratch.Add(pair.Key);
            }
            for (int i = 0; i < _scratch.Count; i++)
                _selected.Remove(_scratch[i]);
            _scratch.Clear();
        }

        private void ToggleView()
        {
            if (effectsView == null || targetCamera == null)
                return;
            var tr = targetCamera.transform;
            if (!_atEffects)
            {
                _homePos = tr.position;
                _homeRot = tr.rotation;
                tr.SetPositionAndRotation(effectsView.position, effectsView.rotation);
            }
            else
            {
                tr.SetPositionAndRotation(_homePos, _homeRot);
            }
            _atEffects = !_atEffects;
        }

        private static OutlineEdgeAA NextEdgeAA(OutlineEdgeAA v) => v switch
        {
            OutlineEdgeAA.Off => OutlineEdgeAA.X2,
            OutlineEdgeAA.X2 => OutlineEdgeAA.X4,
            OutlineEdgeAA.X4 => OutlineEdgeAA.X8,
            _ => OutlineEdgeAA.Off,
        };
    }
}
