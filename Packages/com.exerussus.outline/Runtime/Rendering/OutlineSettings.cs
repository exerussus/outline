using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Exerussus.Outline.Rendering
{
    /// <summary>Настройки фичи (общие для всех стилей) + тиры Desktop/WebGL.</summary>
    [Serializable]
    public sealed class OutlineSettings
    {
        [Tooltip("Когда композитить. До пост-обработки — HDR-цвет контура подхватывает Bloom.")]
        public RenderPassEvent passEvent = RenderPassEvent.BeforeRenderingPostProcessing;

        [Header("Качество (Desktop)")]
        [Range(0.25f, 1f), Tooltip("Разрешение поля расстояний относительно кадра. При авто — верхняя граница.")]
        public float fieldScale = 1f;
        [Tooltip("Дополнительный проход JFA с шагом 1. Чуть чище на стыках, ~20% стоимости поля.")]
        public bool extraPass = false;
        [Tooltip("Авто: брать наибольший масштаб поля, при котором стоимость (пиксели поля × проходы) укладывается в бюджет.")]
        public bool autoFieldScale = true;
        [Min(1000), Tooltip("Бюджет поля: пиксели поля × проходы JFA за кадр.")]
        public int fieldBudget = 450000;
        [Tooltip("Сглаживание края силуэта: число сэмплов маски. Край и заливка перекрытой части становятся гладкими.")]
        public OutlineEdgeAA edgeAntialiasing = OutlineEdgeAA.X4;

        [Header("Качество (WebGL)")]
        [Range(0.25f, 1f)] public float fieldScaleWebGL = 0.5f;
        public bool extraPassWebGL = false;
        [Min(1000)] public int fieldBudgetWebGL = 200000;
        public OutlineEdgeAA edgeAntialiasingWebGL = OutlineEdgeAA.Off;

        [Tooltip("Нижняя граница авто-масштаба поля.")]
        [Range(0.25f, 1f)] public float minFieldScale = 0.25f;

        [Header("Ширина")]
        [Min(1f), Tooltip("Жёсткий потолок ширины, px. Определяет максимум проходов JFA.")]
        public float maxWidth = 256f;
        [Tooltip("Масштабировать ширины в px по высоте кадра относительно опорной.")]
        public bool scaleWithResolution = true;
        [Min(1f)] public float referenceHeight = 1080f;

        [Header("Стык групп")]
        [Min(0f), Tooltip("Сколько «ширин» даёт одна единица приоритета на стыке групп.")]
        public float priorityScale = 0.25f;
        [Tooltip("Свечение соседней группы заходит на силуэт: с приоритетом ≥ своего — целиком, с меньшим — гаснет вглубь на радиусе стыка.")]
        public bool seamOverlay = true;
        [Min(0f), Tooltip("Радиус стыка, px: смешение цветов, где встречаются свечения разных записей, и затухание свечения группы с меньшим приоритетом внутри чужого силуэта. 0 — резкая граница.")]
        public float seamBlend = 6f;

        [Header("Оптимизация")]
        [Tooltip("Считать поле только в экранном прямоугольнике подсвеченных объектов (+ ширина).")]
        public bool cropToBounds = true;
        [Tooltip("Отсекать растеризацию прямоугольником (scissor), а не только ранним выходом в шейдере — экономит заливку и запись.")]
        public bool scissor = true;

        [Header("Маска")]
        [Min(0f), Tooltip("Допуск сравнения с глубиной сцены, мировые единицы (+0.2% от дистанции).")]
        public float occlusionBias = 0.02f;
        [Range(0f, 1f), Tooltip("Порог альфы для прозрачных материалов в режиме Auto.")]
        public float transparentCutoff = 0.1f;
        [Tooltip("Цвет объекта в режиме прозрачности: Simple — упрощённое освещение по текстуре и цвету материала (работает всегда), " +
                 "SourceMaterial — исходный материал объекта (точное освещение; может не рисоваться с GPU Resident Drawer).")]
        public OutlineObjectColorMode objectColor = OutlineObjectColorMode.Simple;

        [Header("Отладка")]
        public OutlineDebugView debugView = OutlineDebugView.None;
        [Tooltip("Диагностика цены координат поверхности для бенчмарка. В режимах, кроме None, картинка неверная.")]
        public OutlineSurfaceDiag surfaceDiag = OutlineSurfaceDiag.None;
        public bool renderInSceneView = true;

        public float EffectiveFieldScale => IsWebGL ? fieldScaleWebGL : fieldScale;
        public bool EffectiveExtraPass => IsWebGL ? extraPassWebGL : extraPass;
        public int EffectiveFieldBudget => IsWebGL ? fieldBudgetWebGL : fieldBudget;
        public int EffectiveEdgeSamples => (int)(IsWebGL ? edgeAntialiasingWebGL : edgeAntialiasing);

        private static bool IsWebGL => Application.platform == RuntimePlatform.WebGLPlayer;
    }
}
