namespace Exerussus.Outline
{
    /// <summary>Параметры конкретной подсветки (на хэндл).</summary>
    public struct OutlineOptions
    {
        /// <summary>Группа: объекты одной группы сливаются в общий силуэт, между группами — свой контур.</summary>
        public int group;
        /// <summary>Добавка к приоритету стиля.</summary>
        public int priority;
        public OutlineAlphaMode alphaMode;
        /// <summary>Порог для OutlineAlphaMode.ForceClip.</summary>
        public float alphaThreshold;
        /// <summary>Для Show(GameObject): брать рендереры детей.</summary>
        public bool includeChildren;
        /// <summary>Для Show(GameObject): брать и неактивных детей.</summary>
        public bool includeInactive;
        /// <summary>Плавное появление, секунды. 0 — сразу.</summary>
        public float fadeIn;
        /// <summary>
        /// Слой 0..3. Слои рисуются независимо (свои маска, поле и композит) и накладываются по порядку: 1 поверх 0.
        /// Внутри слоя — группы, приоритеты, стыки и правило «последний Show владеет рендерером».
        /// </summary>
        public int layer;

        public static OutlineOptions Default => new()
        {
            group = 0,
            priority = 0,
            alphaMode = OutlineAlphaMode.Auto,
            alphaThreshold = 0.5f,
            includeChildren = true,
            includeInactive = false,
            fadeIn = 0f,
            layer = 0,
        };

        /// <summary>Опции по умолчанию в указанном слое.</summary>
        public static OutlineOptions InLayer(int layer, int group = 0, float fadeIn = 0f)
        {
            var o = Default;
            o.layer = layer;
            o.group = group;
            o.fadeIn = fadeIn;
            return o;
        }

        public static OutlineOptions InGroup(int group, float fadeIn = 0f)
        {
            var o = Default;
            o.group = group;
            o.fadeIn = fadeIn;
            return o;
        }
    }
}
