using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Rests;

namespace TShockData
{
    public static class FileManager
    {
        // 插件自身的运行目录（即TShock程序目录）
        private static readonly string RootDir = AppDomain.CurrentDomain.BaseDirectory;

        /// <summary>
        /// 安全解析路径：将相对路径解析为绝对路径，并校验是否在RootDir内
        /// </summary>
        private static string ResolveSafePath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                throw new Exception("Path is empty");

            // 拒绝绝对路径
            if (Path.IsPathRooted(relativePath))
                throw new Exception("Absolute path not allowed");

            // 去掉前导的 / 和 \（防止 Path.Combine 丢弃前面的参数）
            var cleaned = relativePath.TrimStart('/', '\\');

            // 拒绝任何包含 .. 或 . 的路径段
            var parts = cleaned.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (parts.Any(p => p == ".." || p == "."))
                throw new Exception("Path traversal detected");

            // 安全拼装
            var fullPath = Path.GetFullPath(Path.Combine(RootDir, cleaned));

            // 校验仍在根目录内
            if (!fullPath.StartsWith(RootDir, StringComparison.OrdinalIgnoreCase))
                throw new Exception("Path escapes root directory");

            // 检查是否存在符号链接（仅文件存在时）
            if (File.Exists(fullPath))
            {
                var fileInfo = new FileInfo(fullPath);
                if ((fileInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new Exception("Symbolic links are not allowed");
            }

            return fullPath;
        }

        public static object ReadFile(RestRequestArgs args)
        {
            var relativePath = args.Parameters["path"];
            if (string.IsNullOrEmpty(relativePath))
                return new RestObject("400") { { "error", "path is required" } };

            try
            {
                var fullPath = ResolveSafePath(relativePath);

                if (!File.Exists(fullPath))
                    return new RestObject("404") { { "error", "文件不存在" } };

                // 限制最大读取大小（5MB）
                var fileInfo = new FileInfo(fullPath);
                if (fileInfo.Length > 5 * 1024 * 1024)
                    return new RestObject("413") { { "error", "文件过大（超过5MB）" } };

                var content = File.ReadAllText(fullPath);
                return new RestObject("200") { { "content", content } };
            }
            catch (Exception ex)
            {
                return new RestObject("500") { { "error", ex.Message } };
            }
        }

        public static object WriteFile(RestRequestArgs args)
        {
            var relativePath = args.Parameters["path"];
            var content = args.Parameters["content"];

            if (string.IsNullOrEmpty(relativePath) || content == null)
                return new RestObject("400") { { "error", "path and content are required" } };

            try
            {
                var fullPath = ResolveSafePath(relativePath);

                // 确保目标目录存在
                var dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                // 再次检查是否为符号链接（写入前TOCTOU防护）
                if (File.Exists(fullPath))
                {
                    var fileInfo = new FileInfo(fullPath);
                    if ((fileInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                        throw new Exception("Symbolic links are not allowed");
                }

                File.WriteAllText(fullPath, content);
                return new RestObject("200") { { "message", "文件保存成功" } };
            }
            catch (Exception ex)
            {
                return new RestObject("500") { { "error", ex.Message } };
            }
        }

        public static object ListDirectory(RestRequestArgs args)
        {
            var relativePath = args.Parameters["path"];
            if (string.IsNullOrEmpty(relativePath))
                return new RestObject("400") { { "error", "path is required" } };

            try
            {
                var fullPath = ResolveSafePath(relativePath);

                if (!Directory.Exists(fullPath))
                    return new RestObject("404") { { "error", "目录不存在" } };

                var entries = new List<object>();

                foreach (var dir in Directory.GetDirectories(fullPath))
                {
                    var dirInfo = new DirectoryInfo(dir);
                    entries.Add(new Dictionary<string, object>
                    {
                        { "name", dirInfo.Name },
                        { "type", "dir" }
                    });
                }

                foreach (var file in Directory.GetFiles(fullPath))
                {
                    var fileInfo = new FileInfo(file);
                    entries.Add(new Dictionary<string, object>
                    {
                        { "name", fileInfo.Name },
                        { "type", "file" },
                        { "size", fileInfo.Length }
                    });
                }

                return new RestObject("200") { { "entries", entries } };
            }
            catch (Exception ex)
            {
                return new RestObject("500") { { "error", ex.Message } };
            }
        }

        public static object GetDirectoryTree(RestRequestArgs args)
        {
            var relativePath = args.Parameters["path"] ?? "";
            var depthStr = args.Parameters["depth"] ?? "2";

            if (!int.TryParse(depthStr, out var maxDepth))
                maxDepth = 2;

            if (maxDepth < 1 || maxDepth > 5)
                maxDepth = 2;

            try
            {
                var fullPath = ResolveSafePath(relativePath);
                var tree = BuildTree(fullPath, 0, maxDepth);
                return new RestObject("200") { { "tree", tree } };
            }
            catch (Exception ex)
            {
                return new RestObject("500") { { "error", ex.Message } };
            }
        }

        private static List<object> BuildTree(string directory, int currentDepth, int maxDepth)
        {
            var result = new List<object>();

            if (!Directory.Exists(directory) || currentDepth >= maxDepth)
                return result;

            foreach (var dir in Directory.GetDirectories(directory))
            {
                var dirInfo = new DirectoryInfo(dir);
                result.Add(new Dictionary<string, object>
                {
                    { "name", dirInfo.Name },
                    { "type", "dir" },
                    { "children", BuildTree(dir, currentDepth + 1, maxDepth) }
                });
            }

            foreach (var file in Directory.GetFiles(directory))
            {
                var fileInfo = new FileInfo(file);
                result.Add(new Dictionary<string, object>
                {
                    { "name", fileInfo.Name },
                    { "type", "file" },
                    { "size", fileInfo.Length }
                });
            }

            return result;
        }
    }
}
