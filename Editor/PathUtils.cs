using System;
using System.IO;
using UnityEditor.PackageManager;

namespace SourceGenBuilder.Editor
{
    /// <summary>
    /// Utilities for normalizing and validating user-provided paths.
    /// </summary>
    public static class PathUtils
    {
        /// <summary>
        /// Normalizes a relative path: trims whitespace, converts backslashes to forward slashes,
        /// strips leading/trailing slashes, and collapses double slashes.
        /// </summary>
        public static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return "";
            }

            path = path.Trim();
            path = path.Replace('\\', '/');
            path = path.Trim('/');

            while (path.Contains("//"))
            {
                path = path.Replace("//", "/");
            }

            return path;
        }

        /// <summary>
        /// Safely combines path segments, normalizing each segment first.
        /// </summary>
        public static string CombineNormalized(params string[] segments)
        {
            for (int i = 0; i < segments.Length; i++)
            {
                segments[i] = Normalize(segments[i]);
            }

            return Path.Combine(segments);
        }

        /// <summary>
        /// Resolves a project-relative path to a physical path for external tools.
        /// Unity's virtual Packages paths can pass Directory.Exists even when no
        /// corresponding directory exists on disk, so query UPM before checking them.
        /// </summary>
        public static string ResolveProjectPath(string path, string projectRoot)
        {
            string absolutePath = Path.GetFullPath(Path.Combine(projectRoot, (path ?? "").Trim().Replace('\\', '/')));
            string packagesRoot = Path.GetFullPath(Path.Combine(projectRoot, "Packages")) + Path.DirectorySeparatorChar;
            var comparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            if (!absolutePath.StartsWith(packagesRoot, comparison))
            {
                return absolutePath;
            }

            string packageRelativePath = absolutePath.Substring(packagesRoot.Length).Replace('\\', '/');
            int separator = packageRelativePath.IndexOf('/');
            string packageName = separator < 0 ? packageRelativePath : packageRelativePath.Substring(0, separator);
            // Look up the package root: source folders ending in '~' aren't imported assets.
            var package = PackageInfo.FindForAssetPath("Packages/" + packageName);
            if (package == null || string.IsNullOrEmpty(package.resolvedPath))
            {
                return absolutePath;
            }

            string subpath = separator < 0 ? "" : packageRelativePath.Substring(separator + 1);
            return Path.GetFullPath(Path.Combine(package.resolvedPath, subpath));
        }

        /// <summary>
        /// Validates a <see cref="BuildTarget"/> for common configuration errors.
        /// Returns null if valid, or an error message string if invalid.
        /// </summary>
        public static string ValidateBuildTarget(BuildTarget target, string profileName, string sourceRoot, string projectRoot)
        {
            return ValidateBuildTargetAtRoot(target, profileName, ResolveProjectPath(sourceRoot, projectRoot));
        }

        internal static string ValidateBuildTargetAtRoot(BuildTarget target, string profileName, string sourceRootAbs)
        {
            if (target == null)
            {
                return $"[{profileName}] Build target is null.";
            }

            string csproj = Normalize(target.CsprojPath);

            if (string.IsNullOrEmpty(csproj))
            {
                return $"[{profileName}] csproj path is empty.";
            }

            if (!csproj.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                return $"[{profileName}] csproj path does not end with .csproj: \"{csproj}\"";
            }

            string csprojAbs = Path.Combine(sourceRootAbs, csproj);
            if (!File.Exists(csprojAbs))
            {
                return $"[{profileName}] .csproj not found at: {csprojAbs}";
            }

            string installPath = Normalize(target.InstallDllPath);

            if (string.IsNullOrEmpty(installPath))
            {
                return $"[{profileName}] Install DLL path is empty.";
            }

            if (!installPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                return $"[{profileName}] Install path does not end with .dll: \"{installPath}\"\n" +
                       "  It should include the full filename, e.g. Assets/Analyzers/MyLib.dll";
            }

            if (!installPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) &&
                !installPath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
            {
                return $"[{profileName}] Install path should start with Assets/ or Packages/: \"{installPath}\"";
            }

            return null;
        }
    }
}
