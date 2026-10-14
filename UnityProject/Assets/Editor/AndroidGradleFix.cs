using System.IO;
using UnityEditor.Android;
using UnityEngine;

// Gradle 9 refuses to snapshot the libc++_shared.so link that the native (CMake) step leaves in
// its output folder ("not a regular file"), failing every Android build. Unity regenerates the
// Gradle project each build, so this hook patches it every time: once CMake has built, the link is
// swapped for a real copy of the library, and the native-library tasks are left untracked.
public class AndroidGradleFix : IPostGenerateGradleAndroidProject
{
    public int callbackOrder => 100;

    public void OnPostGenerateGradleAndroidProject(string path)
    {
        string gradle = Path.Combine(path, "build.gradle");
        if (!File.Exists(gradle)) return;
        string text = File.ReadAllText(gradle);
        if (text.Contains("LastStanceLinkFix")) return;
        text += @"

// LastStanceLinkFix: the CMake output holds a libc++_shared.so link Gradle cannot snapshot
def lastStanceUnlink = {
    def root = layout.buildDirectory.dir('intermediates/cxx').get().asFile.toPath()
    if (!java.nio.file.Files.exists(root)) return
    def links = []
    java.nio.file.Files.walk(root).withCloseable { s ->
        s.forEach { p ->
            if (java.nio.file.Files.isSymbolicLink(p) ||
                (p.fileName.toString().endsWith('.so') && !java.nio.file.Files.isRegularFile(p, java.nio.file.LinkOption.NOFOLLOW_LINKS) && !java.nio.file.Files.isDirectory(p)))
                links << p
        }
    }
    links.each { p ->
        def real = p.toRealPath()
        def tmp = p.resolveSibling(p.fileName.toString() + '.copy')
        java.nio.file.Files.copy(real, tmp, java.nio.file.StandardCopyOption.REPLACE_EXISTING)
        java.nio.file.Files.delete(p)
        java.nio.file.Files.move(tmp, p)
    }
}
tasks.configureEach { t ->
    if (t.name.startsWith('buildCMake') || t.name.startsWith('configureCMake')) {
        t.doNotTrackState('libc++_shared.so in the CMake output is a link, not a regular file')
        t.doLast { lastStanceUnlink() }
    }
    if (t.name.contains('NativeLibs') || t.name.contains('NativeDebugMetadata') || t.name.contains('SymbolTables')) {
        t.doNotTrackState('reads the CMake output')
        t.doFirst { lastStanceUnlink() }
    }
}
";
        File.WriteAllText(gradle, text);
        Debug.Log("AndroidGradleFix: patched " + gradle);
    }
}
