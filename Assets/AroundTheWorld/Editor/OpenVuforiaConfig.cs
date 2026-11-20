using UnityEditor;
using UnityEngine;

// Unity.exe -projectPath ... -executeMethod OpenVuforiaConfig.Open
// Opens the Vuforia Configuration (where the App License Key goes).
public static class OpenVuforiaConfig
{
    [MenuItem("Around The World/Open Vuforia License Key Settings")]
    public static void Open()
    {
        EditorApplication.delayCall += () =>
        {
            var cfg = AssetDatabase.LoadAssetAtPath<Object>("Assets/Resources/VuforiaConfiguration.asset");
            if (cfg != null)
            {
                Selection.activeObject = cfg;
                EditorGUIUtility.PingObject(cfg);
            }
            if (!EditorApplication.ExecuteMenuItem("Window/Vuforia Configuration"))
                EditorApplication.ExecuteMenuItem("Window/General/Inspector");
        };
    }
}
