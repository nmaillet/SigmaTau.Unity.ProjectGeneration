using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SigmaTau.Unity.ProjectGeneration
{
    public static class PathUtils
    {
        private static readonly char[] PathSeparators = Path.DirectorySeparatorChar == Path.AltDirectorySeparatorChar
            ? new char[] { Path.DirectorySeparatorChar }
            : new char[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };

        public static readonly StringComparison PathComparison =
            Application.platform is RuntimePlatform.WindowsEditor or RuntimePlatform.OSXEditor
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        public static readonly string AssetFolderFullPath = Path.GetFullPath(Application.dataPath);

        public static readonly string ProjectFullPath = Path.GetDirectoryName(AssetFolderFullPath);

        public static readonly string ProjectName = Path.GetFileName(ProjectFullPath);

        public static bool IsDirectorySeparatorChar(char character)
        {
            return character == Path.DirectorySeparatorChar || character == Path.AltDirectorySeparatorChar;
        }

        public static string GetParentFolder(string path)
        {
            int index = path.LastIndexOfAny(PathSeparators);
            return index < 0 ? string.Empty : path[..index];
        }

        public static bool IsSameFile(string path1, string path2)
        {
            return string.Equals(Path.GetFullPath(path1), Path.GetFullPath(path2), PathComparison);
        }

        public static bool IsInAssetsFolder(string path)
        {
            return IsNestedInternal(AssetFolderFullPath, Path.GetFullPath(path));
        }

        public static bool IsNested(string rootPath, string nestedPath)
        {
            return IsNestedInternal(Path.GetFullPath(rootPath), Path.GetFullPath(nestedPath));
        }

        private static bool IsNestedInternal(string rootPath, string nestedPath)
        {
            return
                (
                    (nestedPath.Length > rootPath.Length && IsDirectorySeparatorChar(nestedPath[rootPath.Length]))
                    || nestedPath.Length == rootPath.Length
                )
                && nestedPath.StartsWith(rootPath, PathComparison);
        }

        public static string GetAbsoluteOrRelativePath(string relativeTo, string path)
        {
            return Path.IsPathFullyQualified(path) ? path : Path.GetRelativePath(relativeTo, path);
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
                    Debug.Log("Not in root folder");
                    Debug.Log(rootSourcePath);
                    return null;
                }
            }

            return rootSourcePath;
        }
    }
}

