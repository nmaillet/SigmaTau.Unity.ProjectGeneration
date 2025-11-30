using System.Collections.Generic;
using System.IO;
using System.Linq;
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
                GetStartArguments = (x) => $"--listen \"{x}\"",
            },
            new SigmaTauExecutable
            {
                Filename = "nvim-qt",
                Name = "Neovim-Qt Στ",
                GetStartArguments = (x) => $"-- --listen \"{x}\"",
            },
            new SigmaTauExecutable
            {
                Filename = "neovide",
                Name = "Neovide Στ",
                FocusCommand = "<cmd>NeovideFocus<cr>",
                GetStartArguments = (x) => $"-- --listen \"{x}\"",
            },
        };

        public static CodeEditor.Installation[] FindInstallations()
        {
            var installations = new List<CodeEditor.Installation>();

            foreach (var executable in _executables)
            {
                TryFindInstallationOnWindows(executable, installations);
            }

            return installations.ToArray();
        }

        private static void TryFindInstallationOnWindows(
            SigmaTauExecutable executable,
            List<CodeEditor.Installation> installations)
        {
            if (Application.platform is not RuntimePlatform.WindowsEditor)
            {
                return;
            }

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
                return;
            }

            if (process.ExitCode == 0)
            {
                string path = process.StandardOutput.ReadToEnd();
                installations.Add(new CodeEditor.Installation { Name = executable.Name, Path = path });
            }
        }

        public static SigmaTauExecutable TryGetExecutable(string editorPath)
        {
            // I'm assuming this would work the same on Linux, but not sure. No idea about MacOS.
            string filename = Path.GetFileNameWithoutExtension(editorPath);
            return _executables.FirstOrDefault((ed) => ed.Filename.Equals(filename, PathUtils.PathComparison));
        }
    }
}

