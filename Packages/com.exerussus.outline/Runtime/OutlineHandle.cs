using System;

namespace Exerussus.Outline
{
    /// <summary>
    /// Непрозрачный хэндл подсветки (индекс + версия). Все методы — тонкая обёртка над OutlineApi;
    /// на «протухшем» хэндле (после Hide) методы ничего не делают.
    /// </summary>
    public readonly struct OutlineHandle : IEquatable<OutlineHandle>
    {
        internal readonly int Slot;
        internal readonly int Version;

        internal OutlineHandle(int slot, int version)
        {
            Slot = slot;
            Version = version;
        }

        public static OutlineHandle Invalid => default;

        public bool IsAlive => OutlineApi.IsAlive(this);

        public void SetStyle(OutlineStyle style) => OutlineApi.SetStyle(this, style);
        public void SetGroup(int group) => OutlineApi.SetGroup(this, group);
        public void SetPriority(int priority) => OutlineApi.SetPriority(this, priority);

        /// <summary>Мгновенно задать непрозрачность подсветки 0..1.</summary>
        public void SetFade(float value) => OutlineApi.SetFade(this, value);

        /// <summary>Плавно перейти к непрозрачности target за duration секунд.</summary>
        public void FadeTo(float target, float duration) => OutlineApi.FadeTo(this, target, duration);

        /// <summary>Плавно погасить и снять подсветку.</summary>
        public void FadeOutAndHide(float duration) => OutlineApi.FadeOutAndHide(this, duration);

        /// <summary>Снять подсветку сразу. Хэндл протухает.</summary>
        public void Hide() => OutlineApi.Hide(this);

        public bool Equals(OutlineHandle other) => Slot == other.Slot && Version == other.Version;
        public override bool Equals(object obj) => obj is OutlineHandle other && Equals(other);
        public override int GetHashCode() => (Slot * 397) ^ Version;
        public static bool operator ==(OutlineHandle a, OutlineHandle b) => a.Equals(b);
        public static bool operator !=(OutlineHandle a, OutlineHandle b) => !a.Equals(b);
    }
}
