using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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

                // 注意：不能使用 File.ReadAllText！其内部以 FileShare.Read 打开文件（不含 Write 共享），
                // 而正在被写入的文件（如 TShock 正在写入的日志）已占用 Write 权限，会触发共享冲突异常：
                // "The process cannot access the file ... because it is being used by another process"。
                // 这里显式声明 FileShare.ReadWrite | FileShare.Delete，允许读取正在被写入的文件。
                string content;
                using (var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(fs, Encoding.UTF8, true))
                {
                    content = reader.ReadToEnd();
                }
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

            string fullPath;
            try
            {
                // 空 path 或 "/" → 根目录（TShock 程序目录）
                if (string.IsNullOrWhiteSpace(relativePath) || relativePath.Trim('/', '\\').Length == 0)
                    fullPath = RootDir;
                else
                    fullPath = ResolveSafePath(relativePath);

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

        /// <summary>
        /// 删除文件（仅文件，不删除目录）
        /// </summary>
        public static object DeleteFile(RestRequestArgs args)
        {
            var relativePath = args.Parameters["path"];
            if (string.IsNullOrEmpty(relativePath))
                return new RestObject("400") { { "error", "path is required" } };

            try
            {
                var fullPath = ResolveSafePath(relativePath);

                if (!File.Exists(fullPath))
                    return new RestObject("404") { { "error", "文件不存在" } };

                // 删除前 TOCTOU 防护：再次检查符号链接
                var fileInfo = new FileInfo(fullPath);
                if ((fileInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new Exception("Symbolic links are not allowed");

                File.Delete(fullPath);
                return new RestObject("200") { { "message", "文件删除成功" } };
            }
            catch (Exception ex)
            {
                return new RestObject("500") { { "error", ex.Message } };
            }
        }

        /// <summary>
        /// 上传文件（分片写入）。
        /// 参数：path（相对路径）、data（base64 片段）、append（"1"=追加 / "0"=覆盖，默认覆盖）
        /// 单片 base64 受 REST body 10MB 上限约束，前端按 ~4MB 二进制分片。
        /// </summary>
        public static object UploadFile(RestRequestArgs args)
        {
            var relativePath = args.Parameters["path"];
            var data = args.Parameters["data"];
            if (string.IsNullOrEmpty(relativePath) || string.IsNullOrEmpty(data))
                return new RestObject("400") { { "error", "path and data are required" } };

            var append = args.Parameters["append"] == "1";

            try
            {
                var fullPath = ResolveSafePath(relativePath);

                // 确保目标目录存在
                var dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                // 写入前 TOCTOU 防护：目标已存在且为符号链接时拒绝
                if (File.Exists(fullPath))
                {
                    var fileInfo = new FileInfo(fullPath);
                    if ((fileInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                        throw new Exception("Symbolic links are not allowed");
                }

                var bytes = Convert.FromBase64String(data);
                using (var fs = new FileStream(fullPath,
                    append ? FileMode.Append : FileMode.Create,
                    FileAccess.Write, FileShare.Read))
                {
                    fs.Write(bytes, 0, bytes.Length);
                }

                return new RestObject("200") { { "message", "写入成功" }, { "received", bytes.Length } };
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
