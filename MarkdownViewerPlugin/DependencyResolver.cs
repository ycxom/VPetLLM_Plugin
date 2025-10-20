using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace MarkdownViewerPlugin
{
    internal static class DependencyResolver
    {
        private static bool _initialized = false;
        private static readonly object _lock = new();

        public static void Ensure(params string[] extraProbeDirs)
        {
            if (_initialized) return;
            lock (_lock)
            {
                if (_initialized) return;

                // 计算常见探测目录：当前执行程序集目录 + 额外目录
                var execDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
                var probeDirs = new[] { execDir }
                    .Concat(extraProbeDirs ?? Array.Empty<string>())
                    .Where(d => !string.IsNullOrWhiteSpace(d))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
                {
                    try
                    {
                        var asmName = new AssemblyName(args.Name).Name + ".dll";
                        foreach (var dir in probeDirs)
                        {
                            var path = Path.Combine(dir, asmName);
                            if (File.Exists(path))
                            {
                                return Assembly.LoadFrom(path);
                            }
                            // 常见二级目录：plugin/
                            var pluginPath = Path.Combine(dir, "plugin", asmName);
                            if (File.Exists(pluginPath))
                            {
                                return Assembly.LoadFrom(pluginPath);
                            }
                        }
                    }
                    catch
                    {
                        // 忽略，返回 null 让默认解析继续
                    }
                    return null;
                };

                _initialized = true;
            }
        }
    }
}