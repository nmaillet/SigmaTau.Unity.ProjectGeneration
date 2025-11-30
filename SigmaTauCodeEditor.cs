using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.CodeEditor;
using UnityEditor;
using UnityEngine;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace SigmaTau.Unity.ProjectGeneration
{
    [InitializeOnLoad]
    public class SigmaTauCodeEditor : IExternalCodeEditor
    {
        private static readonly object _staticSyncRoot = new();

        private static Task<CodeEditor.Installation[]> _findInstallationsTask;

        private string _currentPipeName = null;

        private Process _process;

        private readonly string _defaultPipeName;

        static SigmaTauCodeEditor()
        {
            // Using Task.Run() in a static constructor appears to cause a deadlock.
            EditorApplication.delayCall += () => GetFindInstallationsTask();
            CodeEditor.Register(new SigmaTauCodeEditor());
        }

        public SigmaTauCodeEditor()
        {
            string projectName = Path.GetFileName(Path.GetDirectoryName(Application.dataPath));
            _defaultPipeName = $"\\\\.\\pipe\\nvim.unity.{projectName}";
        }

        [MenuItem("SigmaTau/Generate Project Files", isValidateFunction: true)]
        public static bool IsGenerateProjectFilesMenuItemValid()
        {
            return CodeEditor.Editor.CurrentCodeEditor is SigmaTauCodeEditor;
        }

        [MenuItem("SigmaTau/Generate Project Files")]
        public static void GenerateProjectFilesMenuItem()
        {
            CodeEditor.Editor.CurrentCodeEditor.SyncAll();
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

        public CodeEditor.Installation[] Installations => GetFindInstallationsTask().Result;

        public void Initialize(string editorInstallationPath)
        {
            // This seems to get called quite frequently when getting updates via the GUI, so probably best to not do
            // much here.
        }

        public void OnGUI()
        {
            GUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(" ");
            if (GUILayout.Button("Generate Project Files", GUILayout.MaxWidth(200)))
            {
                SyncAll();
            }
            GUILayout.EndHorizontal();
        }

        private bool TryFindExistingNamedPipe()
        {
            if (File.Exists(_defaultPipeName))
            {
                _currentPipeName = _defaultPipeName;
                return true;
            }

            string[] existingPipes = Array.Empty<string>();

            if (Application.platform is RuntimePlatform.WindowsEditor)
            {
                existingPipes = Directory.GetFiles("\\\\.\\pipe\\", "*nvim*");
            }

            _currentPipeName = existingPipes.FirstOrDefault((pipeName) =>
            {
                if (
                    !ExecuteNvimRemote(pipeName, 5000, true, out string output, "--remote-expr", "getcwd()")
                    || string.IsNullOrWhiteSpace(output)
                )
                {
                    return false;
                }

                string processCwd = Path.GetFullPath(output);
                string projectPath = Path.GetFullPath(output);
                Debug.LogFormat("Comparing paths: {0} == {1}", processCwd, projectPath);
                return string.Equals(processCwd, projectPath, PathUtils.PathComparison);
            });

            return _currentPipeName is not null;
        }

        private bool StartEditorProcess(string editorPath, SigmaTauExecutable executable)
        {
            _process?.Dispose();
            _process = null;
            _currentPipeName = _defaultPipeName;

            string arguments = executable.GetStartArguments(_currentPipeName);
            Debug.LogFormat("Starting editor '{0} {1}'", editorPath, arguments);
            Process process = null;

            try
            {
                process = Process.Start(editorPath, arguments);
                using var cts = new CancellationTokenSource(5000);
                while (!File.Exists(_defaultPipeName))
                {
                    if (cts.IsCancellationRequested)
                    {
                        Debug.LogWarning("Timed out waiting for editor to open");
                        process.Kill();
                        return false;
                    }
                }

                // Move process to the field to is can be tracked and clear the local variable so it won't be disposed.
                _process = process;
                process = null;
                return true;
            }
            finally
            {
                process?.Dispose();
            }
        }

        private static bool ExecuteNvimRemote(
            string pipeName,
            int timeoutMs,
            bool captureOutput,
            out string output,
            params string[] arguments
        )
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "nvim",
                ArgumentList = { "--headless", "--server", pipeName },
                CreateNoWindow = true,
                ErrorDialog = false,
                UseShellExecute = false,
                RedirectStandardOutput = captureOutput,
            };

            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);

            if (!process.WaitForExit(timeoutMs))
            {
                process.Kill();
                output = null;
                return false;
            }

            if (process.ExitCode != 0)
            {
                output = null;
                return false;
            }

            output = captureOutput ? process.StandardOutput.ReadToEnd() : null;
            return true;
        }

        public bool OpenProject(string filePath = "", int line = -1, int column = -1)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return false;
            }

            if (!Path.GetExtension(filePath).Equals("cs", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            Debug.LogFormat("Opening file {0}", filePath);

            string editorPath = CodeEditor.CurrentEditorPath;
            var executable = SigmaTauInstallationLocator.TryGetExecutable(editorPath);
            if (executable is null)
            {
                Debug.LogWarningFormat("Could not find executable for editor path: {0}", editorPath);
                return false;
            }

            if (_currentPipeName is null)
            {
                // First attempt to open a file, try to either find an existing open pipe, or start a new instance.
                if (!TryFindExistingNamedPipe() && !StartEditorProcess(editorPath, executable))
                {
                    return false;
                }
            }
            else if (_process is not null && _process.HasExited)
            {
                if (!StartEditorProcess(editorPath, executable))
                {
                    return false;
                }
            }

            if (!ExecuteNvimRemote(_currentPipeName, 2000, false, out string _, "--remote", filePath))
            {
            }

            string command = "<c-\\><c-N>";
            bool executeCommand = false;
            if (line >= 1 && column >= 0)
            {
                command += $"<cmd>{line}<cr>{column + 1}|";
                executeCommand = true;
            }
            if (executable.FocusCommand is not null)
            {
                command += executable.FocusCommand;
                executeCommand = true;
            }
            if (executeCommand)
            {
                // using var commandProcess = ExecuteNvimRemote("--remote-send", command);

            }

            // if (!process.WaitForExit(10_000))
            // {
            //     Debug.LogWarning("Neovim remote send timed out");
            //     process.Kill();
            //     return false;
            // }

            return true;
        }

        public void SyncAll()
        {
            using var projectGeneration = new SigmaTauProjectGeneration(new SigmaTauProjectGenerationOptions
            {
                IncludePackages = false,
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
