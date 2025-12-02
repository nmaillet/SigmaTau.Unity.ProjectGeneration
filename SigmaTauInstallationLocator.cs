using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Unity.CodeEditor;
using UnityEngine;

namespace SigmaTau.Unity.ProjectGeneration
{
    public static class SigmaTauInstallationLocator
    {
        private static readonly SigmaTauExecutable[] _executables = new[]
        {
            new SigmaTauExecutable
            {
                Filename = "nvim",
                Name = "Neovim Στ",
                GetStartArguments = (pipeName) => $"--listen \"{pipeName}\"",
            },
            new SigmaTauExecutable
            {
                Filename = "nvim-qt",
                Name = "Neovim-Qt Στ",
                GetStartArguments = (pipeName) => $"-- --listen \"{pipeName}\"",
            },
            new SigmaTauExecutable
            {
                Filename = "neovide",
                Name = "Neovide Στ",
                FocusCommand = "<cmd>NeovideFocus<cr>",
                GetStartArguments = (pipeName) => $"-- --listen \"{pipeName}\"",
            },
        };

        public static CodeEditor.Installation[] FindInstallations()
        {
            Func<SigmaTauExecutable, CodeEditor.Installation> tryFindInstallation =
                Application.platform is RuntimePlatform.WindowsEditor ? TryFindInstallationOnWindows
                    : null;

            if (tryFindInstallation is null)
            {
                return Array.Empty<CodeEditor.Installation>();
            }

            var tasks = _executables.Select((executable) => Task.Run(() => tryFindInstallation(executable))).ToArray();
            try
            {
                Task.WaitAll(tasks);
            }
            catch
            {
            }

            return tasks.Select((t) => t.IsCompletedSuccessfully ? t.Result : default)
                .Where((i) => !string.IsNullOrWhiteSpace(i.Name))
                .ToArray();
        }

        private static CodeEditor.Installation TryFindInstallationOnWindows(SigmaTauExecutable executable)
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "where",
                Arguments = executable.Filename,
                CreateNoWindow = true,
                ErrorDialog = false,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };

            using var process = System.Diagnostics.Process.Start(startInfo);

            if (!process.WaitForExit(5000))
            {
                Debug.LogWarningFormat("Timed out searching for '{0}'", executable.Filename);
                process.Kill();
                return default;
            }

            if (process.ExitCode == 0)
            {
                string path = process.StandardOutput.ReadToEnd();
                if (!string.IsNullOrWhiteSpace(path))
                {
                    return new CodeEditor.Installation { Name = executable.Name, Path = path };
                }
            }

            return default;
        }

        public static SigmaTauExecutable TryGetExecutable(string editorPath)
        {
            // I'm assuming this would work the same on Linux, but not sure. No idea about MacOS.
            string filename = Path.GetFileNameWithoutExtension(editorPath);
            return _executables.FirstOrDefault((executable) =>
                executable.Filename.Equals(filename, StringComparison.OrdinalIgnoreCase));
        }
    }
}

