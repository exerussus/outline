using System.IO;
using Exerussus.Outline.Editor;
using Exerussus.Outline.Lab;
using Exerussus.Outline.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Exerussus.Outline.Lab.Editor
{
    /// <summary>
    /// Меню «Exerussus/Outline/Lab»: собрать сцену-площадку OutlineLab (фича в рендерерах URP,
    /// стили, материалы, тестовые объекты, демо, HUD, бенчмарк).
    /// </summary>
    public static class OutlineLabBuilder
    {
        internal const string Root = "Assets/OutlineLab";
        private const string Generated = OutlineStylePresets.Folder;
        private const string ScenePath = Root + "/OutlineLab.unity";
        private const int IgnoreRaycastLayer = 2;

        [MenuItem("Exerussus/Outline/Lab/Собрать сцену OutlineLab")]
        public static void BuildScene() => Build();

        /// <summary>Собрать сцену площадки (с бенчмарком). false — пользователь отменил сохранение текущей сцены.</summary>
        public static bool Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return false;

            // сначала новая сцена: NewScene(Single) выгружает неиспользуемые ассеты, и только что созданные
            // стили превратились бы в «фальшивый null» — ссылки на них в сцене сохранились бы пустыми
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            EnsureFolder(Generated);
            OutlineFeatureInstaller.Install(out var feature);

            var hover = OutlineStylePresets.GetOrCreate("Hover");
            var selected = OutlineStylePresets.GetOrCreate("Selected");
            var enemy = OutlineStylePresets.GetOrCreate("Enemy");
            var ghost = OutlineStylePresets.GetOrCreate("Ghost");
            var worldThin = OutlineStylePresets.GetOrCreate("WorldThin");

            var grey = GetOrCreateLit("Grey", new Color(0.55f, 0.55f, 0.58f), null);
            var blue = GetOrCreateLit("Blue", new Color(0.25f, 0.4f, 0.8f), null);
            var red = GetOrCreateLit("Red", new Color(0.8f, 0.25f, 0.2f), null);
            var grid = GetOrCreateLit("GridClip", Color.white, GetOrCreateGridTexture());

            var cam = Camera.main;
            cam.transform.SetPositionAndRotation(new Vector3(0f, 3.4f, -9f), Quaternion.Euler(15f, 0f, 0f));
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            cam.backgroundColor = new Color(0.08f, 0.09f, 0.11f);
            cam.clearFlags = CameraClearFlags.SolidColor;

            // окружение (не кликается)
            var floor = Primitive(PrimitiveType.Plane, "Floor", new Vector3(0f, 0f, 2f), Vector3.one * 2f, grey, false);
            floor.layer = IgnoreRaycastLayer;
            var wall = Primitive(PrimitiveType.Cube, "Wall", new Vector3(2.4f, 1.1f, 0.3f), new Vector3(2.6f, 2.2f, 0.2f), grey, false);
            wall.layer = IgnoreRaycastLayer;

            // жёсткие рёбра: классический тест разрывов hull-метода
            var hardCube = Primitive(PrimitiveType.Cube, "HardCube (Ghost)", new Vector3(-3.2f, 0.6f, 0f), Vector3.one * 1.1f, blue, true);
            hardCube.transform.rotation = Quaternion.Euler(0f, 35f, 0f);
            AddTarget(hardCube, ghost, 0, 0);

            // тонкая геометрия
            Primitive(PrimitiveType.Cylinder, "ThinCylinder", new Vector3(-1.9f, 1f, 0.6f), new Vector3(0.06f, 1f, 0.06f), grey, true);
            Primitive(PrimitiveType.Cylinder, "ThinBar", new Vector3(-1.9f, 2.05f, 0.6f), new Vector3(0.05f, 0.8f, 0.05f), grey, true)
                .transform.rotation = Quaternion.Euler(0f, 0f, 90f);

            // стык групп: A+C — группа 1 (общий силуэт), B — группа 2 (свой контур, приоритет Selected выше)
            var a = Primitive(PrimitiveType.Cube, "Pair A (Selected, g1)", new Vector3(-0.5f, 0.5f, 0f), Vector3.one, blue, true);
            AddTarget(a, selected, 1, 0);
            var c = Primitive(PrimitiveType.Sphere, "Pair C (Selected, g1)", new Vector3(-0.95f, 0.45f, -0.4f), Vector3.one * 0.9f, blue, true);
            AddTarget(c, selected, 1, 0);
            var b = Primitive(PrimitiveType.Cube, "Pair B (Enemy, g2)", new Vector3(0.25f, 0.75f, 0.45f), Vector3.one * 0.9f, red, true);
            b.transform.rotation = Quaternion.Euler(0f, 25f, 0f);
            AddTarget(b, enemy, 2, 0);

            // за стеной: перекрытый стиль (штрих)
            var hidden = Primitive(PrimitiveType.Sphere, "BehindWall (Enemy)", new Vector3(2.4f, 0.7f, 1.8f), Vector3.one * 1.2f, red, true);
            AddTarget(hidden, enemy, 2, 0);
            var capsuleBehind = Primitive(PrimitiveType.Capsule, "BehindWall (Selected)", new Vector3(3.3f, 1f, 1.4f), Vector3.one * 0.8f, blue, true);
            AddTarget(capsuleBehind, selected, 1, 0);

            // альфа-клип (решётка), двусторонняя
            var quad = Primitive(PrimitiveType.Quad, "AlphaClipGrid", new Vector3(1.1f, 1.3f, -1.2f), new Vector3(1.4f, 1.4f, 1f), grid, true);
            quad.transform.rotation = Quaternion.Euler(0f, -20f, 0f);

            // ширина в мире: одинаковые капсулы на разной глубине
            for (int i = 0; i < 3; i++)
            {
                var cap = Primitive(PrimitiveType.Capsule, $"WorldWidth {i}", new Vector3(-4.6f + i * 0.2f, 1f, -1f + i * 4f), Vector3.one * 0.7f, grey, true);
                AddTarget(cap, worldThin, 3, 0);
            }

            // витрина стилей: по объекту на пресет, у каждого своя группа
            var shapes = new[] { PrimitiveType.Sphere, PrimitiveType.Cube, PrimitiveType.Capsule, PrimitiveType.Cylinder };
            var gallery = OutlineStylePresets.Gallery;
            for (int i = 0; i < gallery.Length; i++)
            {
                float x = (i - (gallery.Length - 1) * 0.5f) * 1.6f;
                var go = Primitive(shapes[i % shapes.Length], $"Gallery {gallery[i]}", new Vector3(x, 0.55f, 5.5f),
                    Vector3.one * 0.9f, i % 2 == 0 ? grey : blue, true);
                AddTarget(go, OutlineStylePresets.GetOrCreate(gallery[i]), 10 + i, 0);
            }

            // второй ряд: эффекты свечения и заливки
            var effects = OutlineStylePresets.Effects;
            for (int i = 0; i < effects.Length; i++)
            {
                // сдвиг на полшага — чтобы ряд не прятался за первым
                float x = (i - (effects.Length - 1) * 0.5f) * 1.6f + 0.8f;
                var go = Primitive(shapes[(i + 1) % shapes.Length], $"Effect {effects[i]}", new Vector3(x, 0.9f, 8f),
                    Vector3.one * 0.9f, i % 2 == 0 ? blue : grey, true);
                AddTarget(go, OutlineStylePresets.GetOrCreate(effects[i]), 20 + i, 0);
            }

            CreateBloomVolume();
            // точка обзора второго ряда (клавиша V в демо, визуальные режимы бенчмарка)
            var effectsView = new GameObject("EffectsView").transform;
            effectsView.SetPositionAndRotation(new Vector3(0.8f, 4f, 3f), Quaternion.Euler(32f, 0f, 0f));

            // витрина бенчмарка: отдельный объект вдали от сцены, столбик перед ним — для перекрытой части
            var showFloor = Primitive(PrimitiveType.Plane, "Showcase Floor", new Vector3(30f, 0f, 1f), new Vector3(1.1f, 1f, 0.9f), grey, false);
            showFloor.layer = IgnoreRaycastLayer;
            var showcase = Primitive(PrimitiveType.Cube, "Showcase Cube", new Vector3(30f, 0.7f, 0f), Vector3.one, blue, false);
            showcase.transform.rotation = Quaternion.Euler(0f, 30f, 0f);
            showcase.layer = IgnoreRaycastLayer;
            // столбик на линии взгляда, чуть правее центра куба: перекрывает его полосой (видна перекрытая часть стиля)
            var post = Primitive(PrimitiveType.Cylinder, "Showcase Post", new Vector3(30.18f, 0.9f, -0.85f), new Vector3(0.09f, 0.9f, 0.09f), grey, false);
            post.layer = IgnoreRaycastLayer;
            var showcaseView = new GameObject("ShowcaseView").transform;
            showcaseView.SetPositionAndRotation(new Vector3(30f, 1.55f, -2.1f), Quaternion.Euler(20f, 0f, 0f));

            // сетка 7×3 разных фигур вокруг куба (куб — в центре переднего ряда); трио — куб и два соседа
            var crowd = new Transform[21];
            var trio = new Transform[3];
            var gridShapes = new[] { PrimitiveType.Sphere, PrimitiveType.Capsule, PrimitiveType.Cylinder, PrimitiveType.Cube };
            int n = 0;
            for (int row = 0; row < 3; row++)
            {
                for (int col = 0; col < 7; col++)
                {
                    Transform tr;
                    if (row == 0 && col == 3)
                    {
                        tr = showcase.transform;
                    }
                    else
                    {
                        var type = gridShapes[(row * 7 + col) % gridShapes.Length];
                        float h = type == PrimitiveType.Capsule || type == PrimitiveType.Cylinder ? 0.8f : 0.45f;
                        var go = Primitive(type, $"Showcase Shape {row}-{col}", new Vector3(30f + (col - 3) * 1.3f, h, row * 1.5f),
                            Vector3.one * 0.8f, (row + col) % 2 == 0 ? grey : blue, false);
                        go.transform.rotation = Quaternion.Euler(0f, 17f * n, 0f);
                        go.layer = IgnoreRaycastLayer;
                        tr = go.transform;
                    }
                    crowd[n++] = tr;
                    if (row == 0 && col >= 2 && col <= 4)
                        trio[col - 2] = tr;
                }
            }
            var trioView = new GameObject("ShowcaseView x3").transform;
            trioView.SetPositionAndRotation(new Vector3(30f, 2.1f, -3.9f), Quaternion.Euler(20f, 0f, 0f));
            var crowdView = new GameObject("ShowcaseView x21").transform;
            crowdView.SetPositionAndRotation(new Vector3(30f, 6.2f, -5.2f), Quaternion.Euler(45f, 0f, 0f));

            var tiers = new[]
            {
                ("1 объект", showcaseView, new[] { showcase.transform }),
                ("3 объекта", trioView, trio),
                ("21 объект", crowdView, crowd),
            };
            CreateHud(feature, cam, effectsView, tiers);

            var demo = cam.gameObject.AddComponent<OutlineLabDemo>();
            var demoSo = new SerializedObject(demo);
            demoSo.FindProperty("targetCamera").objectReferenceValue = cam;
            demoSo.FindProperty("effectsView").objectReferenceValue = effectsView;
            demoSo.FindProperty("hoverStyle").objectReferenceValue = hover;
            demoSo.FindProperty("selectedStyle").objectReferenceValue = selected;
            demoSo.FindProperty("enemyStyle").objectReferenceValue = enemy;
            demoSo.FindProperty("feature").objectReferenceValue = feature;
            demoSo.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Outline] Сцена собрана: {ScenePath}. Play: ховер, ЛКМ — выделить, ПКМ — враг, Backspace — снять, 0..3 — отладка, P — паттерн, C — кроп.");
            return true;
        }

        // ------------------------------------------------------------------ Фича

        // ------------------------------------------------------------------ Ассеты

        internal static void CreateBloomVolume()
        {
            string path = $"{Generated}/LabVolume.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
                var bloom = profile.Add<Bloom>(true);
                bloom.threshold.Override(1.1f);
                bloom.intensity.Override(0.7f);
                bloom.scatter.Override(0.6f);
                bloom.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(bloom, profile);
                EditorUtility.SetDirty(profile);
            }

            var go = new GameObject("Global Volume (Bloom)");
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;
        }

        private static void CreateHud(OutlineRendererFeature feature, Camera cam, Transform effectsView,
            (string name, Transform view, Transform[] objects)[] tiers)
        {
            string path = $"{Generated}/LabPanelSettings.asset";
            var panel = AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.PanelSettings>(path);
            if (panel == null)
            {
                panel = ScriptableObject.CreateInstance<UnityEngine.UIElements.PanelSettings>();
                panel.scaleMode = UnityEngine.UIElements.PanelScaleMode.ConstantPixelSize;
                // любая тема проекта, если есть (без неё UI Toolkit предупреждает, но шрифт задан явно)
                var themes = AssetDatabase.FindAssets("t:ThemeStyleSheet");
                if (themes.Length > 0)
                    panel.themeStyleSheet = AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.ThemeStyleSheet>(
                        AssetDatabase.GUIDToAssetPath(themes[0]));
                AssetDatabase.CreateAsset(panel, path);
            }

            var go = new GameObject("HUD");
            var doc = go.AddComponent<UnityEngine.UIElements.UIDocument>();
            doc.panelSettings = panel;
            // бенчмарк стартует с Play, гоняет режимы, пишет Temp/outline.log и выходит из Play
            var bench = go.AddComponent<OutlineBenchmark>();
            var bso = new SerializedObject(bench);
            bso.FindProperty("feature").objectReferenceValue = feature;
            bso.FindProperty("viewCamera").objectReferenceValue = cam;
            bso.FindProperty("effectsView").objectReferenceValue = effectsView;
            var tiersProp = bso.FindProperty("showcaseTiers");
            tiersProp.arraySize = tiers.Length;
            for (int t = 0; t < tiers.Length; t++)
            {
                var el = tiersProp.GetArrayElementAtIndex(t);
                el.FindPropertyRelative("name").stringValue = tiers[t].name;
                el.FindPropertyRelative("view").objectReferenceValue = tiers[t].view;
                var objs = el.FindPropertyRelative("objects");
                objs.arraySize = tiers[t].objects.Length;
                for (int i = 0; i < tiers[t].objects.Length; i++)
                    objs.GetArrayElementAtIndex(i).objectReferenceValue = tiers[t].objects[i];
            }
            // витрина: служебные стили, первый ряд, эффекты
            var names = new System.Collections.Generic.List<string> { "Hover", "Selected", "Enemy", "Ghost", "WorldThin" };
            names.AddRange(OutlineStylePresets.Gallery);
            names.AddRange(OutlineStylePresets.Effects);
            var styles = bso.FindProperty("showcaseStyles");
            styles.arraySize = names.Count;
            for (int i = 0; i < names.Count; i++)
                styles.GetArrayElementAtIndex(i).objectReferenceValue = OutlineStylePresets.GetOrCreate(names[i]);
            bso.ApplyModifiedPropertiesWithoutUndo();

            var hud = go.AddComponent<OutlineLabHud>();
            var so = new SerializedObject(hud);
            so.FindProperty("feature").objectReferenceValue = feature;
            so.FindProperty("benchmark").objectReferenceValue = bench;
            so.FindProperty("uiHoverStyle").objectReferenceValue = OutlineUiStylePresets.GetOrCreate("Hover");
            var uiStyles = so.FindProperty("uiStyles");
            uiStyles.arraySize = OutlineUiStylePresets.Names.Length - 1;
            for (int i = 1; i < OutlineUiStylePresets.Names.Length; i++)
                uiStyles.GetArrayElementAtIndex(i - 1).objectReferenceValue = OutlineUiStylePresets.GetOrCreate(OutlineUiStylePresets.Names[i]);
            so.FindProperty("uiIcon").objectReferenceValue = OutlineStylePresets.StarsTexture();
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static Material GetOrCreateLit(string name, Color color, Texture2D alphaClipTexture)
        {
            string path = $"{Generated}/Mat_{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null)
                return mat;

            mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            mat.SetColor("_BaseColor", color);
            if (alphaClipTexture != null)
            {
                mat.SetTexture("_BaseMap", alphaClipTexture);
                mat.SetFloat("_AlphaClip", 1f);
                mat.SetFloat("_Cutoff", 0.5f);
                mat.SetFloat("_Cull", (float)CullMode.Off);
                mat.EnableKeyword("_ALPHATEST_ON");
                mat.doubleSidedGI = true;
                mat.renderQueue = (int)RenderQueue.AlphaTest;
            }
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        internal static Texture2D GetOrCreateGridTexture()
        {
            string path = $"{Generated}/Tex_Grid.png";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex != null)
                return tex;

            const int size = 256;
            const int cell = 32;
            const int line = 6;
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                bool bar = x % cell < line || y % cell < line;
                pixels[y * size + x] = bar ? new Color32(210, 210, 220, 255) : new Color32(0, 0, 0, 0);
            }
            t.SetPixels32(pixels);
            t.Apply();
            File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ------------------------------------------------------------------ Сцена

        internal static GameObject Primitive(PrimitiveType type, string name, Vector3 pos, Vector3 scale, Material mat, bool pickable)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.position = pos;
            go.transform.localScale = scale;
            if (mat != null)
                go.GetComponent<Renderer>().sharedMaterial = mat;
            if (pickable)
                go.AddComponent<OutlineLabPickable>();
            return go;
        }

        internal static void AddTarget(GameObject go, OutlineStyle style, int group, int priority)
        {
            var target = go.AddComponent<OutlineTarget>();
            var so = new SerializedObject(target);
            so.FindProperty("style").objectReferenceValue = style;
            so.FindProperty("group").intValue = group;
            so.FindProperty("priority").intValue = priority;
            so.ApplyModifiedPropertiesWithoutUndo();
            // OnEnable прошёл до назначения стиля — перевключаем, чтобы подсветка появилась сразу
            target.enabled = false;
            target.enabled = true;
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
