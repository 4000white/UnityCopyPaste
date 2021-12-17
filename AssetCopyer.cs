using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using System;
using Object = UnityEngine.Object;

namespace UnityEditor
{
    public class AssetCopyer
    {
        //复制粘贴一个文件夹时，如果有资源引用了文件夹里的其它资源，新复制出来的资源的引用会替换新文件夹里对应的文件，而不是继续引用旧文件夹里的资源
        //执行这种操作的资源的后缀
        private static List<string> includeList = new List<string> { ".prefab", ".mat", ".anim", ".controller" };

        private const string META = ".meta";
#if UNITY_EDITOR_OSX
        private const string DS_STORE = ".DS_Store";
        private static List<string> ignoreList = new List<string> { META, DS_STORE };
#else
        private static List<string> ignoreList = new List<string> {META};
#endif
        private static List<string> copyPaths = new List<string>();
        [MenuItem("Assets/Copy", false, -10000)]
        static void AssetCopy()
        {
            var objs = Selection.GetFiltered<Object>(SelectionMode.Assets);
            copyPaths.Clear();
            foreach (var obj in objs)
            {
                copyPaths.Add(AssetDatabase.GetAssetPath(obj));
            }
        }
        [MenuItem("Assets/Paste", true)]
        static bool ValidAssetPaste()
        {
            return copyPaths.Count > 0;
        }
        [MenuItem("Assets/Paste", false, -10000)]
        static void AssetPaste()
        {
            var pastePosition = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (Path.HasExtension(pastePosition))
            {
                pastePosition = Path.GetDirectoryName(pastePosition);
            }
            foreach (var copyPath in copyPaths)
            {
                if (!string.IsNullOrEmpty(copyPath))
                {
                    var fileName = Path.GetFileName(copyPath);
                    var pastePath = pastePosition + "/" + fileName;
                    Copy(copyPath, pastePath);
                }
            }
        }

        public static void Copy(string copyPath, string pastePath)
        {
            Debug.Log("copy " + copyPath + " to " + pastePath);
            try
            {
                if (!string.IsNullOrEmpty(copyPath))
                {
                    if (Path.HasExtension(copyPath))
                    {
                        CopyFile(copyPath, pastePath);
                    }
                    else
                    {
                        CopyFolder(copyPath, pastePath);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.Log(e);
            }
        }

        //复制单个文件
        private static void CopyFile(string copyPath, string pastePath)
        {
            if (!Path.HasExtension(copyPath) || !Path.HasExtension(pastePath))
            {
                throw new Exception();
            }
            pastePath = NextAvailableFilename(pastePath);
            File.Copy(copyPath, pastePath);
            AssetDatabase.Refresh();
            AssetDatabase.SaveAssets();
            Selection.activeObject = AssetDatabase.LoadMainAssetAtPath(pastePath);
        }
        //复制文件夹
        private static void CopyFolder(string copyPath, string pastePath)
        {
            if (Path.HasExtension(copyPath) || Path.HasExtension(pastePath))
            {
                throw new Exception();
            }
            pastePath = NextAvailableFilename(pastePath);
            Debug.Log("pastePath " + pastePath);
            RecursivelyCopyFolder(copyPath, pastePath);
            AssetDatabase.Refresh();
            ReplaceReferences(copyPath, pastePath);
            AssetDatabase.Refresh();
            AssetDatabase.SaveAssets();
            Selection.activeObject = AssetDatabase.LoadMainAssetAtPath(pastePath);
        }
        //刚复制出来的新文件夹里的资源还是会有对旧文件夹中资源的引用，通过替换guid的方式，把引用替换成对新文件夹中对应文件的引用
        private static void ReplaceReferences(string oldPath, string newPath)
        {
            var guidMap = new Dictionary<string, string>();
            var fullPath = Path.GetFullPath(oldPath);
            var filePaths = GetAllFiles(fullPath);
            var length = fullPath.Length + 1;
            foreach (var filePath in filePaths)
            {
                string extension = Path.GetExtension(filePath);
                if (!ignoreList.Contains(extension))
                {
                    string assetPath = GetRelativeAssetPath(filePath);
                    string relativePath = filePath.Remove(0, length);
                    string guid = AssetDatabase.AssetPathToGUID(assetPath);
                    string copyPath = newPath + "/" + relativePath;
                    string copyGuid = AssetDatabase.AssetPathToGUID(copyPath);
                    if (copyGuid != null)
                    {
                        guidMap[guid] = copyGuid;
                    }
                }
            }

            fullPath = Path.GetFullPath(newPath);
            filePaths = GetAllFiles(fullPath);
            foreach (var filePath in filePaths)
            {
                string extension = Path.GetExtension(filePath);
                if (includeList.Contains(extension))
                {
                    var assetPath = GetRelativeAssetPath(filePath);
                    string[] deps = AssetDatabase.GetDependencies(assetPath, true);
                    var fileString = File.ReadAllText(filePath);
                    bool bChanged = false;
                    foreach (var v in deps)
                    {
                        var guid = AssetDatabase.AssetPathToGUID(v);
                        if (guidMap.ContainsKey(guid))
                        {
                            if (Regex.IsMatch(fileString, guid))
                            {
                                fileString = Regex.Replace(fileString, guid, guidMap[guid]);
                                bChanged = true;
                                var oldFile = AssetDatabase.GUIDToAssetPath(guid);
                                var newFile = AssetDatabase.GUIDToAssetPath(guidMap[guid]);
                            }
                        }
                    }
                    if (bChanged)
                    {
                        File.WriteAllText(filePath, fileString);
                    }
                }
            }
        }

        private static string GetRelativeAssetPath(string fullPath)
        {
            fullPath = fullPath.Replace("\\", "/");
            int index = fullPath.IndexOf("Assets");
            string relativePath = fullPath.Substring(index);
            return relativePath;
        }

        private static string[] GetAllFiles(string fullPath)
        {
            List<string> files = new List<string>();
            foreach (string file in GetFiles(fullPath))
            {
                files.Add(file);
            }
            return files.ToArray();
        }

        private static IEnumerable<string> GetFiles(string path)
        {
            Queue<string> queue = new Queue<string>();
            queue.Enqueue(path);
            while (queue.Count > 0)
            {
                path = queue.Dequeue();
                try
                {
                    foreach (string subDir in Directory.GetDirectories(path))
                    {
                        queue.Enqueue(subDir);
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex);
                }
                string[] files = null;
                try
                {
                    files = Directory.GetFiles(path);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex);
                }
                if (files != null)
                {
                    for (int i = 0; i < files.Length; i++)
                    {
                        yield return files[i];
                    }
                }
            }
        }

        static void RecursivelyCopyFolder(string sourcePath, string destPath)
        {
            if (sourcePath == destPath)
            {
                throw new Exception("sourcePath == destPath");
            }
            if (Directory.Exists(sourcePath))
            {
                if (!Directory.Exists(destPath))
                {
                    try
                    {
                        Directory.CreateDirectory(destPath);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError(ex);
                    }
                }

                List<string> files = new List<string>(Directory.GetFiles(sourcePath));
                files.ForEach(c =>
                {
#if UNITY_EDITOR_OSX
                    if (!c.EndsWith(META) && !c.EndsWith(DS_STORE))
#else
                    if (!c.EndsWith(META))
#endif
                    {
                        string destFile = Path.Combine(destPath, Path.GetFileName(c));
                        File.Copy(c, destFile, true);
                    }
                });
                List<string> folders = new List<string>(Directory.GetDirectories(sourcePath));
                folders.ForEach(c =>
                {
                    string destDir = Path.Combine(destPath, Path.GetFileName(c));
                    RecursivelyCopyFolder(c, destDir);
                });
            }
            else
            {
                throw new Exception("sourcePath is not exist!");
            }
        }

        private static string numberPattern = " ({0})";
        private static bool FileExist(string filePath, bool isFolder)
        {
            if (isFolder)
            {
                return Directory.Exists(filePath);
            }
            else
            {
                return File.Exists(filePath);
            }
        }
        //获取一个不重复的文件名，如 aa (1).prefab
        public static string NextAvailableFilename(string path)
        {
            bool isFolder = !Path.HasExtension(path);
            if (!FileExist(path, isFolder))
            {
                return path;
            }
            string tmp;
            if (Path.HasExtension(path))
            {
                tmp = path.Insert(path.LastIndexOf(Path.GetExtension(path)), numberPattern);
            }
            else
            {
                tmp = path + numberPattern;
            }
            return GetNextFilename(tmp, isFolder);
        }
        private static string GetNextFilename(string pattern, bool isFolder)
        {
            string tmp = string.Format(pattern, 1);
            if (tmp == pattern)
            {
                throw new ArgumentException("The pattern must include an index place-holder", "pattern");
            }

            if (!FileExist(tmp, isFolder))
            {
                return tmp;
            }

            int min = 1, max = 2;
            while (FileExist(string.Format(pattern, max), isFolder))
            {
                min = max;
                max *= 2;
            }

            while (max != min + 1)
            {
                int pivot = (max + min) / 2;

                if (FileExist(string.Format(pattern, pivot), isFolder))
                    min = pivot;
                else
                    max = pivot;
            }

            return string.Format(pattern, max);
        }
    }
}
