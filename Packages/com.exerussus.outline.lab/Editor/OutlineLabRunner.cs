using Exerussus.Outline.Editor;
using Exerussus.Outline.Rendering;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Exerussus.Outline.Lab.Editor
{
    /// <summary>
    /// Запуск в одну кнопку: сбросить стили к пресетам и настройки фичи к значениям по умолчанию, подключить фичу
    /// к рендерерам URP, собрать сцену заново и войти в Play.
    /// </summary>
    public static class OutlineLabRunner
    {
        [MenuItem("Exerussus/Outline/Lab/Запустить бенчмарк", priority = 0)]
        public static void RunWorldBenchmark() => Run(true, OutlineLabBuilder.Build);

        [MenuItem("Exerussus/Outline/Lab/Запустить витрину", priority = 1)]
        public static void RunWorldShowcase() => Run(true, OutlineShowcaseBuilder.BuildWorld);

        [MenuItem("Exerussus/OutlineUI/Lab/Запустить бенчмарк", priority = 0)]
        public static void RunUiBenchmark() => Run(false, OutlineUiLabBuilder.Build);

        [MenuItem("Exerussus/OutlineUI/Lab/Запустить витрину", priority = 1)]
        public static void RunUiShowcase() => Run(false, OutlineShowcaseBuilder.BuildUi);

        private static void Run(bool world, System.Func<bool> build)
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("[OutlineLab] Сначала выйдите из Play.");
                return;
            }
            // папка пресетов мира: стили мира и текстура звёзд (иконка и пресет Textured в UI)
            OutlineLabBuilder.EnsureFolder(OutlineStylePresets.Folder);
            // мир: стили мира + фича в URP с настройками по умолчанию; UI: стили UI (фичу URP подсветка UI не использует)
            if (world)
            {
                OutlineStylePresets.ResetAll();
                OutlineFeatureInstaller.Install(out _);
                ResetFeatureSettings();
            }
            else
            {
                OutlineUiStylePresets.ResetAll();
            }
            AssetDatabase.SaveAssets();
            if (!build())
                return;
            EditorApplication.EnterPlaymode();
        }

        /// <summary>Настройки OutlineRendererFeature во всех рендерерах URP проекта — к значениям по умолчанию.</summary>
        private static void ResetFeatureSettings()
        {
            string defaults = JsonUtility.ToJson(new OutlineSettings());
            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRendererData"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith("Assets/"))
                    continue;
                var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
                if (data == null)
                    continue;
                foreach (var f in data.rendererFeatures)
                {
                    if (f is OutlineRendererFeature feature)
                    {
                        JsonUtility.FromJsonOverwrite(defaults, feature.settings);
                        EditorUtility.SetDirty(feature);
                    }
                }
            }
        }
    }
}
