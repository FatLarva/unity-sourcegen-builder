using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SourceGenBuilder.Editor
{
    /// <summary>
    /// Defines how a single DLL is built and installed into Unity.
    /// Used for both dependencies (e.g. Attributes) and the main generator/analyzer.
    /// </summary>
    [Serializable]
    public class BuildTarget
    {
        [Tooltip("Relative path from Source Root to the .csproj file.\ne.g. Attributes/MyLib.Attributes.csproj")]
        [SerializeField]
        private string _csprojPath = "";

        [Tooltip("Asset path (including filename) where the built DLL will be installed.\ne.g. Assets/Analyzers/MyLib.Attributes.dll")]
        [SerializeField]
        private string _installDllPath = "";

        [Tooltip("How Unity should treat this DLL after import.")]
        [SerializeField]
        private DllType _dllType = DllType.Runtime;

        public BuildTarget() { }

        public BuildTarget(DllType dllType)
        {
            _dllType = dllType;
        }

        public string CsprojPath => _csprojPath;
        public string InstallDllPath => _installDllPath;
        public DllType DllType => _dllType;

        /// <summary>
        /// Extracts the DLL filename from <see cref="InstallDllPath"/>.
        /// </summary>
        public string DllFileName => Path.GetFileName(PathUtils.Normalize(_installDllPath));
    }

    /// <summary>
    /// Determines how the PluginImporter is configured after the DLL is copied.
    /// </summary>
    public enum DllType
    {
        /// <summary>Regular managed DLL, all platforms enabled.</summary>
        Runtime,

        /// <summary>Roslyn analyzer/source generator. All platforms disabled, RoslynAnalyzer label applied.</summary>
        Analyzer,
    }

    /// <summary>
    /// A profile that describes one source generator (or analyzer) and its dependencies.
    /// Create via Assets → Create → Source Gen Builder → Profile.
    /// </summary>
    [CreateAssetMenu(
        fileName = "NewSourceGeneratorProfile",
        menuName = "Source Gen Builder/Profile",
        order = 200)]
    public class SourceGeneratorProfile : ScriptableObject
    {
        [Tooltip("Display name shown in the builder window.")]
        [SerializeField]
        private string _displayName = "My Generator";

        [Tooltip("Working directory for dotnet build, relative to the Unity project root.\ne.g. Assets/Scripts/GeneratorSources~/MyGenerator")]
        [SerializeField]
        private string _sourceRoot = "";

        [Tooltip("Extra arguments passed to dotnet build.\nDefaults are used if left empty.")]
        [SerializeField]
        private string _extraBuildArgs = "";

        [Tooltip("Dependencies built before the main generator, in order.\nTypically attribute libraries or shared utilities.")]
        [SerializeField]
        private BuildTarget[] _dependencies = Array.Empty<BuildTarget>();

        [Tooltip("The main generator or analyzer DLL.")]
        [SerializeField]
        private BuildTarget _generator = new(DllType.Analyzer);

        public string DisplayName => _displayName;
        public string SourceRoot => _sourceRoot;
        public string ExtraBuildArgs => _extraBuildArgs;
        public IReadOnlyList<BuildTarget> Dependencies => _dependencies;
        public BuildTarget Generator => _generator;

        /// <summary>
        /// Returns the effective dotnet build arguments for a given csproj.
        /// </summary>
        public string GetBuildArguments(string csprojAbsPath, string outputDir)
        {
            string baseArgs = $"build \"{csprojAbsPath}\" -c Release --no-incremental -o \"{outputDir}\"";

            if (!string.IsNullOrWhiteSpace(_extraBuildArgs))
            {
                baseArgs += " " + _extraBuildArgs.Trim();
            }

            return baseArgs;
        }
    }
}
