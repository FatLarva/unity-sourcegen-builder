using System;
using System.IO;

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
        /// Validates a <see cref="BuildTarget"/> for common configuration errors.
        /// Returns null if valid, or an error message string if invalid.
        /// </summary>
        public static string ValidateBuildTarget(BuildTarget target, string profileName, string sourceRoot, string projectRoot)
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

            string csprojAbs = Path.Combine(projectRoot, Normalize(sourceRoot), csproj);
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
