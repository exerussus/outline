using UnityEngine;

namespace Exerussus.Outline
{
    /// <summary>Единые часы подсветки (stateless): в плей-моде — unscaled-время, в редакторе — реальное.</summary>
    public static class OutlineClock
    {
        public static float Now => Application.isPlaying ? Time.unscaledTime : Time.realtimeSinceStartup;
    }
}
