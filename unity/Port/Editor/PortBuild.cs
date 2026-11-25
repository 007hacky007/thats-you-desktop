// Batch-mode build entry points for the port.
//
//   Unity -batchmode -quit -projectPath work/project -buildTarget OSXUniversal \
//         -executeMethod PortBuild.BuildMac -logFile work/reports/build-mac.log
//
// BuildMac builds every enabled scene of EditorBuildSettings (falling back to the original
// game's scene order when the list is empty) as a macOS Apple Silicon Mono development player
// at work/builds/mac/ThatsYou.app (override with -portBuildOut <path.app>), then:
//   * rebuilds the AssetBundles the game loads from StreamingAssets for the Mac target and puts
//     them over the PS4 copies inside the built player (see BuildAssetBundles);
//   * writes StreamingAssets/PortInputAxes.json, the InputManager axis table used by the
//     PortDebug scripted-input shim to answer GetButton/GetAxis for injected keys.
// Any failure exits the editor with code 1. Log lines are prefixed with [PortBuild].

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class PortBuild
{
    // Scene order of the original player (globalgamemanagers, level0..level14).
    static readonly string[] k_OriginalScenes =
    {
        "Assets/_Scene/LanguageSelect.unity",
        "Assets/_Scene/Loading.unity",
        "Assets/_Scene/Party-Master.unity",
        "Assets/_Scene/Scenarios/Home.unity",
        "Assets/_Scene/Scenarios/Scenario_01.unity",
        "Assets/_Scene/Scenarios/Scenario_02.unity",
        "Assets/_Scene/Scenarios/Scenario_05.unity",
        "Assets/_Scene/Scenarios/Scenario_03.unity",
        "Assets/_Scene/Scenarios/Scenario_04.unity",
        "Assets/_Scene/Scenarios/Scenario_06.unity",
        "Assets/_Scene/Scenarios/Scenario_07.unity",
        "Assets/_Scene/Scenarios/Scenario_08.unity",
        "Assets/_Scene/Scenarios/Scenario_09.unity",
        "Assets/_Scene/Scenarios/Scenario_10.unity",
        "Assets/_Scene/Scenarios/EmptyScene.unity",
    };

    const string k_AppName = "ThatsYou.app";

    static void Log(string msg) { Debug.Log("[PortBuild] " + msg); }

    static void Fail(string msg)
    {
        Debug.LogError("[PortBuild] FAILED: " + msg);
        if (Application.isBatchMode)
            EditorApplication.Exit(1);
        throw new BuildFailedException(msg);
    }

    static string ProjectRoot { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }

    static string Arg(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }

    public static string DefaultAppPath
    {
        get { return Path.GetFullPath(Path.Combine(ProjectRoot, "..", "builds", "mac", k_AppName)); }
    }

    static string AppPath { get { return Arg("-portBuildOut") ?? DefaultAppPath; } }

    // StreamingAssets folder inside a built macOS .app
    static string StreamingAssetsOf(string app)
    {
        return Path.Combine(app, "Contents", "Resources", "Data", "StreamingAssets");
    }

    public static string[] Scenes()
    {
        string[] enabled = EditorBuildSettings.scenes.Where(s => s.enabled && !string.IsNullOrEmpty(s.path))
                                                     .Select(s => s.path).ToArray();
        if (enabled.Length == 0)
        {
            Log("EditorBuildSettings has no enabled scenes; using the original game's scene order");
            enabled = k_OriginalScenes;
        }
        string[] missing = enabled.Where(p => !File.Exists(Path.Combine(ProjectRoot, p))).ToArray();
        if (missing.Length > 0)
            Fail("scenes not found: " + string.Join(", ", missing));
        return enabled;
    }

    static void ConfigureMac()
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneOSX &&
            !EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneOSX))
            Fail("could not switch the active build target to StandaloneOSX");

        if (PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone) != ScriptingImplementation.Mono2x)
        {
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            Log("scripting backend set to Mono");
        }

        // UnityEditor.OSXStandalone.UserBuildSettings.architecture lives in the Mac support
        // extension assembly, so it is set by reflection to avoid a hard reference.
        Type t = AppDomain.CurrentDomain.GetAssemblies()
                          .Select(a => a.GetType("UnityEditor.OSXStandalone.UserBuildSettings", false))
                          .FirstOrDefault(x => x != null);
        var prop = t != null ? t.GetProperty("architecture") : null;
        if (prop == null)
            Fail("UnityEditor.OSXStandalone.UserBuildSettings.architecture not found (Mac build support missing?)");
        object arm64 = Enum.Parse(prop.PropertyType, "ARM64");
        prop.SetValue(null, arm64, null);
        Log("architecture: " + prop.GetValue(null, null));
    }

    public static void BuildMac()
    {
        string app = AppPath;
        ConfigureMac();
        string[] scenes = Scenes();
        Log("scenes: " + scenes.Length + " -> " + string.Join(", ", scenes.Select(Path.GetFileNameWithoutExtension)));
        Log("output: " + app);
        Directory.CreateDirectory(Path.GetDirectoryName(app));

        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = app,
            target = BuildTarget.StandaloneOSX,
            targetGroup = BuildTargetGroup.Standalone,
            options = BuildOptions.Development,
        };
        // The original settings ask for Mac App Store receipt validation, which makes an unsigned
        // player quit with exit code 173 at startup. Off for this build only; the project setting
        // is put back afterwards so ProjectSettings.asset is left as it was.
        bool receipt = PlayerSettings.useMacAppStoreValidation;
        BuildReport report;
        try
        {
            PlayerSettings.useMacAppStoreValidation = false;
            report = BuildPipeline.BuildPlayer(options);
        }
        finally
        {
            PlayerSettings.useMacAppStoreValidation = receipt;
        }
        BuildSummary s = report.summary;
        Log(string.Format("player result: {0}, {1} errors, {2} warnings, {3:F0} s, {4:F1} MB",
                          s.result, s.totalErrors, s.totalWarnings, s.totalTime.TotalSeconds, s.totalSize / 1048576.0));
        if (s.result != BuildResult.Succeeded)
        {
            foreach (var step in report.steps)
                foreach (var m in step.messages)
                    if (m.type == LogType.Error || m.type == LogType.Exception)
                        Debug.LogError("[PortBuild] ERROR " + step.name + ": " + m.content);
            Fail("player build " + s.result);
        }

        BuildAssetBundles(StreamingAssetsOf(app));
        WriteInputAxes(StreamingAssetsOf(app));
        Log("RESULT: Succeeded " + app);
    }

    // Rebuild every AssetBundle defined by asset labels in the project for the Mac target into the
    // default player's StreamingAssets.
    public static void BuildAssetBundles()
    {
        BuildAssetBundles(StreamingAssetsOf(AppPath));
    }

    // The PS4 game loads StreamingAssets/AssetBundles/ClipDirector.unity3d (a TextAsset with the
    // clip director JSON) with AssetBundle.LoadFromFile when ScenarioManager.forceLoadClips is set,
    // which Party-Master does. That file is a Unity 5.3 PS4 bundle the Mac player cannot read, so
    // it is rebuilt from the exported asset that carries the bundle label (clipdirector.unity3d)
    // and written over the copy in the built player, keeping the file name the game asks for.
    public static void BuildAssetBundles(string streamingAssets)
    {
        string[] names = AssetDatabase.GetAllAssetBundleNames();
        var builds = new List<AssetBundleBuild>();
        foreach (string name in names)
        {
            string[] assets = AssetDatabase.GetAssetPathsFromAssetBundle(name);
            if (assets.Length == 0)
                continue;
            builds.Add(new AssetBundleBuild { assetBundleName = name, assetNames = assets });
            Log("bundle " + name + ": " + string.Join(", ", assets));
        }
        string srcBundles = Path.Combine(Application.streamingAssetsPath, "AssetBundles");
        string[] shipped = Directory.Exists(srcBundles)
            ? Directory.GetFiles(srcBundles, "*.unity3d", SearchOption.AllDirectories)
            : new string[0];
        if (builds.Count == 0)
        {
            if (shipped.Length > 0)
                Fail("StreamingAssets has bundles but no asset carries a bundle label to rebuild them from");
            Log("no asset bundles to build");
            return;
        }

        string tmp = Path.Combine(ProjectRoot, "Temp", "PortAssetBundles");
        if (Directory.Exists(tmp))
            Directory.Delete(tmp, true);
        Directory.CreateDirectory(tmp);
        AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(
            tmp, builds.ToArray(), BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneOSX);
        if (manifest == null)
            Fail("BuildPipeline.BuildAssetBundles returned no manifest");

        string dstDir = Path.Combine(streamingAssets, "AssetBundles");
        Directory.CreateDirectory(dstDir);
        var rebuilt = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string bundle in manifest.GetAllAssetBundles())
        {
            // Keep the casing of the shipped file (ClipDirector.unity3d), not the lower-case label.
            string shippedName = shipped.Select(Path.GetFileName)
                                        .FirstOrDefault(f => string.Equals(f, bundle, StringComparison.OrdinalIgnoreCase));
            string fileName = shippedName ?? bundle;
            string dst = Path.Combine(dstDir, fileName);
            foreach (string stale in Directory.GetFiles(dstDir).Where(f =>
                         string.Equals(Path.GetFileName(f), bundle, StringComparison.OrdinalIgnoreCase)))
                File.Delete(stale);
            File.Copy(Path.Combine(tmp, bundle), dst, true);
            rebuilt.Add(fileName);
            Log("bundle written: " + dst);
        }
        foreach (string f in shipped.Select(Path.GetFileName).Where(f => !rebuilt.Contains(f)))
            Debug.LogWarning("[PortBuild] WARNING shipped PS4 bundle not rebuilt: " + f);
    }

    // Dump InputManager's axes to JSON so the runtime input shim can map injected keys to
    // GetButton / GetAxis names the same way the legacy input manager would.
    static void WriteInputAxes(string streamingAssets)
    {
        UnityEngine.Object im = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/InputManager.asset").FirstOrDefault();
        if (im == null)
        {
            Debug.LogWarning("[PortBuild] WARNING InputManager.asset not loadable; scripted GetButton/GetAxis disabled");
            return;
        }
        var so = new SerializedObject(im);
        SerializedProperty axes = so.FindProperty("m_Axes");
        var sb = new StringBuilder("{\"axes\":[\n");
        for (int i = 0; i < axes.arraySize; i++)
        {
            SerializedProperty a = axes.GetArrayElementAtIndex(i);
            if (i > 0)
                sb.Append(",\n");
            sb.Append("{");
            foreach (string f in new[] { "m_Name", "negativeButton", "positiveButton", "altNegativeButton", "altPositiveButton" })
                sb.Append('"').Append(f).Append("\":\"").Append(Escape(a.FindPropertyRelative(f).stringValue)).Append("\",");
            sb.Append("\"type\":").Append(a.FindPropertyRelative("type").intValue).Append(',');
            sb.Append("\"axis\":").Append(a.FindPropertyRelative("axis").intValue).Append(',');
            sb.Append("\"invert\":").Append(a.FindPropertyRelative("invert").boolValue ? "true" : "false");
            sb.Append("}");
        }
        sb.Append("\n]}\n");
        Directory.CreateDirectory(streamingAssets);
        string path = Path.Combine(streamingAssets, "PortInputAxes.json");
        File.WriteAllText(path, sb.ToString());
        Log("input axes: " + axes.arraySize + " -> " + path);
    }

    static string Escape(string s)
    {
        return (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
