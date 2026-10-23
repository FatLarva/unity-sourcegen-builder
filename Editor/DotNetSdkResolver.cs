using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SourceGenBuilder.Editor
{
    internal sealed class DotNetSdk
    {
        public string ExecutablePath { get; }
        public string Versions { get; }
        public string Source { get; }

        public DotNetSdk(string executablePath, string versions, string source)
        {
            ExecutablePath = executablePath;
            Versions = versions;
            Source = source;
        }
    }

    /// <summary>Finds a build SDK, rather than accepting a runtime-only dotnet host.</summary>
    internal static class DotNetSdkResolver
    {
        private const int ProbeTimeoutMilliseconds = 2000;
        private const int TotalTimeoutMilliseconds = 5000;

        public static bool TryResolve(string editorContentsPath, string workingDirectory,
            out DotNetSdk sdk, out string diagnostics)
        {
            sdk = null;
            var messages = new StringBuilder();
            var timer = Stopwatch.StartNew();
            bool windows = Path.DirectorySeparatorChar == '\\';
            string executableName = windows ? "dotnet.exe" : "dotnet";
            var checkedPaths = new HashSet<string>(windows
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal);

            string bundledPath = Path.GetFullPath(Path.Combine(editorContentsPath, "DotNetSdk", executableName));
            if (TryCandidate(bundledPath, "Unity bundled SDK", workingDirectory, timer,
                    checkedPaths, messages, out sdk))
            {
                diagnostics = messages.ToString();
                return true;
            }

            if (!File.Exists(bundledPath))
            {
                messages.AppendLine($"Unity bundled dotnet was not found: {bundledPath}");
            }

            bool foundOnPath = false;
            foreach (string entry in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                string directory = Environment.ExpandEnvironmentVariables(entry.Trim().Trim('"'));
                // Do not interpret an empty PATH entry as a request to execute from the project directory.
                if (directory.Length == 0) continue;

                string candidate;
                try
                {
                    candidate = Path.GetFullPath(Path.Combine(directory, executableName));
                }
                catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
                {
                    continue;
                }

                if (!File.Exists(candidate)) continue;
                foundOnPath = true;
                if (TryCandidate(candidate, "PATH", workingDirectory, timer, checkedPaths, messages, out sdk))
                {
                    diagnostics = messages.ToString();
                    return true;
                }
                if (timer.ElapsedMilliseconds >= TotalTimeoutMilliseconds) break;
            }

            if (!foundOnPath) messages.AppendLine($"No {executableName} executable was found on the Unity Editor's PATH.");
            messages.AppendLine("No usable .NET SDK was found. The .NET runtime alone cannot build .csproj projects.");
            messages.AppendLine("Install the .NET SDK required by your generator project: https://dotnet.microsoft.com/download");
            messages.AppendLine("In a new terminal, run 'dotnet --list-sdks' and check that it lists at least one SDK.");
            messages.AppendLine("After changing PATH or installing an SDK, restart Unity and the Unity Hub that launched it so they inherit the updated environment.");
            diagnostics = messages.ToString();
            return false;
        }

        private static bool TryCandidate(string path, string source, string workingDirectory,
            Stopwatch timer, HashSet<string> checkedPaths, StringBuilder messages, out DotNetSdk sdk)
        {
            sdk = null;
            if (!checkedPaths.Add(path) || !File.Exists(path)) return false;

            int remaining = TotalTimeoutMilliseconds - (int)timer.ElapsedMilliseconds;
            if (remaining <= 0)
            {
                messages.AppendLine("SDK discovery reached its 5-second time limit. Check the dotnet installations reported above.");
                return false;
            }

            int timeout = Math.Min(ProbeTimeoutMilliseconds, remaining);
            if (!TryProbe(path, workingDirectory, timeout, out string versions, out string error))
            {
                messages.AppendLine($"{source}: '{path} --list-sdks' {error}");
                return false;
            }

            sdk = new DotNetSdk(path, versions, source);
            return true;
        }

        private static bool TryProbe(string executable, string workingDirectory, int timeout,
            out string versions, out string error)
        {
            versions = null;
            error = null;
            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = "--list-sdks",
                    WorkingDirectory = workingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                var timer = Stopwatch.StartNew();
                try
                {
                    if (!process.Start())
                    {
                        error = "could not start the process.";
                        return false;
                    }

                    // Drain both streams concurrently; a broken host must not block the Editor on a full pipe.
                    Task<string> stdout = process.StandardOutput.ReadToEndAsync();
                    Task<string> stderr = process.StandardError.ReadToEndAsync();
                    int remaining = Math.Max(0, timeout - (int)timer.ElapsedMilliseconds);
                    if (!process.WaitForExit(remaining) ||
                        !Task.WaitAll(new Task[] { stdout, stderr }, Math.Max(0, timeout - (int)timer.ElapsedMilliseconds)))
                    {
                        TryKill(process);
                        error = $"timed out after {timeout} ms. Run this command in a terminal to investigate.";
                        return false;
                    }

                    if (process.ExitCode != 0)
                    {
                        error = $"failed with exit code {process.ExitCode}: {stderr.Result.Trim()}";
                        return false;
                    }

                    var detected = new List<string>();
                    foreach (string line in stdout.Result.Split('\n'))
                    {
                        Match match = Regex.Match(line, @"^\s*(\d+\.\d+\.\d+(?:-[^\s\[]+)?)\s+\[[^\r\n]+\]\s*$");
                        if (match.Success) detected.Add(match.Groups[1].Value);
                    }

                    if (detected.Count == 0)
                    {
                        error = "reported no installed SDKs. This host may have only a .NET runtime.";
                        return false;
                    }

                    versions = string.Join(", ", detected);
                    return true;
                }
                catch (Exception ex)
                {
                    TryKill(process);
                    error = $"could not be queried: {ex.Message}";
                    return false;
                }
            }
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited) process.Kill();
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }
    }
}
