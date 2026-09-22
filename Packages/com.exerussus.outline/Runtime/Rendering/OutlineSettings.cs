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
        [Range(0.25f, 1f), Tooltip("Разрешение поля расстояния относительно кадра. 1 — точные тонкие линии.")]
        public float fieldScale = 1f;
        [Tooltip("Дополнительный проход JFA с шагом 1 (JFA+1) — чище на стыках.")]
        public bool extraPass = true;

        [Header("Качество (WebGL)")]
        [Range(0.25f, 1f)] public float fieldScaleWebGL = 0.5f;
        public bool extraPassWebGL = false;

        [Header("Ширина")]
        [Min(1f), Tooltip("Жёсткий потолок ширины, px. Определяет максимум проходов JFA.")]
        public float maxWidth = 256f;
        [Tooltip("Масштабировать ширины в px по высоте кадра относительно опорной.")]
        public bool scaleWithResolution = true;
        [Min(1f)] public float referenceHeight = 1080f;

        [Header("Стык групп")]
        [Min(0f), Tooltip("Сколько «ширин» даёт одна единица приоритета на стыке групп.")]
        public float priorityScale = 0.25f;
        [Tooltip("Свечение группы с приоритетом ≥ рисуется поверх силуэта чужой группы (контур на стыке).")]
        public bool seamOverlay = true;
        [Min(0f), Tooltip("Радиус мягкого смешения цветов, где встречаются свечения разных записей, px. 0 — резкая граница.")]
        public float seamBlend = 6f;

        [Header("Оптимизация")]
        [Tooltip("Считать поле только в экранном прямоугольнике подсвеченных объектов (+ ширина).")]
        public bool cropToBounds = true;
        [Tooltip("Отсекать растеризацию прямоугольником (scissor), а не только ранним выходом в шейдере — экономит заливку и запись.")]
        public bool scissor = true;
        [Tooltip("Экспериментально: подстраивать разрешение поля под бюджет GPU по ProfilingSampler. " +
                 "GPU-рекордер в редакторе даёт недостоверные значения; на WebGL не работает.")]
        public bool autoFieldScale = false;
        [Min(0.1f), Tooltip("Бюджет GPU на всю подсветку, мс.")]
        public float gpuBudgetMs = 1.5f;
        [Range(0.25f, 1f), Tooltip("Нижняя граница авто-разрешения поля.")]
        public float minFieldScale = 0.375f;

        [Header("Маска")]
        [Min(0f), Tooltip("Допуск сравнения с глубиной сцены, мировые единицы (+0.2% от дистанции).")]
        public float occlusionBias = 0.02f;
        [Range(0f, 1f), Tooltip("Порог альфы для прозрачных материалов в режиме Auto.")]
        public float transparentCutoff = 0.1f;

        [Header("Отладка")]
        public OutlineDebugView debugView = OutlineDebugView.None;
        public bool renderInSceneView = true;

        public float EffectiveFieldScale => IsWebGL ? fieldScaleWebGL : fieldScale;
        public bool EffectiveExtraPass => IsWebGL ? extraPassWebGL : extraPass;

        private static bool IsWebGL => Application.platform == RuntimePlatform.WebGLPlayer;
    }
}
