using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace Exerussus.Outline.Lab.Editor
{
    /// <summary>
    /// Меню «Exerussus/OutlineUI/Lab»: сцена бенчмарка подсветки UI Toolkit (UIDocument + OutlineUiBenchmark
    /// со стилями площадки) и запуск бенчмарка.
    /// </summary>
    public static class OutlineUiLabBuilder
    {
        private const string Root = "Assets/OutlineUiLab";
        private const string ScenePath = Root + "/OutlineUiLab.unity";
        private const string PanelPath = Root + "/OutlineUiLabPanelSettings.asset";

        // прозрачность и маскировка в UI не применяются — этих стилей в бенче нет
        private static readonly HashSet<string> Skip = new() { "Cloak", "Glass" };

        [MenuItem("Exerussus/OutlineUI/Lab/Собрать сцену бенчмарка")]
        public static void BuildScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            EnsureFolder(Root);
            EnsureFolder(OutlineStylePresets.Folder);

            var cam = Camera.main;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.09f, 0.1f, 0.13f);
            }

            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
            if (panel == null)
            {
                panel = ScriptableObject.CreateInstance<PanelSettings>();
                panel.scaleMode = PanelScaleMode.ConstantPixelSize;
                panel.scale = 1f;
                var themes = AssetDatabase.FindAssets("t:ThemeStyleSheet");
                if (themes.Length > 0)
                    panel.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(AssetDatabase.GUIDToAssetPath(themes[0]));
                AssetDatabase.CreateAsset(panel, PanelPath);
            }

            var go = new GameObject("UI Bench");
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = panel;
            var bench = go.AddComponent<OutlineUiBenchmark>();

            var names = new List<string> { "Hover", "Selected", "Enemy" };
            names.AddRange(OutlineStylePresets.Gallery);
            foreach (var n in OutlineStylePresets.Effects)
            {
                if (!Skip.Contains(n))
                    names.Add(n);
            }
            var so = new SerializedObject(bench);
            var styles = so.FindProperty("styles");
            styles.arraySize = names.Count;
            for (int i = 0; i < names.Count; i++)
                styles.GetArrayElementAtIndex(i).objectReferenceValue = OutlineStylePresets.GetOrCreate(names[i]);
            so.FindProperty("icon").objectReferenceValue = OutlineStylePresets.StarsTexture();
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[OutlineUiLab] Сцена собрана: {ScenePath}. Play — бенчмарк, лог Temp/outline-ui.log, снимки Temp/OutlineUiShots.");
        }

        [MenuItem("Exerussus/OutlineUI/Lab/Запустить бенчмарк")]
        public static void RunBenchmark()
        {
            if (EditorApplication.isPlaying)
                return;
            if (!File.Exists(ScenePath))
                BuildScene();
            else if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            if (EditorSceneManager.GetActiveScene().path != ScenePath)
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent))
                EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
