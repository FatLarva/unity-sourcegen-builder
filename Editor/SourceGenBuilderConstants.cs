using System.IO;
using UnityEngine;

namespace SourceGenBuilder.Editor
{
    /// <summary>
    /// Tool-level constants for Source Gen Builder.
    /// </summary>
    public static class SourceGenBuilderConstants
    {
        public const string RoslynAnalyzerLabel = "RoslynAnalyzer";

        public const string LogFolderName = "SourceGenBuilder";
        public const string LogFileName = "build.log";
        public const string PrevLogFileName = "build.prev.log";

        public static string ProjectRoot =>
            Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        public static string LogFolderPath =>
            Path.Combine(ProjectRoot, "Library", LogFolderName);
    }
}
