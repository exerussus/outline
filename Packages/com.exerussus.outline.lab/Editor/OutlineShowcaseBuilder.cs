using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace Exerussus.Outline.Lab.Editor
{
    /// <summary>
    /// Сцены витрин (без замеров): мир — ряд из шести объектов со страницами стилей и эффектов;
    /// UI — колонки элементов со страницами UI-стилей и эффектов.
    /// </summary>
    public static class OutlineShowcaseBuilder
    {
        private const string WorldScenePath = OutlineLabBuilder.Root + "/OutlineShowcase.unity";
        private const string UiScenePath = OutlineUiLabBuilder.Root + "/OutlineUiShowcase.unity";

        // служебные стили площадки идут первыми, затем галерея и эффекты
        private static readonly string[] Service = { "Hover", "Selected", "Enemy", "Ghost", "WorldThin" };

        /// <summary>Витрина подсветки мира. false — пользователь отменил сохранение текущей сцены.</summary>
        public static bool BuildWorld()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return false;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            OutlineLabBuilder.EnsureFolder(OutlineStylePresets.Folder);
            Exerussus.Outline.Editor.OutlineFeatureInstaller.Install(out _);

            var cam = Camera.main;
            cam.transform.SetPositionAndRotation(new Vector3(0f, 1.9f, -8.2f), Quaternion.Euler(10f, 0f, 0f));
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.08f, 0.09f, 0.11f);
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            OutlineLabBuilder.CreateBloomVolume();

            var grey = OutlineLabBuilder.GetOrCreateLit("Grey", new Color(0.55f, 0.55f, 0.58f), null);
            var blue = OutlineLabBuilder.GetOrCreateLit("Blue", new Color(0.25f, 0.4f, 0.8f), null);
            OutlineLabBuilder.Primitive(PrimitiveType.Plane, "Floor", new Vector3(0f, 0f, 1f), new Vector3(1.8f, 1f, 0.8f), grey, false);

            // шесть объектов в ряд, крупно
            var shapes = new[] { PrimitiveType.Cube, PrimitiveType.Sphere, PrimitiveType.Capsule, PrimitiveType.Cylinder, PrimitiveType.Cube, PrimitiveType.Sphere };
            var slots = new Transform[shapes.Length];
            for (int i = 0; i < shapes.Length; i++)
            {
                float x = (i - (shapes.Length - 1) * 0.5f) * 2.1f;
                float y = shapes[i] == PrimitiveType.Capsule || shapes[i] == PrimitiveType.Cylinder ? 1f : 0.75f;
                var go = OutlineLabBuilder.Primitive(shapes[i], $"Slot {i + 1}", new Vector3(x, y, 0f), Vector3.one, i % 2 == 0 ? blue : grey, false);
                go.transform.rotation = Quaternion.Euler(0f, 25f + 17f * i, 0f);
                slots[i] = go.transform;
            }

            var ui = new GameObject("Showcase");
            var doc = ui.AddComponent<UIDocument>();
            doc.panelSettings = OutlineUiLabBuilder.GetOrCreatePanel();
            var show = ui.AddComponent<OutlineShowcaseScene>();

            var names = new List<string>(Service);
            names.AddRange(OutlineStylePresets.Gallery);
            names.AddRange(OutlineStylePresets.Effects);
            var so = new SerializedObject(show);
            so.FindProperty("viewCamera").objectReferenceValue = cam;
            var slotsProp = so.FindProperty("slots");
            slotsProp.arraySize = slots.Length;
            for (int i = 0; i < slots.Length; i++)
                slotsProp.GetArrayElementAtIndex(i).objectReferenceValue = slots[i];
            var styles = so.FindProperty("styles");
            styles.arraySize = names.Count;
            for (int i = 0; i < names.Count; i++)
                styles.GetArrayElementAtIndex(i).objectReferenceValue = OutlineStylePresets.GetOrCreate(names[i]);
            so.FindProperty("swapA").objectReferenceValue = OutlineStylePresets.GetOrCreate("Selected");
            so.FindProperty("swapB").objectReferenceValue = OutlineStylePresets.GetOrCreate("Enemy");
            so.FindProperty("fadeStyle").objectReferenceValue = OutlineStylePresets.GetOrCreate("Neon");
            so.FindProperty("motionStyle").objectReferenceValue = OutlineStylePresets.GetOrCreate("Textured");
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, WorldScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Outline] Витрина собрана: {WorldScenePath}.");
            return true;
        }

        /// <summary>Витрина подсветки UI. false — пользователь отменил сохранение текущей сцены.</summary>
        public static bool BuildUi()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return false;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            OutlineUiLabBuilder.EnsureFolder(OutlineUiLabBuilder.Root);
            OutlineLabBuilder.EnsureFolder(OutlineStylePresets.Folder);

            var cam = Camera.main;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.09f, 0.1f, 0.13f);
            }

            var go = new GameObject("UI Showcase");
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = OutlineUiLabBuilder.GetOrCreatePanel();
            var show = go.AddComponent<OutlineUiShowcase>();
            var so = new SerializedObject(show);
            var names = OutlineUiStylePresets.Names;
            var styles = so.FindProperty("styles");
            styles.arraySize = names.Length;
            for (int i = 0; i < names.Length; i++)
                styles.GetArrayElementAtIndex(i).objectReferenceValue = OutlineUiStylePresets.GetOrCreate(names[i]);
            so.FindProperty("swapA").objectReferenceValue = OutlineUiStylePresets.GetOrCreate("Selected");
            so.FindProperty("swapB").objectReferenceValue = OutlineUiStylePresets.GetOrCreate("Enemy");
            so.FindProperty("fadeStyle").objectReferenceValue = OutlineUiStylePresets.GetOrCreate("Neon");
            so.FindProperty("motionStyle").objectReferenceValue = OutlineUiStylePresets.GetOrCreate("Textured");
            so.FindProperty("icon").objectReferenceValue = OutlineStylePresets.StarsTexture();
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, UiScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[OutlineUiLab] Витрина собрана: {UiScenePath}.");
            return true;
        }
    }
}
