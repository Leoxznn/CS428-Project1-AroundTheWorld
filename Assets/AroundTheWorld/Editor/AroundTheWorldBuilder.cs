using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Vuforia;
using Object = UnityEngine.Object;

// Menu: Around The World > Build Scene
//
// Builds Assets/Scenes/AroundTheWorld.unity from the Roswell starter scene:
//   * Merge cube  -> Eiffel Tower (Paris)      : °F, 12-hour clock
//   * Class cube  -> Belém Tower (Lisbon)      : °C, 24-hour clock
// Each knickknack gets 4 text sides, 2 self-made models (saved as prefabs in MyModels),
// up to 3 downloaded models (whatever you drop into WebModels/Paris and WebModels/Belem),
// an ambient sound (your file from Sounds/<place>, or a generated one), two lighting
// styles switched by flipping the cube, and its own layer so its lights only affect it.
//
// Re-running the menu rebuilds both knickknacks (manual edits to them are lost),
// so add your downloaded models/sounds to the folders and simply run it again.
public static class AroundTheWorldBuilder
{
    const string Root = "Assets/AroundTheWorld";
    const string SourceScene = "Assets/Scenes/RoswellScene.unity";
    const string TargetScene = "Assets/Scenes/AroundTheWorld.unity";
    const float Top = 0.056f; // top surface of the plinth that sits on the cube (cube is 0.1 wide)

    static int currentLayer;

    [MenuItem("Around The World/Build Scene")]
    public static void BuildMenu()
    {
        if (!EditorUtility.DisplayDialog("Around The World",
                "Build (or rebuild) Assets/Scenes/AroundTheWorld.unity?\n\nBoth knickknacks are regenerated, so manual edits to them will be lost.",
                "Build", "Cancel")) return;
        string report = Build();
        EditorUtility.DisplayDialog("Around The World", report, "OK");
    }

    // Unity.exe -batchmode -quit -projectPath ... -executeMethod AroundTheWorldBuilder.BuildFromCommandLine
    public static void BuildFromCommandLine()
    {
        Debug.Log(Build());
    }

    static string Build()
    {
        var report = new List<string>();
        EnsureFolders();

        if (!File.Exists(TargetScene))
        {
            if (!AssetDatabase.CopyAsset(SourceScene, TargetScene))
                throw new Exception("Could not copy " + SourceScene);
        }
        var scene = EditorSceneManager.OpenScene(TargetScene, OpenSceneMode.Single);

        foreach (var go in scene.GetRootGameObjects())
            if (go.name.StartsWith("Knickknack_")) Object.DestroyImmediate(go);

        CleanMergeTarget(report);

        int parisLayer = EnsureLayer("Paris");
        int belemLayer = EnsureLayer("Belem");

        var globalLight = GameObject.Find("Directional Light");
        if (globalLight != null)
        {
            var l = globalLight.GetComponent<Light>();
            l.cullingMask &= ~((1 << parisLayer) | (1 << belemLayer));
            EditorUtility.SetDirty(l);
        }

        var paris = BuildParis(parisLayer, report);
        paris.transform.position = new Vector3(-0.15f, 0, 0.5f);
        var belem = BuildBelem(belemLayer, report);
        belem.transform.position = new Vector3(0.15f, 0, 0.5f);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(TargetScene, true) };
        PlayerSettings.productName = "Around the World AR";
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        if ((int)PlayerSettings.Android.minSdkVersion < (int)AndroidSdkVersions.AndroidApiLevel26)
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
        if (string.IsNullOrEmpty(PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android)) ||
            PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android).Contains("DefaultCompany"))
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.cs428.aroundtheworld");
        PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.iOS, PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android));
        if (string.IsNullOrEmpty(PlayerSettings.iOS.cameraUsageDescription))
            PlayerSettings.iOS.cameraUsageDescription = "The camera is used to track the AR cubes.";

        AssetDatabase.SaveAssets();

        report.Insert(0, "Scene saved: " + TargetScene);
        if (string.IsNullOrEmpty(ApiKeys.OpenWeather)) report.Add("WARNING: ApiKeys.OpenWeather is empty - weather will not load.");
        return string.Join("\n", report);
    }

    // ------------------------------------------------------------------ scene setup

    static void EnsureFolders()
    {
        foreach (var f in new[] { "Materials", "MyModels", "WebModels", "WebModels/Paris", "WebModels/Belem", "Sounds", "Sounds/Paris", "Sounds/Belem" })
        {
            string path = Root + "/" + f;
            if (!AssetDatabase.IsValidFolder(path))
            {
                string parent = Path.GetDirectoryName(path).Replace('\\', '/');
                AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
            }
        }
    }

    static void CleanMergeTarget(List<string> report)
    {
        var all = Object.FindObjectsOfType<MultiTargetBehaviour>(true);
        var merge = all.FirstOrDefault(m => m.TargetName == "Merge") ?? all.FirstOrDefault(m => m.name == "KnickKnack");
        if (merge == null) { report.Add("No Merge MultiTarget found in scene - it will be created at runtime."); return; }

        var kids = new List<GameObject>();
        foreach (Transform t in merge.transform)
            if (!t.name.Contains("Preview")) kids.Add(t.gameObject);
        foreach (var k in kids) Object.DestroyImmediate(k);
        if (kids.Count > 0) report.Add($"Removed {kids.Count} Roswell objects from the Merge target.");

        var handler = merge.GetComponent<DefaultObserverEventHandler>();
        if (handler != null)
        {
            for (int i = handler.OnTargetFound.GetPersistentEventCount() - 1; i >= 0; i--) UnityEventTools.RemovePersistentListener(handler.OnTargetFound, i);
            for (int i = handler.OnTargetLost.GetPersistentEventCount() - 1; i >= 0; i--) UnityEventTools.RemovePersistentListener(handler.OnTargetLost, i);
            EditorUtility.SetDirty(handler);
        }
    }

    static int EnsureLayer(string name)
    {
        var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = tagManager.FindProperty("layers");
        for (int i = 8; i < 32; i++)
            if (layers.GetArrayElementAtIndex(i).stringValue == name) return i;
        for (int i = 8; i < 32; i++)
        {
            var p = layers.GetArrayElementAtIndex(i);
            if (string.IsNullOrEmpty(p.stringValue))
            {
                p.stringValue = name;
                tagManager.ApplyModifiedProperties();
                return i;
            }
        }
        throw new Exception("No free layer for " + name);
    }

    // ------------------------------------------------------------------ PARIS (Merge cube)

    static GameObject BuildParis(int layer, List<string> report)
    {
        currentLayer = layer;
        var root = new GameObject("Knickknack_Paris_EiffelTower");
        var content = Empty("Content", root.transform, Vector3.zero);

        var target = root.AddComponent<KnickknackTarget>();
        target.targetName = "Merge";
        target.content = content;

        // --- sides
        var sides = Empty("Sides", content.transform, Vector3.zero).transform;
        var back = Unlit("Paris_Panel", new Color(0.06f, 0.08f, 0.2f));
        var border = Unlit("Paris_PanelBorder", new Color(0.85f, 0.68f, 0.3f));
        Side(sides, "Front_Name", Vector3.forward, "<b>Eiffel Tower</b>\n<size=55%>La Tour Eiffel\nParis, France</size>", back, border);
        var weatherText = Side(sides, "Right_Weather", Vector3.right, "Weather", back, border);
        var timeText = Side(sides, "Back_Time", Vector3.back, "Time", back, border);
        Side(sides, "Left_Facts", Vector3.left,
            "<b>Did you know?</b>\n<size=60%>330 m tall\nBuilt 1887-1889 by Gustave Eiffel's company\nfor the 1889 World's Fair\nRepainted every 7 years\n(60 tonnes of paint!)</size>", back, border);

        var weather = root.AddComponent<KnickknackWeather>();
        weather.text = weatherText; weather.placeName = "Paris";
        weather.latitude = 48.8584; weather.longitude = 2.2945;
        weather.unit = KnickknackWeather.TemperatureUnit.Fahrenheit;

        var clock = root.AddComponent<KnickknackClock>();
        clock.text = timeText; clock.placeName = "Paris"; clock.ianaTimezone = "Europe/Paris";
        clock.style = KnickknackClock.ClockStyle.TwelveHour;
        clock.weatherFallback = weather; clock.fallbackUtcOffsetHours = 1f;

        // --- plinth: Champ de Mars lawn
        var models = Empty("Models", content.transform, Vector3.zero).transform;
        Prim(PrimitiveType.Cylinder, "Plinth_Stone", models, new Vector3(0, 0.0525f, 0), new Vector3(0.116f, 0.0025f, 0.116f), Mat("Paris_Stone", new Color(0.78f, 0.74f, 0.66f)));
        Prim(PrimitiveType.Cylinder, "Plinth_Lawn", models, new Vector3(0, 0.0545f, 0), new Vector3(0.108f, 0.0015f, 0.108f), Mat("Paris_Lawn", new Color(0.3f, 0.55f, 0.24f)));
        Prim(PrimitiveType.Cube, "Path", models, new Vector3(0, Top + 0.0002f, 0.03f), new Vector3(0.014f, 0.0006f, 0.05f), Mat("Paris_Gravel", new Color(0.83f, 0.76f, 0.6f)));

        // --- my models
        var tower = SavePrefab(BuildEiffelTower(), "MyModel1_EiffelTower");
        Place(tower, models, new Vector3(0, Top, 0), Quaternion.Euler(0, 45, 0));
        var lamp = SavePrefab(BuildStreetLamp(), "MyModel2_ParisStreetLamp");
        Vector3 lampPos = new Vector3(-0.036f, Top, 0.03f);
        Place(lamp, models, lampPos, Quaternion.identity);

        // --- web models
        PlaceWebModels(models, Root + "/WebModels/Paris", new[]
        {
            new Slot(new Vector3(0.034f, Top, 0.032f), 0.026f, Motion.Spin, 0),
            new Slot(new Vector3(0.036f, Top, -0.03f), 0.03f, Motion.None, 0),
            new Slot(new Vector3(0, Top + 0.07f, 0), 0.018f, Motion.Orbit, 0.045f),
        }, "Paris", report);

        // --- lighting
        var lighting = Empty("Lighting", content.transform, Vector3.zero).transform;
        var day = Empty("Style_Day", lighting, Vector3.zero);
        DirLight(day.transform, "Sun", new Vector3(45, -35, 0), new Color(1f, 0.95f, 0.85f), 1.15f, LightShadows.Hard);
        DirLight(day.transform, "SkyFill", new Vector3(20, 150, 0), new Color(0.65f, 0.75f, 1f), 0.35f, LightShadows.None);

        var night = Empty("Style_Night", lighting, Vector3.zero);
        DirLight(night.transform, "Moonlight", new Vector3(55, 160, 0), new Color(0.45f, 0.55f, 1f), 0.3f, LightShadows.None);
        PointLight(night.transform, "TowerGoldGlow", new Vector3(0, Top + 0.04f, 0.02f), new Color(1f, 0.72f, 0.35f), 2.2f, 0.12f);
        PointLight(night.transform, "LampLight", lampPos + new Vector3(0, 0.04f, 0.004f), new Color(1f, 0.8f, 0.5f), 1.2f, 0.05f);
        var beaconPivot = Empty("BeaconPivot", night.transform, new Vector3(0, Top + 0.1f, 0));
        beaconPivot.AddComponent<KnickknackSpin>().degreesPerSecond = new Vector3(0, 90, 0);
        var beacon = new GameObject("Beacon");
        beacon.transform.SetParent(beaconPivot.transform, false);
        beacon.transform.localRotation = Quaternion.Euler(12, 0, 0);
        var bl = beacon.AddComponent<Light>();
        bl.type = LightType.Spot; bl.color = new Color(1f, 0.95f, 0.8f); bl.intensity = 3f; bl.range = 0.4f; bl.spotAngle = 14f;
        bl.cullingMask = 1 << currentLayer;
        Prim(PrimitiveType.Sphere, "BeaconBulb", beaconPivot.transform, Vector3.zero, Vector3.one * 0.0035f, Mat("Paris_BeaconGlow", Color.white, 0, 0.5f, new Color(2f, 1.9f, 1.6f)));
        // the famous hourly sparkle
        var sparkleMat = Mat("Paris_Sparkle", Color.white, 0, 0.5f, new Color(3f, 3f, 3f));
        var rnd = new System.Random(42);
        for (int i = 0; i < 22; i++)
        {
            float y = (float)rnd.NextDouble() * 0.095f;
            float half = EiffelHalfWidth(y);
            int face = rnd.Next(4);
            float along = ((float)rnd.NextDouble() * 2 - 1) * half;
            Vector3 p = face switch
            {
                0 => new Vector3(along, y, half), 1 => new Vector3(along, y, -half),
                2 => new Vector3(half, y, along), _ => new Vector3(-half, y, along)
            };
            p = Quaternion.Euler(0, 45, 0) * p + new Vector3(0, Top, 0);
            var s = Prim(PrimitiveType.Sphere, "Sparkle" + i, night.transform, p, Vector3.one * 0.0022f, sparkleMat);
            s.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            s.AddComponent<KnickknackTwinkle>();
        }

        var flip = root.AddComponent<KnickknackFlipLighting>();
        flip.styleA = new[] { day }; flip.styleB = new[] { night };
        flip.styleAName = "Sunny afternoon"; flip.styleBName = "Paris by night (sparkle)";
        night.SetActive(false);

        // --- sound
        target.ambientAudio = AddAmbient(root, EnsureSound(Root + "/Sounds/Paris", "Generated_ParisMusicBox", MusicBox, report));

        SetLayerRecursive(root, layer);
        return root;
    }

    static float EiffelHalfWidth(float y)
    {
        // matches the leg geometry below (half width of the tower at height y)
        if (y < 0.03f) return Mathf.Lerp(0.024f, 0.012f, y / 0.03f);
        if (y < 0.062f) return Mathf.Lerp(0.012f, 0.0055f, (y - 0.03f) / 0.032f);
        return Mathf.Lerp(0.0055f, 0.0018f, Mathf.Clamp01((y - 0.062f) / 0.035f));
    }

    static GameObject BuildEiffelTower()
    {
        var root = new GameObject("MyModel1_EiffelTower");
        var t = root.transform;
        var iron = Mat("Eiffel_Iron", new Color(0.45f, 0.33f, 0.23f), 0.55f, 0.35f);
        var deck = Mat("Eiffel_Deck", new Color(0.36f, 0.26f, 0.18f), 0.4f, 0.3f);

        int[] sx = { 1, 1, -1, -1 }, sz = { 1, -1, -1, 1 };
        for (int i = 0; i < 4; i++)
        {
            // lower legs, middle section and upper mast
            Beam(t, "LegLower" + i, new Vector3(sx[i] * 0.024f, 0, sz[i] * 0.024f), new Vector3(sx[i] * 0.012f, 0.03f, sz[i] * 0.012f), 0.0055f, iron);
            Beam(t, "LegLowerInner" + i, new Vector3(sx[i] * 0.019f, 0, sz[i] * 0.019f), new Vector3(sx[i] * 0.0095f, 0.03f, sz[i] * 0.0095f), 0.0025f, iron);
            Beam(t, "LegMid" + i, new Vector3(sx[i] * 0.012f, 0.03f, sz[i] * 0.012f), new Vector3(sx[i] * 0.0055f, 0.062f, sz[i] * 0.0055f), 0.0038f, iron);
            Beam(t, "LegUpper" + i, new Vector3(sx[i] * 0.0055f, 0.062f, sz[i] * 0.0055f), new Vector3(sx[i] * 0.0018f, 0.097f, sz[i] * 0.0018f), 0.0026f, iron);
        }
        // arches between the legs and horizontal bracing rings
        Vector3[] faceDir = { Vector3.forward, Vector3.right, Vector3.back, Vector3.left };
        foreach (var n in faceDir)
        {
            Vector3 side = Vector3.Cross(Vector3.up, n);
            float h0 = EiffelHalfWidth(0.006f), h1 = EiffelHalfWidth(0.016f);
            Vector3 a = n * h0 + side * h0 * 0.85f + Vector3.up * 0.006f;
            Vector3 m = n * h1 + Vector3.up * 0.016f;
            Vector3 b = n * h0 - side * h0 * 0.85f + Vector3.up * 0.006f;
            Beam(t, "Arch", a, m, 0.0018f, iron);
            Beam(t, "Arch", m, b, 0.0018f, iron);
            foreach (float y in new[] { 0.045f, 0.08f })
            {
                float h = EiffelHalfWidth(y);
                Beam(t, "Brace", n * h + side * h + Vector3.up * y, n * h - side * h + Vector3.up * y, 0.0014f, iron);
            }
            // X lattice on the middle section
            float ya = 0.032f, yb = 0.06f, ha = EiffelHalfWidth(ya), hb = EiffelHalfWidth(yb);
            Beam(t, "Lattice", n * ha + side * ha + Vector3.up * ya, n * hb - side * hb + Vector3.up * yb, 0.001f, iron);
            Beam(t, "Lattice", n * ha - side * ha + Vector3.up * ya, n * hb + side * hb + Vector3.up * yb, 0.001f, iron);
        }
        Prim(PrimitiveType.Cube, "Platform1", t, new Vector3(0, 0.03f, 0), new Vector3(0.032f, 0.0032f, 0.032f), deck);
        Prim(PrimitiveType.Cube, "Platform2", t, new Vector3(0, 0.062f, 0), new Vector3(0.016f, 0.0026f, 0.016f), deck);
        Prim(PrimitiveType.Cube, "TopDeck", t, new Vector3(0, 0.098f, 0), new Vector3(0.0062f, 0.004f, 0.0062f), deck);
        Prim(PrimitiveType.Cylinder, "Antenna", t, new Vector3(0, 0.106f, 0), new Vector3(0.0014f, 0.006f, 0.0014f), iron);
        return root;
    }

    static GameObject BuildStreetLamp()
    {
        var root = new GameObject("MyModel2_ParisStreetLamp");
        var t = root.transform;
        var metal = Mat("Lamp_Metal", new Color(0.08f, 0.14f, 0.1f), 0.6f, 0.5f);
        var glass = Mat("Lamp_Glass", new Color(1f, 0.9f, 0.65f), 0, 0.8f, new Color(1.2f, 0.85f, 0.4f));
        Prim(PrimitiveType.Cylinder, "Base", t, new Vector3(0, 0.002f, 0), new Vector3(0.008f, 0.002f, 0.008f), metal);
        Prim(PrimitiveType.Cylinder, "BaseRing", t, new Vector3(0, 0.0055f, 0), new Vector3(0.0055f, 0.0015f, 0.0055f), metal);
        Prim(PrimitiveType.Cylinder, "Pole", t, new Vector3(0, 0.02f, 0), new Vector3(0.0026f, 0.017f, 0.0026f), metal);
        Prim(PrimitiveType.Cylinder, "Collar", t, new Vector3(0, 0.035f, 0), new Vector3(0.0045f, 0.001f, 0.0045f), metal);
        Prim(PrimitiveType.Cube, "Lantern", t, new Vector3(0, 0.0405f, 0), new Vector3(0.0075f, 0.009f, 0.0075f), glass, new Vector3(0, 45, 0));
        for (int i = 0; i < 4; i++)
        {
            var a = Quaternion.Euler(0, 45 + 90 * i, 0) * new Vector3(0.0053f, 0, 0);
            Prim(PrimitiveType.Cube, "Frame" + i, t, new Vector3(a.x, 0.0405f, a.z), new Vector3(0.001f, 0.0095f, 0.001f), metal);
        }
        Prim(PrimitiveType.Cylinder, "Cap", t, new Vector3(0, 0.0458f, 0), new Vector3(0.011f, 0.0012f, 0.011f), metal);
        Prim(PrimitiveType.Cylinder, "Roof", t, new Vector3(0, 0.0478f, 0), new Vector3(0.006f, 0.0012f, 0.006f), metal);
        Prim(PrimitiveType.Sphere, "Finial", t, new Vector3(0, 0.0505f, 0), Vector3.one * 0.0028f, metal);
        return root;
    }

    // ------------------------------------------------------------------ BELÉM (Class cube)

    static GameObject BuildBelem(int layer, List<string> report)
    {
        currentLayer = layer;
        var root = new GameObject("Knickknack_Lisbon_BelemTower");
        var content = Empty("Content", root.transform, Vector3.zero);

        var target = root.AddComponent<KnickknackTarget>();
        target.targetName = "ClassCube";
        target.content = content;

        var sides = Empty("Sides", content.transform, Vector3.zero).transform;
        var back = Unlit("Belem_Panel", new Color(0.02f, 0.16f, 0.38f));
        var border = Unlit("Belem_PanelBorder", new Color(0.93f, 0.93f, 0.97f));
        Side(sides, "Front_Name", Vector3.forward, "<b>Belém Tower</b>\n<size=55%>Torre de Belém\nLisbon, Portugal</size>", back, border);
        var weatherText = Side(sides, "Right_Weather", Vector3.right, "Weather", back, border);
        var timeText = Side(sides, "Back_Time", Vector3.back, "Time", back, border);
        Side(sides, "Left_Facts", Vector3.left,
            "<b>Did you know?</b>\n<size=60%>Built 1514-1519 to guard the Tagus river\nManueline style\nShips of the Age of Discovery sailed from here\nUNESCO World Heritage since 1983</size>", back, border);

        var weather = root.AddComponent<KnickknackWeather>();
        weather.text = weatherText; weather.placeName = "Lisbon";
        weather.latitude = 38.6916; weather.longitude = -9.2160;
        weather.unit = KnickknackWeather.TemperatureUnit.Celsius;

        var clock = root.AddComponent<KnickknackClock>();
        clock.text = timeText; clock.placeName = "Lisbon"; clock.ianaTimezone = "Europe/Lisbon";
        clock.style = KnickknackClock.ClockStyle.TwentyFourHour;
        clock.weatherFallback = weather; clock.fallbackUtcOffsetHours = 0f;

        var models = Empty("Models", content.transform, Vector3.zero).transform;
        Prim(PrimitiveType.Cylinder, "Plinth_Stone", models, new Vector3(0, 0.0525f, 0), new Vector3(0.116f, 0.0025f, 0.116f), Mat("Belem_Quay", new Color(0.7f, 0.68f, 0.62f)));
        Prim(PrimitiveType.Cylinder, "Plinth_TagusRiver", models, new Vector3(0, 0.0545f, 0), new Vector3(0.108f, 0.0015f, 0.108f), Mat("Belem_Water", new Color(0.1f, 0.33f, 0.5f), 0.1f, 0.9f));
        Prim(PrimitiveType.Cube, "Pier", models, new Vector3(-0.012f, Top + 0.001f, -0.012f), new Vector3(0.05f, 0.002f, 0.046f), Mat("Belem_PierStone", new Color(0.62f, 0.6f, 0.55f)));

        var tower = SavePrefab(BuildBelemTower(), "MyModel3_BelemTower");
        Place(tower, models, new Vector3(-0.012f, Top + 0.002f, -0.012f), Quaternion.Euler(0, 20, 0));
        var caravel = SavePrefab(BuildCaravel(), "MyModel4_Caravel");
        var boat = Place(caravel, models, new Vector3(0.03f, Top, 0.026f), Quaternion.Euler(0, -30, 0));
        boat.AddComponent<KnickknackBob>();

        PlaceWebModels(models, Root + "/WebModels/Belem", new[]
        {
            new Slot(new Vector3(-0.012f, Top + 0.085f, -0.012f), 0.016f, Motion.Orbit, 0.045f),
            new Slot(new Vector3(-0.035f, Top + 0.002f, 0.03f), 0.02f, Motion.Spin, 0),
            new Slot(new Vector3(0.035f, Top, -0.032f), 0.026f, Motion.None, 0),
        }, "Belem", report);

        var lighting = Empty("Lighting", content.transform, Vector3.zero).transform;
        var day = Empty("Style_SunnyDay", lighting, Vector3.zero);
        DirLight(day.transform, "Sun", new Vector3(60, 30, 0), new Color(1f, 0.98f, 0.93f), 1.25f, LightShadows.Hard);
        DirLight(day.transform, "SkyFill", new Vector3(25, -150, 0), new Color(0.6f, 0.8f, 1f), 0.35f, LightShadows.None);

        var sunset = Empty("Style_Sunset", lighting, Vector3.zero);
        DirLight(sunset.transform, "LowSun", new Vector3(10, -80, 0), new Color(1f, 0.5f, 0.25f), 1.1f, LightShadows.Hard);
        DirLight(sunset.transform, "DuskFill", new Vector3(30, 100, 0), new Color(0.55f, 0.35f, 0.6f), 0.3f, LightShadows.None);
        var lanternMat = Mat("Belem_LanternGlow", new Color(1f, 0.8f, 0.5f), 0, 0.5f, new Color(1.5f, 0.9f, 0.4f));
        var towerRot = Quaternion.Euler(0, 20, 0);
        Vector3 towerOrigin = new Vector3(-0.012f, Top + 0.002f, -0.012f);
        foreach (var p in BelemGaritaPositions())
        {
            Vector3 wp = towerOrigin + towerRot * (p + new Vector3(0, 0.004f, 0));
            PointLight(sunset.transform, "Lantern", wp, new Color(1f, 0.7f, 0.35f), 0.8f, 0.03f);
            var g = Prim(PrimitiveType.Sphere, "LanternGlow", sunset.transform, wp, Vector3.one * 0.0018f, lanternMat);
            g.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }
        PointLight(sunset.transform, "BoatLantern", new Vector3(0.03f, Top + 0.012f, 0.026f), new Color(1f, 0.75f, 0.4f), 0.8f, 0.04f);

        var flip = root.AddComponent<KnickknackFlipLighting>();
        flip.styleA = new[] { day }; flip.styleB = new[] { sunset };
        flip.styleAName = "Sunny Lisbon day"; flip.styleBName = "Sunset over the Tagus";
        sunset.SetActive(false);

        target.ambientAudio = AddAmbient(root, EnsureSound(Root + "/Sounds/Belem", "Generated_TagusWaves", Waves, report));

        SetLayerRecursive(root, layer);
        return root;
    }

    static IEnumerable<Vector3> BelemGaritaPositions()
    {
        // the little domed watchtowers on the bastion corners
        yield return new Vector3(0.0205f, 0.018f, -0.012f);
        yield return new Vector3(-0.0205f, 0.018f, -0.012f);
        yield return new Vector3(0.0205f, 0.018f, 0.012f);
        yield return new Vector3(-0.0205f, 0.018f, 0.012f);
    }

    static GameObject BuildBelemTower()
    {
        var root = new GameObject("MyModel3_BelemTower");
        var t = root.transform;
        var stone = Mat("Belem_Limestone", new Color(0.9f, 0.86f, 0.76f), 0, 0.2f);
        var stoneDark = Mat("Belem_LimestoneShade", new Color(0.78f, 0.73f, 0.63f), 0, 0.15f);
        var window = Mat("Belem_Window", new Color(0.12f, 0.1f, 0.09f), 0, 0.1f);
        var cross = Mat("Belem_CrossRed", new Color(0.7f, 0.1f, 0.1f));

        // bastion (the lower fortress) with its pointed prow towards the river
        Prim(PrimitiveType.Cube, "Bastion", t, new Vector3(0, 0.009f, 0), new Vector3(0.042f, 0.018f, 0.026f), stone);
        Prim(PrimitiveType.Cube, "BastionProw", t, new Vector3(0, 0.009f, 0.013f), new Vector3(0.0297f, 0.018f, 0.0297f), stone, new Vector3(0, 45, 0));
        Prim(PrimitiveType.Cube, "BastionBand", t, new Vector3(0, 0.0165f, 0), new Vector3(0.0435f, 0.0012f, 0.0275f), stoneDark);
        // merlons with the red Cross of the Order of Christ shields
        for (int i = 0; i < 7; i++)
        {
            float x = -0.018f + i * 0.006f;
            Prim(PrimitiveType.Cube, "MerlonBack", t, new Vector3(x, 0.0195f, -0.0125f), new Vector3(0.0032f, 0.003f, 0.0016f), stone);
        }
        for (int s = -1; s <= 1; s += 2)
            for (int i = 0; i < 4; i++)
            {
                float z = -0.008f + i * 0.0065f;
                Prim(PrimitiveType.Cube, "MerlonSide", t, new Vector3(s * 0.0205f, 0.0195f, z), new Vector3(0.0016f, 0.003f, 0.0032f), stone);
                Prim(PrimitiveType.Cube, "Shield", t, new Vector3(s * 0.0214f, 0.0195f, z), new Vector3(0.0003f, 0.0016f, 0.0016f), cross);
            }
        for (int i = 0; i < 5; i++)
        {
            float k = -0.0105f + i * 0.0052f;
            Vector3 p = new Vector3(k, 0.0195f, 0.0325f - Mathf.Abs(k));
            Prim(PrimitiveType.Cube, "MerlonProw", t, p, new Vector3(0.0028f, 0.003f, 0.0016f), stone, new Vector3(0, Mathf.Abs(k) < 0.001f ? 0 : (k < 0 ? -45 : 45), 0));
        }
        // gun ports along the bastion
        for (int i = 0; i < 4; i++)
            Prim(PrimitiveType.Cube, "GunPort", t, new Vector3(-0.0135f + i * 0.009f, 0.008f, -0.0131f), new Vector3(0.0035f, 0.0035f, 0.0004f), window);
        foreach (var p in BelemGaritaPositions())
        {
            Prim(PrimitiveType.Cylinder, "Garita", t, p + new Vector3(0, 0.004f, 0), new Vector3(0.0048f, 0.004f, 0.0048f), stone);
            Prim(PrimitiveType.Sphere, "GaritaDome", t, p + new Vector3(0, 0.0085f, 0), new Vector3(0.0052f, 0.004f, 0.0052f), stone);
            Prim(PrimitiveType.Sphere, "GaritaFinial", t, p + new Vector3(0, 0.0112f, 0), Vector3.one * 0.0012f, stone);
        }

        // the main tower, standing at the back of the bastion
        const float tz = -0.004f, tw = 0.021f;
        Prim(PrimitiveType.Cube, "Tower", t, new Vector3(0, 0.04f, tz), new Vector3(tw, 0.044f, tw), stone);
        Prim(PrimitiveType.Cube, "TowerBand1", t, new Vector3(0, 0.044f, tz), new Vector3(tw + 0.001f, 0.001f, tw + 0.001f), stoneDark);
        Prim(PrimitiveType.Cube, "TowerBand2", t, new Vector3(0, 0.061f, tz), new Vector3(tw + 0.0012f, 0.0012f, tw + 0.0012f), stoneDark);
        float face = tz + tw / 2f;
        // Renaissance loggia / balcony facing the river
        Prim(PrimitiveType.Cube, "Loggia", t, new Vector3(0, 0.05f, face + 0.0003f), new Vector3(0.014f, 0.0065f, 0.0006f), window);
        Prim(PrimitiveType.Cube, "LoggiaFloor", t, new Vector3(0, 0.0465f, face + 0.002f), new Vector3(0.016f, 0.001f, 0.004f), stone);
        for (int i = 0; i < 4; i++)
            Prim(PrimitiveType.Cube, "LoggiaColumn", t, new Vector3(-0.0055f + i * 0.0037f, 0.05f, face + 0.0035f), new Vector3(0.0008f, 0.0065f, 0.0008f), stone);
        Prim(PrimitiveType.Cube, "LoggiaRoof", t, new Vector3(0, 0.0538f, face + 0.002f), new Vector3(0.016f, 0.0009f, 0.0045f), stone);
        // windows on the tower
        Prim(PrimitiveType.Cube, "Window1", t, new Vector3(0, 0.032f, face + 0.0003f), new Vector3(0.004f, 0.006f, 0.0006f), window);
        Prim(PrimitiveType.Cube, "Window2", t, new Vector3(0, 0.057f, face + 0.0003f), new Vector3(0.003f, 0.0035f, 0.0006f), window);
        for (int s = -1; s <= 1; s += 2)
        {
            Prim(PrimitiveType.Cube, "SideWindow", t, new Vector3(s * (tw / 2 + 0.0003f), 0.05f, tz), new Vector3(0.0006f, 0.005f, 0.004f), window);
            Prim(PrimitiveType.Cube, "SideWindowLow", t, new Vector3(s * (tw / 2 + 0.0003f), 0.033f, tz), new Vector3(0.0006f, 0.004f, 0.003f), window);
        }
        // tower terrace merlons and corner garitas
        for (int i = 0; i < 4; i++)
        {
            float k = -0.0075f + i * 0.005f;
            Prim(PrimitiveType.Cube, "TowerMerlon", t, new Vector3(k, 0.0635f, face - 0.0008f), new Vector3(0.0026f, 0.003f, 0.0014f), stone);
            Prim(PrimitiveType.Cube, "TowerMerlon", t, new Vector3(k, 0.0635f, tz - tw / 2 + 0.0008f), new Vector3(0.0026f, 0.003f, 0.0014f), stone);
            Prim(PrimitiveType.Cube, "TowerMerlon", t, new Vector3(tw / 2 - 0.0008f, 0.0635f, tz + k), new Vector3(0.0014f, 0.003f, 0.0026f), stone);
            Prim(PrimitiveType.Cube, "TowerMerlon", t, new Vector3(-tw / 2 + 0.0008f, 0.0635f, tz + k), new Vector3(0.0014f, 0.003f, 0.0026f), stone);
        }
        for (int a = -1; a <= 1; a += 2)
            for (int b = -1; b <= 1; b += 2)
            {
                Vector3 c = new Vector3(a * tw / 2, 0.0635f, tz + b * tw / 2);
                Prim(PrimitiveType.Cylinder, "TowerGarita", t, c, new Vector3(0.0034f, 0.0028f, 0.0034f), stone);
                Prim(PrimitiveType.Sphere, "TowerGaritaDome", t, c + new Vector3(0, 0.003f, 0), new Vector3(0.0036f, 0.003f, 0.0036f), stone);
            }
        // top storey
        Prim(PrimitiveType.Cube, "TopStorey", t, new Vector3(0, 0.0665f, tz), new Vector3(0.012f, 0.009f, 0.012f), stone);
        Prim(PrimitiveType.Cube, "TopRoof", t, new Vector3(0, 0.0715f, tz), new Vector3(0.0135f, 0.001f, 0.0135f), stoneDark);
        Prim(PrimitiveType.Cube, "TopWindow", t, new Vector3(0, 0.067f, tz + 0.0062f), new Vector3(0.003f, 0.004f, 0.0004f), window);
        return root;
    }

    static GameObject BuildCaravel()
    {
        var root = new GameObject("MyModel4_Caravel");
        var t = root.transform;
        var wood = Mat("Caravel_Wood", new Color(0.42f, 0.26f, 0.14f), 0, 0.25f);
        var woodDark = Mat("Caravel_WoodDark", new Color(0.26f, 0.16f, 0.09f), 0, 0.2f);
        var sail = Mat("Caravel_Sail", new Color(0.96f, 0.93f, 0.85f), 0, 0.1f);
        var red = Mat("Belem_CrossRed", new Color(0.7f, 0.1f, 0.1f));

        // hull: lower half is hidden in the "river" plinth
        Prim(PrimitiveType.Sphere, "Hull", t, new Vector3(0, 0.001f, 0), new Vector3(0.013f, 0.011f, 0.036f), wood);
        Prim(PrimitiveType.Cube, "Deck", t, new Vector3(0, 0.0058f, -0.001f), new Vector3(0.0105f, 0.0012f, 0.028f), woodDark);
        Prim(PrimitiveType.Cube, "Sterncastle", t, new Vector3(0, 0.0085f, -0.011f), new Vector3(0.0105f, 0.0045f, 0.008f), wood);
        Prim(PrimitiveType.Cube, "SterncastleRail", t, new Vector3(0, 0.0112f, -0.011f), new Vector3(0.011f, 0.0008f, 0.0085f), woodDark);
        Prim(PrimitiveType.Cube, "Bowsprit", t, new Vector3(0, 0.008f, 0.019f), new Vector3(0.0009f, 0.0009f, 0.01f), woodDark, new Vector3(-15, 0, 0));

        // two masts with lateen sails bearing the red cross
        var masts = new[] { (z: 0.006f, h: 0.026f, sw: 0.016f, sh: 0.018f), (z: -0.007f, h: 0.02f, sw: 0.012f, sh: 0.014f) };
        foreach (var m in masts)
        {
            Prim(PrimitiveType.Cylinder, "Mast", t, new Vector3(0, 0.006f + m.h / 2, m.z), new Vector3(0.0011f, m.h / 2, 0.0011f), woodDark);
            var sailPivot = Empty("SailPivot", t, new Vector3(0.0009f, 0.008f + m.sh * 0.55f, m.z));
            sailPivot.transform.localRotation = Quaternion.Euler(-28, 0, 0);
            Prim(PrimitiveType.Cube, "Sail", sailPivot.transform, Vector3.zero, new Vector3(0.0004f, m.sh, m.sw * 0.7f), sail);
            Prim(PrimitiveType.Cube, "Yard", sailPivot.transform, new Vector3(0, m.sh / 2, 0), new Vector3(0.0008f, 0.0008f, m.sw * 0.75f), woodDark);
            Prim(PrimitiveType.Cube, "CrossV", sailPivot.transform, new Vector3(0.00035f, 0, 0), new Vector3(0.0003f, m.sh * 0.55f, 0.0018f), red);
            Prim(PrimitiveType.Cube, "CrossH", sailPivot.transform, new Vector3(0.00035f, m.sh * 0.08f, 0), new Vector3(0.0003f, 0.0018f, m.sw * 0.45f), red);
            Prim(PrimitiveType.Cube, "Pennant", t, new Vector3(0, 0.006f + m.h + 0.001f, m.z - 0.002f), new Vector3(0.0003f, 0.0016f, 0.004f), red);
        }
        return root;
    }

    // ------------------------------------------------------------------ helpers: geometry

    static GameObject Empty(string name, Transform parent, Vector3 localPos)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        return go;
    }

    static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material mat, Vector3? euler = null)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        if (euler.HasValue) go.transform.localEulerAngles = euler.Value;
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.lightProbeUsage = LightProbeUsage.Off;
        r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return go;
    }

    static GameObject Beam(Transform parent, string name, Vector3 a, Vector3 b, float thickness, Material mat)
    {
        var go = Prim(PrimitiveType.Cube, name, parent, (a + b) * 0.5f, new Vector3(thickness, (b - a).magnitude, thickness), mat);
        go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, (b - a).normalized);
        return go;
    }

    static GameObject SavePrefab(GameObject go, string name)
    {
        string path = $"{Root}/MyModels/{name}.prefab";
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        return prefab;
    }

    static GameObject Place(GameObject prefab, Transform parent, Vector3 pos, Quaternion rot)
    {
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        inst.transform.SetParent(parent, false);
        inst.transform.localPosition = pos;
        inst.transform.localRotation = rot;
        return inst;
    }

    static TextMeshPro Side(Transform parent, string name, Vector3 normal, string text, Material backing, Material border)
    {
        var side = Empty(name, parent, Vector3.zero).transform;
        var rot = Quaternion.LookRotation(-normal, Vector3.up);

        var b = Prim(PrimitiveType.Quad, "Border", side, normal * 0.0502f, Vector3.one * 0.1f, border);
        b.transform.localRotation = rot;
        b.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        var q = Prim(PrimitiveType.Quad, "Backing", side, normal * 0.0504f, Vector3.one * 0.093f, backing);
        q.transform.localRotation = rot;
        q.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

        var tgo = new GameObject("Text");
        tgo.transform.SetParent(side, false);
        tgo.transform.localPosition = normal * 0.0507f;
        tgo.transform.localRotation = rot;
        tgo.transform.localScale = Vector3.one * 0.005f;
        var tmp = tgo.AddComponent<TextMeshPro>();
        tmp.rectTransform.sizeDelta = new Vector2(16.5f, 16.5f); // 0.0825 m, inside the 0.093 m backing
        tmp.text = text;
        tmp.enableWordWrapping = true;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 2f;
        tmp.fontSizeMax = 28f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.overflowMode = TextOverflowModes.Truncate;
        tmp.color = Color.white;
        return tmp;
    }

    static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform c in go.transform) SetLayerRecursive(c.gameObject, layer);
    }

    // ------------------------------------------------------------------ helpers: materials & lights

    static Material Mat(string name, Color c, float metallic = 0f, float smoothness = 0.3f, Color? emission = null)
    {
        string path = $"{Root}/Materials/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.color = c;
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Glossiness", smoothness);
        if (emission.HasValue)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission.Value);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        else
        {
            m.DisableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", Color.black);
        }
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material Unlit(string name, Color c)
    {
        string path = $"{Root}/Materials/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Unlit/Color"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.color = c;
        EditorUtility.SetDirty(m);
        return m;
    }

    static Light DirLight(Transform parent, string name, Vector3 euler, Color c, float intensity, LightShadows shadows)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localEulerAngles = euler;
        var l = go.AddComponent<Light>();
        l.type = LightType.Directional;
        l.color = c; l.intensity = intensity; l.shadows = shadows;
        l.shadowStrength = 0.7f;
        l.renderMode = LightRenderMode.ForcePixel;
        l.cullingMask = 1 << currentLayer;
        return l;
    }

    static Light PointLight(Transform parent, string name, Vector3 pos, Color c, float intensity, float range)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = c; l.intensity = intensity; l.range = range;
        l.shadows = LightShadows.None;
        l.cullingMask = 1 << currentLayer;
        return l;
    }

    // ------------------------------------------------------------------ web models

    enum Motion { None, Spin, Orbit }

    struct Slot
    {
        public Vector3 pos; public float size; public Motion motion; public float radius;
        public Slot(Vector3 p, float s, Motion m, float r) { pos = p; size = s; motion = m; radius = r; }
    }

    static void PlaceWebModels(Transform parent, string folder, Slot[] slots, string label, List<string> report)
    {
        var paths = AssetDatabase.FindAssets("t:GameObject", new[] { folder })
            .Select(AssetDatabase.GUIDToAssetPath).Distinct().OrderBy(p => p).ToList();

        for (int i = 0; i < slots.Length; i++)
        {
            if (i >= paths.Count)
            {
                report.Add($"{label}: web model slot {i + 1} is empty (put a .fbx/.obj/.dae/.prefab in {folder}).");
                continue;
            }
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(paths[i]);
            var slot = slots[i];
            var holder = Empty($"WebModel{i + 1}_{asset.name}", parent, slot.pos);
            Transform mount = holder.transform;
            if (slot.motion == Motion.Orbit)
            {
                holder.AddComponent<KnickknackOrbit>();
                mount = Empty("Offset", holder.transform, new Vector3(slot.radius, 0, 0)).transform;
            }
            else if (slot.motion == Motion.Spin)
            {
                holder.AddComponent<KnickknackSpin>().degreesPerSecond = new Vector3(0, 25, 0);
            }

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            inst.transform.SetParent(mount, false);
            FitInto(inst, mount, slot.size);
            foreach (var col in inst.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(col);
            report.Add($"{label}: web model {i + 1} = {asset.name}");
        }
    }

    static void FitInto(GameObject inst, Transform mount, float size)
    {
        inst.transform.localPosition = Vector3.zero;
        inst.transform.localScale = Vector3.one;
        var rs = inst.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return;
        Bounds b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        float max = Mathf.Max(b.size.x, b.size.y, b.size.z);
        if (max <= 0f) return;
        inst.transform.localScale = Vector3.one * (size / max);

        b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        Vector3 bottomCenter = new Vector3(b.center.x, b.min.y, b.center.z);
        inst.transform.position += mount.position - bottomCenter;
    }

    // ------------------------------------------------------------------ sound

    static AudioSource AddAmbient(GameObject root, AudioClip clip)
    {
        var src = root.AddComponent<AudioSource>();
        src.clip = clip;
        src.loop = true;
        src.playOnAwake = false;
        src.volume = 0.5f;
        src.spatialBlend = 0.6f;
        src.minDistance = 0.3f;
        src.maxDistance = 3f;
        src.rolloffMode = AudioRolloffMode.Linear;
        return src;
    }

    static AudioClip EnsureSound(string folder, string generatedName, Func<int, float[]> generator, List<string> report)
    {
        var clips = AssetDatabase.FindAssets("t:AudioClip", new[] { folder })
            .Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p.Contains("Generated_") ? 1 : 0).ThenBy(p => p).ToList();
        if (clips.Count > 0 && !clips[0].Contains("Generated_"))
        {
            report.Add("Ambient sound: " + Path.GetFileName(clips[0]));
            return AssetDatabase.LoadAssetAtPath<AudioClip>(clips[0]);
        }
        string path = $"{folder}/{generatedName}.wav";
        if (!File.Exists(path))
        {
            const int sr = 22050;
            WriteWav(path, generator(sr), sr);
            AssetDatabase.ImportAsset(path);
        }
        report.Add("Ambient sound: " + Path.GetFileName(path) + " (generated)");
        return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
    }

    static void WriteWav(string path, float[] samples, int sampleRate)
    {
        using (var fs = new FileStream(path, FileMode.Create))
        using (var w = new BinaryWriter(fs))
        {
            int dataBytes = samples.Length * 2;
            w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + dataBytes);
            w.Write(new[] { 'W', 'A', 'V', 'E' }); w.Write(new[] { 'f', 'm', 't', ' ' });
            w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(sampleRate); w.Write(sampleRate * 2); w.Write((short)2); w.Write((short)16);
            w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(dataBytes);
            foreach (var s in samples) w.Write((short)Mathf.Clamp(Mathf.RoundToInt(s * 32767f), -32768, 32767));
        }
    }

    static void Normalize(float[] a, float peak)
    {
        float m = 0f;
        foreach (var v in a) m = Mathf.Max(m, Mathf.Abs(v));
        if (m <= 0f) return;
        float k = peak / m;
        for (int i = 0; i < a.Length; i++) a[i] *= k;
    }

    // Gentle waves of the Tagus river: 24 s seamless loop.
    static float[] Waves(int sr)
    {
        int n = sr * 24, fade = sr * 2;
        var a = new float[n + fade];
        var rnd = new System.Random(7);
        float[] waveAmp = { 1f, 0.75f, 0.9f, 0.65f };
        float lp1 = 0, lp2 = 0, lp3 = 0;
        for (int i = 0; i < a.Length; i++)
        {
            float t = i / (float)sr;
            float white = (float)(rnd.NextDouble() * 2 - 1);
            lp1 += 0.015f * (white - lp1);
            lp2 += 0.2f * (white - lp2);
            lp3 += 0.2f * (lp2 - lp3);
            int k = (int)(t / 6f) % 4;
            float phase = (t % 6f) / 6f;
            float swell = Mathf.Pow(Mathf.Sin(Mathf.PI * phase), 2) * waveAmp[k];
            float foam = Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Clamp01((phase - 0.3f) / 0.7f)), 3) * waveAmp[k];
            a[i] = lp1 * 4f * (0.3f + 0.7f * swell) + lp3 * 0.5f * foam;
        }
        var outp = new float[n];
        for (int i = 0; i < n; i++)
        {
            if (i < fade) { float f = i / (float)fade; outp[i] = a[i] * f + a[n + i] * (1 - f); }
            else outp[i] = a[i];
        }
        Normalize(outp, 0.6f);
        return outp;
    }

    // A soft, slow music-box waltz (original chord arpeggios): 14.4 s seamless loop.
    static float[] MusicBox(int sr)
    {
        const float beat = 0.6f;
        int[][] chords = { new[] { 60, 64, 67 }, new[] { 57, 60, 64 }, new[] { 53, 57, 60 }, new[] { 55, 59, 62 } };
        int beats = chords.Length * 6;
        int n = Mathf.RoundToInt(sr * beat * beats);
        var a = new float[n];
        for (int c = 0; c < chords.Length; c++)
        {
            var ch = chords[c];
            int[] pattern = { ch[0] + 12, ch[1] + 12, ch[2] + 12, ch[0] + 24, ch[2] + 12, ch[1] + 12 };
            for (int b = 0; b < 6; b++)
            {
                int start = Mathf.RoundToInt(sr * beat * (c * 6 + b));
                AddNote(a, sr, start, pattern[b], b == 0 || b == 3 ? 0.9f : 0.6f, 2.2f);
                if (b == 0 || b == 3) AddNote(a, sr, start, ch[0] - 12 + (b == 3 ? 7 : 0), 0.45f, 1.5f);
            }
        }
        Normalize(a, 0.4f);
        return a;
    }

    static void AddNote(float[] a, int sr, int start, int midi, float amp, float decay)
    {
        float f = 440f * Mathf.Pow(2f, (midi - 69) / 12f);
        int len = sr * 3;
        for (int j = 0; j < len; j++)
        {
            float t = j / (float)sr;
            float env = (1f - Mathf.Exp(-t * 300f)) * Mathf.Exp(-t * decay);
            float v = Mathf.Sin(2 * Mathf.PI * f * t)
                      + 0.35f * Mathf.Sin(2 * Mathf.PI * 2f * f * t) * Mathf.Exp(-t * 3f)
                      + 0.12f * Mathf.Sin(2 * Mathf.PI * 3.01f * f * t) * Mathf.Exp(-t * 6f);
            a[(start + j) % a.Length] += amp * env * v;
        }
    }
}
