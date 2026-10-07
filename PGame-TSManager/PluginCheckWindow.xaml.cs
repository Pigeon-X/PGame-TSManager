using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Windows;

namespace PGameTSManager
{
    public sealed class PluginCheckRow
    {
        public string Name { get; set; } = "";
        public string Version { get; set; } = "";
        public string Path { get; set; } = "";
        public string DependencyText { get; set; } = "正常";
    }

    public partial class PluginCheckWindow : Window
    {
        private readonly string _pluginDir;
        private readonly string _binDir;
        private static readonly HashSet<string> IgnoredRefs = new(StringComparer.OrdinalIgnoreCase)
        {
            "System", "System.Core", "System.Runtime", "System.Private.CoreLib", "mscorlib",
            "netstandard", "TShockAPI", "Terraria", "OTAPI", "OTAPI.Runtime",
            "TerrariaServerAPI", "MonoMod", "MonoMod.RuntimeDetour", "Newtonsoft.Json",
            "MySql.Data", "Microsoft.Data.Sqlite"
        };

        public PluginCheckWindow(string pluginDir, string binDir)
        {
            InitializeComponent();
            _pluginDir = pluginDir;
            _binDir = binDir;
            SourceInitialized += (_, _) =>
            {
                var lightTheme = App.IsLightTheme();
                WindowChromeHelper.Apply(this, !lightTheme,
                    WindowChromeHelper.Bgr(lightTheme ? "#EDE9FE" : "#1B1533"),
                    WindowChromeHelper.Bgr(lightTheme ? "#A855F7" : "#6D28D9"));
            };
            Refresh();
        }

        private void Refresh_Click(object _, RoutedEventArgs e) => Refresh();

        private void Refresh()
        {
            var rows = new List<PluginCheckRow>();
            try
            {
                var available = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var file in Directory.Exists(_pluginDir) ? Directory.GetFiles(_pluginDir, "*.dll") : Array.Empty<string>())
                    available.Add(Path.GetFileNameWithoutExtension(file));
                foreach (var file in Directory.Exists(_binDir) ? Directory.GetFiles(_binDir, "*.dll") : Array.Empty<string>())
                    available.Add(Path.GetFileNameWithoutExtension(file));

                var files = Directory.Exists(_pluginDir)
                    ? Directory.GetFiles(_pluginDir, "*.dll")
                    : Array.Empty<string>();
                foreach (var file in files.OrderBy(x => x))
                {
                    var asmName = AssemblyName.GetAssemblyName(file);
                    var references = ReadReferences(file)
                        .Where(x => !x.StartsWith("System.", StringComparison.OrdinalIgnoreCase) &&
                                    !x.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase) &&
                                    !IgnoredRefs.Contains(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    var missing = references.Where(x => !available.Contains(x)).ToList();
                    rows.Add(new PluginCheckRow
                    {
                        Name = asmName.Name ?? Path.GetFileNameWithoutExtension(file),
                        Version = asmName.Version?.ToString() ?? "-",
                        Path = file,
                        DependencyText = missing.Count == 0
                            ? "依赖正常"
                            : "可能缺失：" + string.Join(", ", missing)
                    });
                }
                PluginList.ItemsSource = rows;
                StatusText.Text = $"共 {rows.Count} 个插件，发现 {rows.Count(x => x.DependencyText != "依赖正常")} 个需检查";
            }
            catch (Exception ex)
            {
                StatusText.Text = "检查失败：" + ex.Message;
            }
        }

        private static IEnumerable<string> ReadReferences(string file)
        {
            using var stream = File.OpenRead(file);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata) yield break;
            var md = pe.GetMetadataReader();
            foreach (var handle in md.AssemblyReferences)
            {
                var reference = md.GetAssemblyReference(handle);
                yield return md.GetString(reference.Name);
            }
        }

        private void OpenPlugin_Click(object _, RoutedEventArgs e) => OpenFolder(_pluginDir);
        private void OpenBin_Click(object _, RoutedEventArgs e) => OpenFolder(_binDir);

        private static void OpenFolder(string path)
        {
            try { if (Directory.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true }); }
            catch { }
        }

        private void Close_Click(object _, RoutedEventArgs e) => Close();
    }
}
