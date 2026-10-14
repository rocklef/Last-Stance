using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Android;
using UnityEngine;

public static class BuildTools
{
    public const string ExePath = "../Build/LastStance.exe";

    [MenuItem("Last Stance/Build Windows Player")]
    public static void BuildWindows()
    {
        PlayerSettings.productName = "Last Stance";
        PlayerSettings.companyName = "Last Stance";
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.defaultScreenWidth = 1600;
        PlayerSettings.defaultScreenHeight = 900;
        PlayerSettings.resizableWindow = true;
        PlayerSettings.runInBackground = true;
        CobraAssets.Build();
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64);
        var report = BuildPipeline.BuildPlayer(new[] { SceneBuilder.ScenePath }, ExePath, BuildTarget.StandaloneWindows64, BuildOptions.None);
        Debug.Log($"BuildTools: {report.summary.result}, {report.summary.totalErrors} errors, output {report.summary.outputPath}");
        if (Application.isBatchMode) EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
    }

    public const string ApkPath = "../Build/LastStance.apk";

    // The phone version: landscape, ARM64, with touch controls enabled automatically.
    [MenuItem("Last Stance/Build Android APK")]
    public static void BuildAndroid()
    {
        PlayerSettings.productName = "Last Stance";
        PlayerSettings.companyName = "Last Stance";
        var android = NamedBuildTarget.Android;
        PlayerSettings.SetApplicationIdentifier(android, "com.laststance.game");
        PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
        AndroidExternalToolsSettings.sdkRootPath = "C:/Users/ASUS/AppData/Local/Android/Sdk";
        AndroidExternalToolsSettings.jdkRootPath = "C:/Users/ASUS/OneDrive/Desktop/6000.6.2f1/Editor/Data/PlaybackEngines/AndroidPlayer/OpenJDK";
        AndroidExternalToolsSettings.ndkRootPath = "C:/Users/ASUS/AppData/Local/Android/Sdk/ndk/27.2.12479018";   // outside OneDrive: its cloud placeholders break Gradle
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
        PlayerSettings.bundleVersion = "1.0";
        PlayerSettings.Android.bundleVersionCode = 1;
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
        PlayerSettings.allowedAutorotateToLandscapeLeft = true;
        PlayerSettings.allowedAutorotateToLandscapeRight = true;
        PlayerSettings.allowedAutorotateToPortrait = false;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
        PlayerSettings.runInBackground = false;
        EditorUserBuildSettings.buildAppBundle = false;
        PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)35;
        // Unity's own SDK download was incomplete: use the full Android SDK already on this PC
        string sdk = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk");
        if (System.IO.Directory.Exists(sdk))
        {
            EditorPrefs.SetBool("SdkUseEmbedded", false);
            EditorPrefs.SetString("AndroidSdkRoot", sdk);
            var t = System.Type.GetType("UnityEditor.Android.AndroidExternalToolsSettings, UnityEditor.Android.Extensions");
            var prop = t?.GetProperty("sdkRootPath", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (prop != null && prop.CanWrite) prop.SetValue(null, sdk);
            Debug.Log("BuildTools: Android SDK " + sdk + (prop != null ? " (set)" : " (prefs only)"));
        }
        CobraAssets.Build();
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
        var report = BuildPipeline.BuildPlayer(new[] { SceneBuilder.ScenePath }, ApkPath, BuildTarget.Android, BuildOptions.None);
        Debug.Log($"BuildTools: {report.summary.result}, {report.summary.totalErrors} errors, output {report.summary.outputPath}, {report.summary.totalSize / (1024 * 1024)} MB");
        if (Application.isBatchMode) EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
    }
}
