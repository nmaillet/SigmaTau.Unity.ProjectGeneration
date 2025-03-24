using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SigmaTau.Unity.ProjectGeneration
{
    public static class PathUtils
    {
        private static readonly char[] _pathSeparators = new char[] { '\\', '/' };

        public static readonly StringComparison PathComparison =
            Application.platform is RuntimePlatform.WindowsEditor
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        public static string GetParentFolder(string path)
        {
            int index = path.LastIndexOfAny(_pathSeparators);
            return index < 0 ? string.Empty : path[..index];
        }

        public static bool IsInAssetsFolder(string path)
        {
            return path is not null
                && path.StartsWith("Assets", PathComparison)
                && (path.Length == 6 || _pathSeparators.Contains(path[6]));
        }

        public static bool IsSubfolder(string rootPath, string subPath)
        {
            return subPath.Contains(rootPath, PathComparison);
        }

        public static string TryFindRootPathOfAllFiles(IEnumerable<string> files)
        {
            string rootSourcePath = null;

            foreach (string sourceFile in files)
            {
                string folderPath = GetParentFolder(sourceFile);
                rootSourcePath ??= folderPath;

                while (
                    rootSourcePath.Length > 0
                    && folderPath.Length > 0
                    && !string.Equals(rootSourcePath, folderPath, PathComparison)
                )
                {
                    if (rootSourcePath.Length > folderPath.Length)
                    {
                        rootSourcePath = GetParentFolder(rootSourcePath);
                    }
                    else
                    {
                        folderPath = GetParentFolder(folderPath);
                    }
                }

                if (!IsInAssetsFolder(rootSourcePath))
                {
                    return null;
                }
            }

            return rootSourcePath;
        }
    }
}

