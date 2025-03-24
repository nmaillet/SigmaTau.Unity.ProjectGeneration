using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Unity.CodeEditor;
using UnityEditor;
using UnityEngine;

namespace SigmaTau.Unity.ProjectGeneration
{
    [InitializeOnLoad]
    public class SigmaTauCodeEditor : IExternalCodeEditor
    {
        private const string _roslynAnalyzerKey = "sigma_tau_code_editor_roslyn_analyzer_path";

        private static readonly object _staticSyncRoot = new();

        private static Task<CodeEditor.Installation[]> _findInstallationsTask;

        private string _roslynAnalyzerPath;

        private string RoslynAnalyzerPath
        {
            get
            {
                // Only fetch it the first time (we trim the string when persisting which would cause issues if we read
                // every time).
                _roslynAnalyzerPath ??= EditorPrefs.GetString(_roslynAnalyzerKey);
                return _roslynAnalyzerPath;
            }
            set
            {
                // Always update the backing field; only persist if the trimmed value has changed.
                value ??= string.Empty;
                var valueSpan = value.AsSpan().Trim();
                if (
                    _roslynAnalyzerPath is null
                    || !valueSpan.Equals(_roslynAnalyzerPath.AsSpan().Trim(), StringComparison.Ordinal)
                )
                {
                    EditorPrefs.SetString(_roslynAnalyzerKey, valueSpan.ToString());
                }

                _roslynAnalyzerPath = value;
            }
        }

        static SigmaTauCodeEditor()
        {
            // Using Task.Run() in a static constructor appears to cause a deadlock.
            EditorApplication.delayCall += () => GetFindInstallationsTask();
            CodeEditor.Register(new SigmaTauCodeEditor());
        }

        private static Task<CodeEditor.Installation[]> GetFindInstallationsTask()
        {
            // Check first so we can skip getting the lock after the initial run of this.
            if (_findInstallationsTask is null)
            {
                lock (_staticSyncRoot)
                {
                    _findInstallationsTask ??= Task.Run(SigmaTauInstallationLocator.FindInstallations);
                }
            }

            return _findInstallationsTask;
        }

        public CodeEditor.Installation[] Installations
        {
            get
            {
                return GetFindInstallationsTask().Result;
            }
        }

        public void Initialize(string editorInstallationPath)
        {
            // This seems to get called quite frequently when getting updates via the GUI, so probably best to not do
            // much here.
        }

        public void OnGUI()
        {
            // The DLL is available as a Nuget package: https://www.nuget.org/packages/Microsoft.Unity.Analyzers
            // Unity doesn't play well with Nuget packages normally, but maybe this could work, need to test.
            RoslynAnalyzerPath = EditorGUILayout.TextField("Additional Roslyn Analyzer", RoslynAnalyzerPath);

            GUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(" ");
            if (GUILayout.Button("Browse", GUILayout.Width(85)))
            {
                string result = EditorUtility.OpenFilePanel("Additional Roslyn Analyzer", string.Empty, string.Empty);
                if (!string.IsNullOrWhiteSpace(result))
                {
                    RoslynAnalyzerPath = result;
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(" ");
            if (GUILayout.Button("Generate Project Files", GUILayout.MaxWidth(200)))
            {
                SyncAll();
            }
            GUILayout.EndHorizontal();
        }

        public bool OpenProject(string filePath = "", int line = -1, int column = -1)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return false;
            }

            string editorPath = CodeEditor.CurrentEditorPath;
            var executable = SigmaTauInstallationLocator.TryGetExecutable(editorPath);
            if (executable is null)
            {
                return false;
            }

            string serverName = @"\\.\pipe\nvim.sigmatau.BajouOne";

            var commands = new List<string> { $"n {Path.GetFullPath(filePath)}" };

            if (line >= 1 && column >= 0)
            {
                commands.Add($"lua vim.api.nvim_win_set_cursor(0, {{{line}, {column}}})");
            }

            if (!string.IsNullOrWhiteSpace(executable.FocusCommand))
            {
                commands.Add(executable.FocusCommand);
            }

            if (File.Exists(serverName))
            {
                // Seems like the project is already open, use nvim remote to send the commands over.
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "nvim",
                    Arguments = string.Format(
                        "--server {0} --remote-send \"<C-\\><C-N><CMD>{1}<CR>",
                        serverName,
                        string.Join("<CR><CMD>", commands)
                    ),
                    CreateNoWindow = true,
                    ErrorDialog = false,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                };

                using var process = System.Diagnostics.Process.Start(startInfo);

                if (!process.WaitForExit(10_000))
                {
                    Debug.LogWarning("Neovim remote send timed out");
                    return false;
                }

                if (process.ExitCode != 0)
                {
                    Debug.LogWarningFormat("Neovim remote send invalid exit code: {0}", process.ExitCode);
                    return false;
                }
            }
            else
            {
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = editorPath,
                    Arguments = string.Format(
                        "{0}--listen {1} +\"{2}\"",
                        executable.ArgumentPrefix,
                        serverName,
                        string.Join(" | ", commands)
                    ),
                };
                using var _ = System.Diagnostics.Process.Start(startInfo);
            }

            return true;
        }

        public void SyncAll()
        {
            string[] analyzers = string.IsNullOrWhiteSpace(RoslynAnalyzerPath)
                ? Array.Empty<string>()
                : new string[] { RoslynAnalyzerPath };
            var projectGeneration = new SigmaTauProjectGeneration(new SigmaTauProjectGenerationOptions
            {
                IncludePackages = false,
                Analyzers = analyzers,
                ProjectTypeGuid = "{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}",
                CapabilitiesToRemove = new string[]
                {
                    "LaunchProfiles",
                    "SharedProjectReferences",
                    "ReferenceManagerSharedProjects",
                    "ProjectReferences",
                    "ReferenceManagerProjects",
                    "COMReferences",
                    "ReferenceManagerCOM",
                    "AssemblyReferences",
                    "ReferenceManagerAssemblies",
                },
            });
            projectGeneration.GenerateProjectFiles();
        }

        public void SyncIfNeeded(string[] addedFiles, string[] deletedFiles, string[] movedFiles, string[] movedFromFiles, string[] importedFiles)
        {
            SyncAll();
        }

        public bool TryGetInstallationForPath(string editorPath, out CodeEditor.Installation installation)
        {
            var executable = SigmaTauInstallationLocator.TryGetExecutable(editorPath);
            installation = new CodeEditor.Installation { Name = executable?.Name, Path = editorPath };
            return executable is not null;
        }
    }
}
