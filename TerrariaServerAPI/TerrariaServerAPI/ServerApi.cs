using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using GameLauncher;
using HarmonyLib;
using Newtonsoft.Json;
using Terraria;
using ConsoleColor = System.ConsoleColor;

namespace TerrariaApi.Server
{
    public static partial class ServerApi
    {
        public static class Program
        {
	        private static readonly Harmony instance = new Harmony("SetColorHooker");
			
	        public static void SetBColor(ConsoleColor value)
			{
				Console.WriteLine($"\u0001bgclr{value}");
			}

			public static void SetFColor(ConsoleColor value)
			{
				switch (value)
				{
					case ConsoleColor.Gray:
						Console.WriteLine($"\u0001fgclrLightGray");
						break;
					case ConsoleColor.DarkGray:
						Console.WriteLine($"\u0001fgclrGray");
						break;
					default:
						Console.WriteLine($"\u0001fgclr{value}");
						break;
				}
			}

	        public static void SetTitle(string value)
	        {
				Console.WriteLine($"\u0001title{value}");
	        }

			public static void ResetColor()
			{
				SetFColor(ConsoleColor.Gray);
				SetBColor(ConsoleColor.Black);
			}

			static Program()
			{
				instance.Patch(typeof(Console).GetProperty(nameof(Console.ForegroundColor))?.SetMethod,
					new HarmonyMethod(typeof(Program).GetMethod(nameof(SetFColor))));
				instance.Patch(typeof(Console).GetProperty(nameof(Console.BackgroundColor))?.SetMethod,
					new HarmonyMethod(typeof(Program).GetMethod(nameof(SetBColor))));
				instance.Patch(typeof(Console).GetProperty(nameof(Console.Title))?.SetMethod,
					new HarmonyMethod(typeof(Program).GetMethod(nameof(SetTitle))));
				instance.Patch(typeof(Console).GetMethod(nameof(Console.ResetColor)),
					new HarmonyMethod(typeof(Program).GetMethod(nameof(ResetColor))));
				ResetColor();
			}

            public static void Main(string[] args)
            {
                AppDomain.CurrentDomain.UnhandledException += (_, a) => Console.WriteLine(a.ExceptionObject.ToString());
                var config = JsonConvert.DeserializeObject<ServerConfig>(File.ReadAllText(args[0]));
                ServerPluginsDirectoryPath = args[1];
                var otapi = typeof(Main).Assembly;
                var targs = config.CreateArgs(args[2]);
                var parent = Process.GetProcessById(int.Parse(args[4]));
                parent.EnableRaisingEvents = true;
                parent.Exited += (_, __) => Environment.Exit(1);

                TerrariaApi.Server.Program.InitialiseInternals();

                Hooks.AttachOTAPIHooks(targs);
                if (targs.Any(x => x == "-skipassemblyload"))
                {
                    Terraria.Main.SkipAssemblyLoad = true;
                }

                AppDomain.CurrentDomain.AssemblyResolve += (sender, sargs) =>
                {
                    var resourceName = new AssemblyName(sargs.Name).Name + ".dll";
                    var text = Array.Find(typeof(Main).Assembly.GetManifestResourceNames(),
                        element => element.EndsWith(resourceName));
                    if (text == null) return null;
                    Assembly result;
                    using (Stream manifestResourceStream = typeof(Main).Assembly.GetManifestResourceStream(text))
                    {
                        var array = new byte[manifestResourceStream.Length];
                        manifestResourceStream.Read(array, 0, array.Length);
                        result = Assembly.Load(array);
                    }

                    return result;
                };

                OTAPI.Hooks.Command.StartCommandThread = null;
                OTAPI.Hooks.Game.PreInitialize = () =>
                {
                    try
                    {
                        Console.WriteLine("TerrariaAPI Version: {0} (Protocol {1} ({2}), OTAPI {3})",
                            ApiVersion,
                            Terraria.Main.versionNumber2, Terraria.Main.curRelease, otapi.GetName()?.Version);
                        Initialize(args, Terraria.Main.instance);
                    }
                    catch (Exception ex)
                    {
                        var logWriter = LogWriter;
                        var str = "Startup aborted due to an exception in the Server API initialization:\n";
                        var ex2 = ex;
                        logWriter.ServerWriteLine(str + ((ex2 != null) ? ex2.ToString() : null), TraceLevel.Error);
                        Console.ReadLine();
                    }

                    Hooks.GameInitialize.Invoke(EventArgs.Empty);
                };

                void Initialize(string[] commandLineArgs, Main game)
                {
                    Profiler.BeginMeasureServerInitTime();
                    LogWriter.ServerWriteLine($"TerrariaApi - Server v{ApiVersion} started.",
                        TraceLevel.Verbose);
                    LogWriter.ServerWriteLine("\tCommand line: " + Environment.CommandLine,
                        TraceLevel.Verbose);
                    LogWriter.ServerWriteLine(
                        $"\tOS: {Environment.OSVersion} (64bit: {Environment.Is64BitOperatingSystem})",
                        TraceLevel.Verbose);
                    LogWriter.ServerWriteLine("\tMono: " + RunningMono.ToString(),
                        TraceLevel.Verbose);
                    ServerApi.game = game;
                    HandleCommandLine(commandLineArgs);

					AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;
                    Console.InputEncoding = Encoding.UTF8;
                    Console.OutputEncoding = Encoding.UTF8;
                    Console.SetIn(new TextWrapper(Console.In));
                    Console.ReadLine();
                    LoadPlugins();
                }

                void LoadPlugins()
                {
                    var list2 = config.plugins.Select(p => new FileInfo($"{Path.Combine(ServerPluginsDirectoryPath, p)}.dll"))
                        .ToList();
                    var dictionary = new Dictionary<TerrariaPlugin, Stopwatch>();
                    var loadedPlugins = new Dictionary<Assembly, string>();
                    foreach (var fileInfo in list2)
                    {
                        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileInfo.Name);
                        try
                        {
	                        if (!loadedAssemblies.TryGetValue(fileNameWithoutExtension, out var assembly))
                            {
                                try
                                {
                                    assembly = Assembly.LoadFrom(fileInfo.FullName);
                                    if (loadedPlugins.TryGetValue(assembly, out var name))
                                    {
	                                    LogWriter.ServerWriteLine(
		                                    $"Plugin `{fileNameWithoutExtension}` shares the same module name with `{name}`, using legacy assembly loading",
		                                    TraceLevel.Warning);
										assembly = Assembly.Load(File.ReadAllBytes(fileInfo.FullName));
                                    }
									else
										loadedPlugins.Add(assembly, fileNameWithoutExtension);

                                }
                                catch (BadImageFormatException e)
                                {
                                    LogWriter.ServerWriteLine($"failed to load plugin {e}", TraceLevel.Error);
									continue;
                                }

                                loadedAssemblies.Add(fileNameWithoutExtension, assembly);
                            }
							
                            if (InvalidateAssembly(assembly, fileInfo.Name))
                            {
                                foreach (var type in assembly.GetExportedTypes())
                                {
                                    if (type.IsSubclassOf(typeof(TerrariaPlugin)) && type.IsPublic && !type.IsAbstract)
                                    {
                                        var customAttributes =
                                            type.GetCustomAttributes(typeof(ApiVersionAttribute), false);
                                        if (customAttributes.Length != 0)
                                        {
                                            if (!IgnoreVersion)
                                            {
                                                var apiVersion = ((ApiVersionAttribute) customAttributes[0]).ApiVersion;
                                                if (apiVersion.Major != ApiVersion.Major ||
                                                    apiVersion.Minor != ApiVersion.Minor)
                                                {
                                                    LogWriter.ServerWriteLine(
	                                                    $"Plugin \"{type.FullName}\" is designed for a different Server API version ({apiVersion.ToString(2)}) and was ignored.", TraceLevel.Warning);
                                                    goto IL_28B;
                                                }
                                            }

                                            if (assembly.GetName().Name != fileNameWithoutExtension)
                                            {
	                                            LogWriter.ServerWriteLine(
		                                            $"Plugin Assembly Name `{assembly.GetName().Name}` is inconsistency with plugin file name `{fileInfo.Name}`",
		                                            TraceLevel.Warning);
                                            }

											if (fileNameWithoutExtension.Any(c => c >= 128))
											{
												LogWriter.ServerWriteLine(
													$"Plugin Name `{fileNameWithoutExtension}` contains non-ascii character(s)",
													TraceLevel.Warning);
											}

                                            TerrariaPlugin terrariaPlugin;
                                            try
                                            {
                                                var stopwatch = new Stopwatch();
                                                stopwatch.Start();
                                                terrariaPlugin =
                                                    (TerrariaPlugin) Activator.CreateInstance(type, game);
                                                stopwatch.Stop();
                                                dictionary.Add(terrariaPlugin, stopwatch);
                                            }
                                            catch (Exception innerException)
                                            {
                                                throw new InvalidOperationException(
	                                                $"Could not create an instance of plugin class \"{type.FullName}\".", innerException);
                                            }

                                            plugins.Add(new PluginContainer(terrariaPlugin));
                                        }
                                    }

                                    IL_28B: ;
                                }
                            }
                        }
                        catch (Exception innerException2)
                        {
                            throw new InvalidOperationException(
	                            $"Failed to load assembly \"{fileInfo.Name}\".", innerException2);
                        }
                    }

                    foreach (var pluginContainer in from x in Plugins
                             orderby x.Plugin.Order, x.Plugin.Name
                             select x)
                    {
                        var stopwatch2 = dictionary[pluginContainer.Plugin];
                        stopwatch2.Start();
                        try
                        {
                            pluginContainer.Initialize();
                        }
                        catch (Exception innerException3)
                        {
                            throw new InvalidOperationException(
	                            $"Plugin \"{pluginContainer.Plugin.Name}\" has thrown an exception during initialization.", innerException3);
                        }

                        stopwatch2.Stop();
                        LogWriter.ServerWriteLine(
	                        $"Plugin {pluginContainer.Plugin.Name} v{pluginContainer.Plugin.Version} (by {pluginContainer.Plugin.Author}) initiated.", TraceLevel.Info);
                    }

                    if (Profiler.WrappedProfiler != null)
                    {
                        foreach (var keyValuePair in dictionary)
                        {
                            var key = keyValuePair.Key;
                            var value = keyValuePair.Value;
                            Profiler.InputPluginInitTime(key, value.Elapsed);
                        }
                    }
                }

                WindowsLaunch.Main(targs);
            }
        }
    }
}
