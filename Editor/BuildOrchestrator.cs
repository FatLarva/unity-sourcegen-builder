using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;

namespace SourceGenBuilder.Editor
{
    /// <summary>
    /// Drives the build pipeline: validates profiles, manages the dotnet process,
    /// copies DLLs, and configures Unity importers. Stateful — one build at a time.
    /// </summary>
    public class BuildOrchestrator
    {
        public struct BuildStep
        {
            public SourceGeneratorProfile Profile;
            public BuildTarget Target;
            public string SourceRoot;
        }

        // ── State ───────────────────────────────────────────────────────

        private Process _activeProcess;
        private string _dotnetExecutable;
        private StringBuilder _stdOutput;
        private StringBuilder _errOutput;
        private string _lastLine;
        private readonly object _outputLock = new();

        private List<BuildStep> _buildQueue;
        private int _buildIndex;
        private HashSet<string> _firstTimeInstalls;
        private readonly RotatingFileLog _log;

        public bool IsBuilding { get; private set; }
        public int StepCount => _buildQueue?.Count ?? 0;
        public int CurrentStepIndex => _buildIndex;
        public BuildStep? CurrentStep =>
            _buildQueue != null && _buildIndex < _buildQueue.Count ? _buildQueue[_buildIndex] : null;

        public string LastLine
        {
            get
            {
                lock (_outputLock)
                {
                    return _lastLine;
                }
            }
        }

        /// <summary>
        /// Fired when the entire build sequence completes (success or failure).
        /// Cancellation does not fire this event. The bool indicates success.
        /// </summary>
        public event Action<bool> BuildFinished;

        // ── Construction ────────────────────────────────────────────────

        public BuildOrchestrator(RotatingFileLog log)
        {
            _log = log;
        }

        // ── Public API ──────────────────────────────────────────────────

        /// <summary>
        /// Validates all profiles in settings, builds the queue, and starts.
        /// Returns false if validation failed (errors are logged).
        /// </summary>
        public bool StartBuild(SourceGenBuilderSettings settings)
        {
            if (IsBuilding)
            {
                _log.AppendTimestamped("A build is already in progress.");
                return false;
            }

            if (settings == null)
            {
                _log.AppendTimestamped("ERROR: No settings asset assigned.");
                return false;
            }

            string projectRoot = SourceGenBuilderConstants.ProjectRoot;

            _buildQueue = new List<BuildStep>();
            _firstTimeInstalls = new HashSet<string>();

            bool hasErrors = false;

            foreach (var profile in settings.Profiles)
            {
                if (profile == null)
                {
                    continue;
                }

                string profileName = string.IsNullOrWhiteSpace(profile.DisplayName)
                    ? profile.name
                    : profile.DisplayName;

                if (string.IsNullOrWhiteSpace(profile.SourceRoot))
                {
                    _log.AppendTimestamped($"ERROR: [{profileName}] Source root is empty. Skipping.");
                    hasErrors = true;
                    continue;
                }

                string sourceRootAbs;
                try
                {
                    sourceRootAbs = PathUtils.ResolveProjectPath(profile.SourceRoot, projectRoot);
                }
                catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is NotSupportedException)
                {
                    _log.AppendTimestamped($"ERROR: [{profileName}] Invalid source root '{profile.SourceRoot}': {ex.Message}");
                    hasErrors = true;
                    continue;
                }
                if (!Directory.Exists(sourceRootAbs))
                {
                    _log.AppendTimestamped($"ERROR: [{profileName}] Source root directory not found: {sourceRootAbs}");
                    hasErrors = true;
                    continue;
                }

                foreach (var dep in profile.Dependencies)
                {
                    string error = PathUtils.ValidateBuildTargetAtRoot(dep, profileName, sourceRootAbs);
                    if (error != null)
                    {
                        _log.AppendTimestamped($"ERROR: {error}");
                        hasErrors = true;
                        continue;
                    }

                    _buildQueue.Add(new BuildStep { Profile = profile, Target = dep, SourceRoot = sourceRootAbs });
                }

                {
                    string error = PathUtils.ValidateBuildTargetAtRoot(profile.Generator, profileName, sourceRootAbs);
                    if (error != null)
                    {
                        _log.AppendTimestamped($"ERROR: {error}");
                        hasErrors = true;
                    }
                    else
                    {
                        _buildQueue.Add(new BuildStep { Profile = profile, Target = profile.Generator, SourceRoot = sourceRootAbs });
                    }
                }
            }

            if (hasErrors)
            {
                _log.AppendTimestamped("Build aborted due to validation errors. Fix the issues above and try again.");
                _buildQueue = null;
                _firstTimeInstalls = null;
                return false;
            }

            if (_buildQueue.Count == 0)
            {
                _log.AppendTimestamped("Nothing to build. Add profiles with valid paths.");
                _buildQueue = null;
                _firstTimeInstalls = null;
                return false;
            }

            foreach (var step in _buildQueue)
            {
                string installPath = PathUtils.Normalize(step.Target.InstallDllPath);
                if (!File.Exists(PathUtils.ResolveProjectPath(installPath, projectRoot)))
                {
                    _firstTimeInstalls.Add(installPath);
                }
            }

            _log.Rotate();
            if (!DotNetSdkResolver.TryResolve(EditorApplication.applicationContentsPath, projectRoot,
                    out var sdk, out string diagnostics))
            {
                _log.AppendTimestamped("ERROR: " + diagnostics);
                _buildQueue = null;
                _firstTimeInstalls = null;
                return false;
            }

            if (!string.IsNullOrEmpty(diagnostics)) _log.AppendTimestamped(diagnostics);
            _dotnetExecutable = sdk.ExecutablePath;
            _log.AppendTimestamped($"Using .NET SDK from {sdk.Source}: {_dotnetExecutable}\n  Available SDKs: {sdk.Versions}");
            _buildIndex = 0;
            IsBuilding = true;

            _log.AppendTimestamped($"Build started — {_buildQueue.Count} step(s) queued.");
            EditorApplication.LockReloadAssemblies();

            StartCurrentStep();
            return true;
        }

        /// <summary>
        /// Must be called every frame (e.g. from OnGUI) while building.
        /// Returns true if still in progress, false if finished.
        /// </summary>
        public bool Poll()
        {
            if (!IsBuilding)
            {
                return false;
            }

            if (_activeProcess == null)
            {
                Finish(false);
                return false;
            }

            if (!_activeProcess.HasExited)
            {
                return true;
            }

            _activeProcess.WaitForExit();

            string stdout;
            string stderr;
            lock (_outputLock)
            {
                stdout = _stdOutput.ToString().Trim();
                stderr = _errOutput.ToString().Trim();
            }

            if (stdout.Length > 0)
            {
                _log.Append(stdout + "\n");
            }

            if (stderr.Length > 0)
            {
                _log.AppendTimestamped($"⚠ stderr:\n{stderr}");
            }

            var step = _buildQueue[_buildIndex];
            string dllName = step.Target.DllFileName;

            if (_activeProcess.ExitCode != 0)
            {
                _log.AppendTimestamped($"ERROR: {dllName} failed with exit code {_activeProcess.ExitCode}.");
                Finish(false);
                return false;
            }

            _log.AppendTimestamped($"{dllName} built successfully.");

            _activeProcess.Dispose();
            _activeProcess = null;

            if (!CopyDll(step))
            {
                Finish(false);
                return false;
            }

            _buildIndex++;

            if (_buildIndex < _buildQueue.Count)
            {
                StartCurrentStep();
                return true;
            }

            FinalizeInstall();
            Finish(true);
            return false;
        }

        /// <summary>
        /// Cancels the current build, killing the process if running.
        /// Does NOT fire <see cref="BuildFinished"/> — the caller is responsible
        /// for any UI transitions since it initiated the cancel.
        /// </summary>
        public void Cancel()
        {
            if (!IsBuilding)
            {
                return;
            }

            _log.AppendTimestamped("Build cancelled by user.");
            KillProcess();
            CleanupState();
        }

        // ─── Internal ───────────────────────────────────────────────────

        private void StartCurrentStep()
        {
            if (_buildIndex >= _buildQueue.Count)
            {
                return;
            }

            var step = _buildQueue[_buildIndex];
            string workDir = step.SourceRoot;
            string csprojRel = PathUtils.Normalize(step.Target.CsprojPath);
            string csprojAbs = Path.Combine(workDir, csprojRel);
            string binDir = Path.Combine(Path.GetDirectoryName(csprojAbs)!, "bin");

            string args = step.Profile.GetBuildArguments(csprojAbs, binDir);

            ProcessStartInfo psi = new()
            {
                FileName = _dotnetExecutable,
                Arguments = args,
                WorkingDirectory = workDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            string dllName = step.Target.DllFileName;
            if (_buildIndex == 0 || _buildQueue[_buildIndex - 1].Profile != step.Profile)
            {
                _log.AppendTimestamped($"Source root: {step.Profile.SourceRoot} → {workDir}");
            }
            _log.AppendTimestamped($"[{_buildIndex + 1}/{_buildQueue.Count}] dotnet build {Path.GetFileName(csprojRel)} → {dllName} ...");

            try
            {
                _activeProcess = Process.Start(psi);
            }
            catch (Exception ex)
            {
                _log.AppendTimestamped($"ERROR: Failed to start the SDK host '{_dotnetExecutable}'.\n  Working directory: {workDir}\n  {ex.Message}");
                _activeProcess = null;
                Finish(false);
                return;
            }

            if (_activeProcess == null)
            {
                _log.AppendTimestamped($"ERROR: SDK host '{_dotnetExecutable}' did not return a process. Working directory: {workDir}");
                Finish(false);
                return;
            }

            _stdOutput = new StringBuilder();
            _errOutput = new StringBuilder();
            _lastLine = $"Building {dllName}...";

            _activeProcess.EnableRaisingEvents = true;
            _activeProcess.OutputDataReceived += OnStdOutput;
            _activeProcess.ErrorDataReceived += OnStdError;
            _activeProcess.BeginOutputReadLine();
            _activeProcess.BeginErrorReadLine();
        }

        private void OnStdOutput(object sender, DataReceivedEventArgs args)
        {
            if (args.Data == null) return;

            lock (_outputLock)
            {
                _stdOutput.AppendLine(args.Data);

                string trimmed = args.Data.Trim();
                if (trimmed.Length > 0)
                {
                    _lastLine = trimmed;
                }
            }
        }

        private void OnStdError(object sender, DataReceivedEventArgs args)
        {
            if (args.Data == null) return;

            lock (_outputLock)
            {
                _errOutput.AppendLine(args.Data);
            }
        }

        private bool CopyDll(BuildStep step)
        {
            string projectRoot = SourceGenBuilderConstants.ProjectRoot;
            string csprojRel = PathUtils.Normalize(step.Target.CsprojPath);
            string csprojDir = Path.GetDirectoryName(csprojRel) ?? "";
            string dllName = step.Target.DllFileName;
            string sourcePath = Path.Combine(step.SourceRoot, csprojDir, "bin", dllName);

            if (!File.Exists(sourcePath))
            {
                _log.AppendTimestamped($"ERROR: Built DLL not found at: {sourcePath}");
                return false;
            }

            string installPath = PathUtils.Normalize(step.Target.InstallDllPath);
            string destAbs = PathUtils.ResolveProjectPath(installPath, projectRoot);
            string destDir = Path.GetDirectoryName(destAbs)!;
            try
            {
                Directory.CreateDirectory(destDir);
                File.Copy(sourcePath, destAbs, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                _log.AppendTimestamped($"ERROR: Could not copy '{sourcePath}' to '{destAbs}': {ex.Message}");
                return false;
            }
            _log.AppendTimestamped($"Copied → {installPath}");
            return true;
        }

        private void FinalizeInstall()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            foreach (var step in _buildQueue)
            {
                string assetPath = PathUtils.Normalize(step.Target.InstallDllPath);

                if (!_firstTimeInstalls.Contains(assetPath))
                {
                    continue;
                }

                switch (step.Target.DllType)
                {
                    case DllType.Runtime:
                        ConfigureRuntimeDll(assetPath);
                        break;
                    case DllType.Analyzer:
                        ConfigureAnalyzerDll(assetPath);
                        break;
                }
            }

            _log.AppendTimestamped($"Done! All {_buildQueue.Count} DLL(s) installed.");
        }

        private void ConfigureRuntimeDll(string assetPath)
        {
            PluginImporter importer = AssetImporter.GetAtPath(assetPath) as PluginImporter;
            if (importer == null)
            {
                _log.AppendTimestamped($"WARNING: Could not get PluginImporter for {assetPath}.");
                return;
            }

            importer.SetCompatibleWithAnyPlatform(true);
            importer.SaveAndReimport();

            _log.AppendTimestamped($"Configured as Runtime DLL (all platforms): {Path.GetFileName(assetPath)}");
        }

        private void ConfigureAnalyzerDll(string assetPath)
        {
            PluginImporter importer = AssetImporter.GetAtPath(assetPath) as PluginImporter;
            if (importer == null)
            {
                _log.AppendTimestamped($"WARNING: Could not get PluginImporter for {assetPath}.");
                return;
            }

            importer.SetCompatibleWithAnyPlatform(false);
            importer.SetCompatibleWithEditor(false);
            importer.SaveAndReimport();

            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            if (asset == null)
            {
                _log.AppendTimestamped($"WARNING: Could not load asset at {assetPath} to set label.");
                return;
            }

            string[] existingLabels = AssetDatabase.GetLabels(asset);

            for (int i = 0; i < existingLabels.Length; i++)
            {
                if (string.Equals(existingLabels[i], SourceGenBuilderConstants.RoslynAnalyzerLabel, StringComparison.OrdinalIgnoreCase))
                {
                    _log.AppendTimestamped($"Configured as Analyzer: {Path.GetFileName(assetPath)}");
                    return;
                }
            }

            string[] newLabels = new string[existingLabels.Length + 1];
            existingLabels.CopyTo(newLabels, 0);
            newLabels[existingLabels.Length] = SourceGenBuilderConstants.RoslynAnalyzerLabel;

            AssetDatabase.SetLabels(asset, newLabels);
            _log.AppendTimestamped($"Configured as Analyzer + {SourceGenBuilderConstants.RoslynAnalyzerLabel} label: {Path.GetFileName(assetPath)}");
        }

        private void KillProcess()
        {
            try
            {
                if (_activeProcess != null && !_activeProcess.HasExited)
                {
                    _activeProcess.Kill();
                }
            }
            catch
            {
                // Process may have exited between the check and kill.
            }
        }

        private void Finish(bool success)
        {
            CleanupState();
            BuildFinished?.Invoke(success);
        }

        private void CleanupState()
        {
            if (_activeProcess != null)
            {
                _activeProcess.Dispose();
                _activeProcess = null;
            }

            _stdOutput = null;
            _dotnetExecutable = null;
            _errOutput = null;
            _lastLine = null;
            IsBuilding = false;
            _buildQueue = null;
            _firstTimeInstalls = null;

            EditorApplication.UnlockReloadAssemblies();
        }
    }
}
