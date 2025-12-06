using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.CodeEditor;
using UnityEditor;
using UnityEngine;

using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace SigmaTau.Unity.ProjectGeneration
{
    [InitializeOnLoad]
    public class SigmaTauCodeEditor : IExternalCodeEditor
    {
        private const int _startExecutableTimeoutMs = 5000;

        private const int _nvimCommandTimeoutMs = 2000;

        private static readonly object _staticSyncRoot = new();

        private static Task<CodeEditor.Installation[]> _findInstallationsTask;

        private string _currentPipeName = null;

        private readonly string _defaultPipeName;

        static SigmaTauCodeEditor()
        {
            // Using Task.Run() in a static constructor appears to cause a deadlock.
            EditorApplication.delayCall += () => GetFindInstallationsTask();
            CodeEditor.Register(new SigmaTauCodeEditor());
        }

        public SigmaTauCodeEditor()
        {
            _defaultPipeName = Application.platform is RuntimePlatform.WindowsEditor
                ? $"\\\\.\\pipe\\nvim.unity.{PathUtils.ProjectName}"
                : null;
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

        private void VerifyOrCreatePipe(string editorPath, SigmaTauExecutable executable)
        {
            if (!string.IsNullOrWhiteSpace(_currentPipeName) && File.Exists(_currentPipeName))
            {
                return;
            }

            _currentPipeName = null;

            if (!string.IsNullOrWhiteSpace(_defaultPipeName)
                && !string.Equals(_defaultPipeName, _currentPipeName, PathUtils.PathComparison)
                && File.Exists(_defaultPipeName))
            {
                ExecuteNvimRemote(_defaultPipeName, true, out string output, "--remote-expr", "getcwd()");
                if (!string.IsNullOrWhiteSpace(output) && PathUtils.IsSameFile(output, PathUtils.ProjectFullPath))
                {
                    Debug.LogFormat("Using default pipe name: {0}", _defaultPipeName);
                    _currentPipeName = _defaultPipeName;
                    return;
                }
            }

            IEnumerable<string> existingPipes;
            if (Application.platform is RuntimePlatform.WindowsEditor)
            {
                existingPipes = Directory.EnumerateFiles("\\\\.\\pipe\\", "*nvim*");
            }
            else
            {
                existingPipes = Enumerable.Empty<string>();
                Debug.LogWarningFormat("Platform {0} does not support searching for existing pipes",
                    Application.platform);
            }

            _currentPipeName = existingPipes.FirstOrDefault((pipeName) =>
            {
                if (string.Equals(pipeName, _defaultPipeName, PathUtils.PathComparison))
                {
                    return false;
                }
                ExecuteNvimRemote(pipeName, true, out string output, "--remote-expr", "getcwd()");
                return !string.IsNullOrWhiteSpace(output) && PathUtils.IsSameFile(output, PathUtils.ProjectFullPath);
            });

            if (!string.IsNullOrWhiteSpace(_currentPipeName))
            {
                Debug.LogFormat("Using existing pipe: {0}", _currentPipeName);
                return;
            }
            if (string.IsNullOrWhiteSpace(_defaultPipeName))
            {
                Debug.LogWarningFormat("Platform {0} does not have a default named pipe", Application.platform);
                return;
            }

            string arguments = executable.GetStartArguments(_currentPipeName);

            Debug.LogFormat("Starting editor '{0} {1}'", editorPath, arguments);
            using var process = Process.Start(editorPath, arguments);

            var timeout = Stopwatch.StartNew();
            while (!File.Exists(_defaultPipeName))
            {
                Thread.Sleep(100);
                if (timeout.ElapsedMilliseconds > _startExecutableTimeoutMs)
                {
                    Debug.LogWarningFormat("Timed out waiting for editor to open ({0}ms)", _startExecutableTimeoutMs);
                    process.Kill();
                    return;
                }
            }

            _currentPipeName = _defaultPipeName;
        }

        private static bool ExecuteNvimRemote(
            string pipeName,
            bool captureOutput,
            out string output,
            params string[] arguments)
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

            if (!process.WaitForExit(_nvimCommandTimeoutMs))
            {
                Debug.LogWarningFormat("<noparse>Command '{0}' with arguments '{1}' timed out ({2}ms)</noparse>",
                    startInfo.FileName, string.Join(' ', startInfo.ArgumentList), _nvimCommandTimeoutMs);
                process.Kill();
                output = null;
                return false;
            }

            if (process.ExitCode != 0)
            {
                Debug.LogWarningFormat(
                    "<noparse>Command '{0}' with arguments '{1}' failed with exit code {2}</noparse>",
                    startInfo.FileName, string.Join(' ', startInfo.ArgumentList), process.ExitCode);
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

            if (!Path.GetExtension(filePath).Equals(".cs", StringComparison.OrdinalIgnoreCase))
            {
                Debug.Log(Path.GetExtension(filePath));
                return false;
            }

            string editorPath = CodeEditor.CurrentEditorPath;
            var executable = SigmaTauInstallationLocator.TryGetExecutable(editorPath);
            if (executable is null)
            {
                Debug.LogWarningFormat("Could not find executable for editor path: {0}", editorPath);
                return false;
            }

            VerifyOrCreatePipe(editorPath, executable);
            if (string.IsNullOrWhiteSpace(_currentPipeName))
            {
                return false;
            }

            // Try to open the file.
            if (!ExecuteNvimRemote(_currentPipeName, false, out _, "--remote", filePath))
            {
                return false;
            }

            var commandStringBuilder = new StringBuilder("<c-\\><c-N>");
            bool executeCommand = false;
            if (line >= 1 && column >= 0)
            {
                commandStringBuilder.AppendFormat("<cmd>{0}<cr>{1}|", line, column);
                executeCommand = true;
            }
            if (executable.FocusCommand is not null)
            {
                commandStringBuilder.Append(executable.FocusCommand);
                executeCommand = true;
            }
            if (executeCommand)
            {
                // The file was already open, so if this failed, still return true.
                ExecuteNvimRemote(
                    _currentPipeName, false, out _, "--remote-send", commandStringBuilder.ToString());
            }

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
