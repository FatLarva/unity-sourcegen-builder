using System.IO;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace SourceGenBuilder.Editor
{
    public class SourceGeneratorBuilderWindow : EditorWindow
    {
        // ── Settings asset ──────────────────────────────────────────────

        private SourceGenBuilderSettings _settings;
        private ReorderableList _reorderableList;
        private SerializedObject _settingsSO;
        private SerializedProperty _profilesProp;
        private Vector2 _profileScroll;

        // ── Build orchestrator ──────────────────────────────────────────

        private RotatingFileLog _log;
        private BuildOrchestrator _orchestrator;

        // ── Window state ────────────────────────────────────────────────

        private Vector2 _logScroll;
        private bool _autoScroll = true;
        private GUIStyle _logStyle;
        private GUIStyle _buttonStyle;

        private static SourceGeneratorBuilderWindow _instance;

        // ── Splitter ────────────────────────────────────────────────────

        private float _splitterRatio = 0.35f;
        private bool _draggingSplitter;

        // ── Window lifecycle ────────────────────────────────────────────

        [MenuItem("Tools/Source Gen Builder")]
        public static void ShowWindow()
        {
            if (_instance != null)
            {
                _instance.Focus();
                return;
            }

            var window = CreateInstance<SourceGeneratorBuilderWindow>();
            window.titleContent = new GUIContent("Source Gen Builder");
            window.minSize = new Vector2(500, 350);
            window.EnsureLogAndOrchestrator();
            window._log.LoadFromDisk();
            window.FindSettings();
            _instance = window;
            window.Show();
        }

        private void OnEnable()
        {
            _instance = this;
            EnsureLogAndOrchestrator();
            BindSettings();
        }

        private void OnDestroy()
        {
            if (_orchestrator != null)
            {
                _orchestrator.BuildFinished -= OnBuildFinished;

                if (_orchestrator.IsBuilding)
                {
                    _orchestrator.Cancel();
                }
            }

            if (_instance == this)
            {
                _instance = null;
            }
        }

        private void EnsureLogAndOrchestrator()
        {
            _log ??= new RotatingFileLog(
                SourceGenBuilderConstants.LogFolderPath,
                SourceGenBuilderConstants.LogFileName,
                SourceGenBuilderConstants.PrevLogFileName);

            if (_orchestrator == null)
            {
                _orchestrator = new BuildOrchestrator(_log);
            }

            // Always re-subscribe so the event points at *this* window instance,
            // not a previous one from before a modal transition.
            _orchestrator.BuildFinished -= OnBuildFinished;
            _orchestrator.BuildFinished += OnBuildFinished;
        }

        private void OnBuildFinished(bool success)
        {
            _shouldReopenAsNormal = true;
        }

        // ── Modal transitions ───────────────────────────────────────────

        // Set by the modal when it closes itself (build done or cancel).
        // Read after ShowModalUtility() returns to decide whether to reopen.
        private bool _shouldReopenAsNormal;

        private void OpenAsModal()
        {
            Rect lastPos = position;
            var settingsRef = _settings;
            var logRef = _log;
            var orchestratorRef = _orchestrator;

            // Detach before Close() so OnDestroy doesn't cancel the in-flight build.
            _orchestrator = null;

            Close();
            _instance = null;

            // Run the modal + reopen flow outside of any OnGUI frame.
            EditorApplication.delayCall += () =>
            {
                var modal = CreateInstance<SourceGeneratorBuilderWindow>();
                modal.titleContent = new GUIContent("Source Gen Builder  (building…)");
                modal.minSize = new Vector2(500, 350);
                modal.position = lastPos;
                modal._log = logRef;
                modal._orchestrator = orchestratorRef;
                modal._settings = settingsRef;
                _instance = modal;

                // This BLOCKS until the modal is closed (build finishes, fails, or cancelled).
                modal.ShowModalUtility();

                // Only reopen if the modal closed itself (build done / cancel).
                // If the user force-closed it via the OS, _shouldReopenAsNormal is false.
                if (!modal._shouldReopenAsNormal)
                {
                    _instance = null;
                    return;
                }

                Rect modalPos = modal.position;
                _instance = null;

                var window = CreateInstance<SourceGeneratorBuilderWindow>();
                window.titleContent = new GUIContent("Source Gen Builder");
                window.minSize = new Vector2(500, 350);
                window._log = logRef;
                window._orchestrator = orchestratorRef;
                window._settings = settingsRef;
                window.position = modalPos;
                _instance = window;
                window.Show();
            };

            GUIUtility.ExitGUI();
        }

        // ── Settings asset management ───────────────────────────────────

        private void FindSettings()
        {
            if (_settings != null)
            {
                return;
            }

            string[] guids = AssetDatabase.FindAssets("t:SourceGenBuilderSettings");
            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                _settings = AssetDatabase.LoadAssetAtPath<SourceGenBuilderSettings>(path);
            }
        }

        private void BindSettings()
        {
            _reorderableList = null;
            _settingsSO = null;
            _profilesProp = null;

            if (_settings == null)
            {
                return;
            }

            _settingsSO = new SerializedObject(_settings);
            _profilesProp = _settingsSO.FindProperty("_profiles");

            _reorderableList = new ReorderableList(_settingsSO, _profilesProp, true, true, true, true)
            {
                drawHeaderCallback = rect =>
                {
                    EditorGUI.LabelField(rect, "Profiles (build order)");
                },

                drawElementCallback = (rect, index, isActive, isFocused) =>
                {
                    rect.y += 2;
                    rect.height = EditorGUIUtility.singleLineHeight;

                    var element = _profilesProp.GetArrayElementAtIndex(index);
                    EditorGUI.PropertyField(rect, element, GUIContent.none);
                },

                elementHeight = EditorGUIUtility.singleLineHeight + 6,
            };
        }

        // ── Cached styles ───────────────────────────────────────────────

        private void EnsureStyles()
        {
            if (_logStyle == null)
            {
                _logStyle = new GUIStyle(EditorStyles.label)
                {
                    richText = false,
                    wordWrap = true,
                    fontSize = 12,
                    font = EditorGUIUtility.Load("Fonts/RobotoMono/RobotoMono-Regular.ttf") as Font,
                    normal =
                    {
                        textColor = EditorGUIUtility.isProSkin
                            ? new Color(0.78f, 0.78f, 0.78f)
                            : new Color(0.15f, 0.15f, 0.15f),
                    },
                    padding = new RectOffset(4, 4, 2, 2),
                };
            }

            if (_buttonStyle == null)
            {
                _buttonStyle = new GUIStyle(GUI.skin.button)
                {
                    fontStyle = FontStyle.Bold,
                    fontSize = 11,
                    fixedHeight = 22,
                    margin = new RectOffset(3, 3, 3, 3),
                    padding = new RectOffset(8, 8, 3, 3),
                };
            }
        }

        // ── GUI ─────────────────────────────────────────────────────────

        private void OnGUI()
        {
            // If the build finished or was cancelled, close the modal before
            // any layout calls. This must happen on the Layout event so IMGUI
            // doesn't get confused by mismatched control counts on Repaint.
            if (_shouldReopenAsNormal && Event.current.type == EventType.Layout)
            {
                Close();
                GUIUtility.ExitGUI();
                return;
            }

            EnsureStyles();
            EnsureLogAndOrchestrator();

            bool isBuilding = _orchestrator.IsBuilding;

            if (isBuilding)
            {
                bool stillRunning = _orchestrator.Poll();
                if (stillRunning)
                {
                    Repaint();
                }
            }

            DrawToolbar(isBuilding);

            EditorGUILayout.BeginHorizontal();

            float totalWidth = position.width;
            float leftWidth = totalWidth * _splitterRatio;
            float rightWidth = totalWidth - leftWidth - 6;

            EditorGUILayout.BeginVertical(GUILayout.Width(leftWidth));
            DrawProfilePanel();
            EditorGUILayout.EndVertical();

            DrawSplitter();

            EditorGUILayout.BeginVertical(GUILayout.Width(rightWidth));
            DrawStatus(isBuilding);
            DrawBuildLog(isBuilding);
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
        }

        private void DrawSplitter()
        {
            Rect splitterRect = GUILayoutUtility.GetRect(6, 6, GUILayout.ExpandHeight(true));
            EditorGUIUtility.AddCursorRect(splitterRect, MouseCursor.ResizeHorizontal);

            if (Event.current.type == EventType.MouseDown && splitterRect.Contains(Event.current.mousePosition))
            {
                _draggingSplitter = true;
                Event.current.Use();
            }

            if (_draggingSplitter)
            {
                if (Event.current.type == EventType.MouseDrag)
                {
                    _splitterRatio = Mathf.Clamp(Event.current.mousePosition.x / position.width, 0.2f, 0.6f);
                    Repaint();
                    Event.current.Use();
                }

                if (Event.current.type == EventType.MouseUp)
                {
                    _draggingSplitter = false;
                    Event.current.Use();
                }
            }
        }

        private void DrawToolbar(bool isBuilding)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            GUILayout.Space(2);

            using (new EditorGUI.DisabledScope(isBuilding || _settings == null))
            {
                Color prevBg = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.3f, 0.75f, 0.35f);

                if (GUILayout.Button("▶  Build All", _buttonStyle, GUILayout.Width(90)))
                {
                    if (_orchestrator.StartBuild(_settings))
                    {
                        OpenAsModal();
                        return;
                    }
                }

                GUI.backgroundColor = prevBg;
            }

            if (isBuilding)
            {
                Color prevBg = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.9f, 0.35f, 0.3f);

                if (GUILayout.Button("■  Cancel", _buttonStyle, GUILayout.Width(90)))
                {
                    _orchestrator.Cancel();
                    _shouldReopenAsNormal = true;
                    Close();
                    GUIUtility.ExitGUI();
                }

                GUI.backgroundColor = prevBg;
            }

            GUILayout.FlexibleSpace();

            using (new EditorGUI.DisabledScope(isBuilding))
            {
                if (GUILayout.Button("Ping DLLs", _buttonStyle, GUILayout.Width(80)))
                {
                    PingDlls();
                }

                Color prevBg2 = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.6f, 0.6f, 0.65f);

                if (GUILayout.Button("Clear Log", _buttonStyle, GUILayout.Width(80)))
                {
                    _log.Clear();
                }

                GUI.backgroundColor = prevBg2;
            }

            GUILayout.Space(2);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawProfilePanel()
        {
            _profileScroll = EditorGUILayout.BeginScrollView(_profileScroll, GUILayout.ExpandHeight(true));

            EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            _settings = (SourceGenBuilderSettings)EditorGUILayout.ObjectField(
                _settings, typeof(SourceGenBuilderSettings), false);
            if (EditorGUI.EndChangeCheck())
            {
                BindSettings();
            }

            if (_settings == null)
            {
                EditorGUILayout.HelpBox(
                    "Assign a SourceGenBuilderSettings asset, or create one via:\n" +
                    "Assets → Create → Source Gen Builder → Settings",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.Space(4);

                if (_settingsSO == null || _reorderableList == null)
                {
                    BindSettings();
                }

                if (_settingsSO != null)
                {
                    _settingsSO.Update();
                    _reorderableList?.DoLayoutList();
                    _settingsSO.ApplyModifiedProperties();
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawStatus(bool isBuilding)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

            if (isBuilding)
            {
                var step = _orchestrator.CurrentStep;
                string snapshot = _orchestrator.LastLine ?? "Starting...";

                if (step.HasValue)
                {
                    string dllName = step.Value.Target.DllFileName;
                    string progress = $"[{_orchestrator.CurrentStepIndex + 1}/{_orchestrator.StepCount}] " +
                                      $"{step.Value.Profile.DisplayName} → {dllName}";

                    EditorGUILayout.LabelField($"⏳  {progress}: {snapshot}", EditorStyles.wordWrappedMiniLabel);
                }
                else
                {
                    EditorGUILayout.LabelField($"⏳  {snapshot}", EditorStyles.wordWrappedMiniLabel);
                }
            }
            else
            {
                if (_settings == null || _settings.Profiles.Count == 0)
                {
                    EditorGUILayout.LabelField("ℹ️  No profiles configured.", EditorStyles.wordWrappedMiniLabel);
                }
                else
                {
                    int installed = 0;
                    int total = 0;
                    string projectRoot = SourceGenBuilderConstants.ProjectRoot;

                    foreach (var profile in _settings.Profiles)
                    {
                        if (profile == null) continue;

                        foreach (var dep in profile.Dependencies)
                        {
                            string path = PathUtils.Normalize(dep.InstallDllPath);
                            if (string.IsNullOrEmpty(path)) continue;

                            total++;
                            if (File.Exists(Path.Combine(projectRoot, path)))
                            {
                                installed++;
                            }
                        }

                        string genPath = PathUtils.Normalize(profile.Generator.InstallDllPath);
                        if (!string.IsNullOrEmpty(genPath))
                        {
                            total++;
                            if (File.Exists(Path.Combine(projectRoot, genPath)))
                            {
                                installed++;
                            }
                        }
                    }

                    string icon = total == 0 ? "ℹ️" : installed == total ? "✅" : "⚠️";
                    string status = total == 0
                        ? "Profiles have no configured targets."
                        : $"{installed}/{total} DLLs installed.";

                    EditorGUILayout.LabelField($"{icon}  {status}", EditorStyles.wordWrappedMiniLabel);
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawBuildLog(bool isBuilding)
        {
            GUILayout.Label("Build Log", EditorStyles.boldLabel);

            _logScroll = EditorGUILayout.BeginScrollView(_logScroll, GUILayout.ExpandHeight(true));

            string log = _log.Content;
            float height = _logStyle.CalcHeight(new GUIContent(log), position.width * (1f - _splitterRatio) - 40);
            EditorGUILayout.SelectableLabel(log, _logStyle, GUILayout.ExpandWidth(true),
                GUILayout.Height(height));

            GUILayout.FlexibleSpace();

            EditorGUILayout.EndScrollView();

            if (_autoScroll && isBuilding)
            {
                _logScroll.y = float.MaxValue;
            }
        }

        // ── Ping ────────────────────────────────────────────────────────

        private void PingDlls()
        {
            if (_settings == null) return;

            foreach (var profile in _settings.Profiles)
            {
                if (profile == null) continue;

                string path = PathUtils.Normalize(profile.Generator.InstallDllPath);
                if (string.IsNullOrEmpty(path)) continue;

                var asset = AssetDatabase.LoadAssetAtPath<Object>(path);

                if (asset != null)
                {
                    EditorUtility.FocusProjectWindow();
                    EditorGUIUtility.PingObject(asset);
                    Selection.activeObject = asset;
                    return;
                }
            }

            _log.AppendTimestamped("No installed DLLs found.");
            Repaint();
        }
    }
}
