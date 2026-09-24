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
        internal const string Root = "Assets/OutlineUiLab";
        private const string ScenePath = Root + "/OutlineUiLab.unity";
        private const string PanelPath = Root + "/OutlineUiLabPanelSettings.asset";

        [MenuItem("Exerussus/OutlineUI/Lab/Собрать сцену бенчмарка")]
        public static void BuildScene() => Build();

        /// <summary>Собрать сцену бенчмарка UI. false — пользователь отменил сохранение текущей сцены.</summary>
        public static bool Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return false;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            EnsureFolder(Root);
            EnsureFolder(OutlineStylePresets.Folder); // текстура звёзд для иконки и пресета Textured

            var cam = Camera.main;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.09f, 0.1f, 0.13f);
            }

            var panel = GetOrCreatePanel();

            var go = new GameObject("UI Bench");
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = panel;
            var bench = go.AddComponent<OutlineUiBenchmark>();

            var names = OutlineUiStylePresets.Names;
            var so = new SerializedObject(bench);
            var styles = so.FindProperty("styles");
            styles.arraySize = names.Length;
            for (int i = 0; i < names.Length; i++)
                styles.GetArrayElementAtIndex(i).objectReferenceValue = OutlineUiStylePresets.GetOrCreate(names[i]);
            so.FindProperty("icon").objectReferenceValue = OutlineStylePresets.StarsTexture();
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[OutlineUiLab] Сцена собрана: {ScenePath}. Play — бенчмарк, лог Temp/outline-ui.log, снимки Temp/OutlineUiShots.");
            return true;
        }

        /// <summary>PanelSettings площадки UI: постоянный размер пикселя, тема проекта.</summary>
        internal static PanelSettings GetOrCreatePanel()
        {
            EnsureFolder(Root);
            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
            if (panel != null)
                return panel;
            panel = ScriptableObject.CreateInstance<PanelSettings>();
            panel.scaleMode = PanelScaleMode.ConstantPixelSize;
            panel.scale = 1f;
            var themes = AssetDatabase.FindAssets("t:ThemeStyleSheet");
            if (themes.Length > 0)
                panel.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(AssetDatabase.GUIDToAssetPath(themes[0]));
            AssetDatabase.CreateAsset(panel, PanelPath);
            return panel;
        }

        internal static void EnsureFolder(string path)
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
