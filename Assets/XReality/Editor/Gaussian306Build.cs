using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace XReality.Editor
{
    public static class Gaussian306Build
    {
        public static void Windows()
        {
            StudioProject.Prepare();
            string output = Path.GetFullPath("Builds/GaussianAvatars-306/Live-XReality/Windows/XReality.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            var report = BuildPipeline.BuildPlayer(scenes, output, BuildTarget.StandaloneWindows64, BuildOptions.None);
            if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("306 evaluation build failed.");
            Debug.Log("XR_GA_306_LIVE_BUILD_OK: " + output);
        }
    }
}
