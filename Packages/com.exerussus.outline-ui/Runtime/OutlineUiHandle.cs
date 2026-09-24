using System;

namespace Exerussus.Outline.UI
{
    /// <summary>
    /// Непрозрачный хэндл подсветки элемента UI (индекс + версия). Методы — тонкая обёртка над OutlineUi;
    /// на «протухшем» хэндле (после Hide) ничего не делают.
    /// </summary>
    public readonly struct OutlineUiHandle : IEquatable<OutlineUiHandle>
    {
        internal readonly int Slot;
        internal readonly int Version;

        internal OutlineUiHandle(int slot, int version)
        {
            Slot = slot;
            Version = version;
        }

        public static OutlineUiHandle Invalid => default;

        public bool IsAlive => OutlineUi.IsAlive(this);

        /// <summary>Сменить стиль сразу (duration = 0) или плавно.</summary>
        public void SetStyle(OutlineStyle style, float duration = 0f) => OutlineUi.SetStyle(this, style, duration);

        public void SetFade(float value) => OutlineUi.SetFade(this, value);
        public void FadeTo(float target, float duration) => OutlineUi.FadeTo(this, target, duration);
        public void FadeOutAndHide(float duration) => OutlineUi.FadeOutAndHide(this, duration);
        public void Hide() => OutlineUi.Hide(this);

        public bool Equals(OutlineUiHandle other) => Slot == other.Slot && Version == other.Version;
        public override bool Equals(object obj) => obj is OutlineUiHandle other && Equals(other);
        public override int GetHashCode() => (Slot * 397) ^ Version;
        public static bool operator ==(OutlineUiHandle a, OutlineUiHandle b) => a.Equals(b);
        public static bool operator !=(OutlineUiHandle a, OutlineUiHandle b) => !a.Equals(b);
    }
}
