using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

// A 方案：把「没有真实控制台时就会抛异常」的 Console 调用从 DLL 里去掉。
// 会抛的成员：set/get_ForegroundColor、set/get_BackgroundColor、set/get_Title、
//            ResetColor、Clear、CursorVisible、Window*/Buffer*/Cursor* 等。
// 这些全是纯装饰，删掉不影响功能。
//
// 注意两点（都是踩过的坑）：
//   1. 必须 InMemory=true，否则 Cecil 一直占着文件句柄，Write 同路径必报 “being used by another process”
//   2. 必须给 DefaultAssemblyResolver 指到 bin\，否则重建元数据时解析不到 OTAPI/TerrariaServer
internal static class Program
{
    private static readonly HashSet<string> VoidWithArg = new HashSet<string>
    {
        "set_ForegroundColor", "set_BackgroundColor", "set_Title", "set_CursorVisible",
        "set_WindowWidth", "set_WindowHeight", "set_BufferWidth", "set_BufferHeight",
        "set_CursorLeft", "set_CursorTop", "SetWindowSize", "SetBufferSize", "MoveBufferArea",
    };
    private static readonly HashSet<string> VoidNoArg = new HashSet<string> { "ResetColor", "Clear" };
    private static readonly HashSet<string> EnumGetter = new HashSet<string>
    {
        "get_ForegroundColor", "get_BackgroundColor",
    };

    private static int Main(string[] args)
    {
        if (args.Length < 1) { Console.WriteLine("用法: TShockConsoleColorPatch <dll...>"); return 1; }
        var total = 0;
        foreach (var path in args)
        {
            if (!File.Exists(path)) { Console.WriteLine("[跳过] " + path); continue; }

            var resolver = new DefaultAssemblyResolver();
            var selfDir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
            resolver.AddSearchDirectory(selfDir);
            foreach (var extra in new[] { Path.Combine(selfDir, "..", "bin"), Path.Combine(selfDir, "..", "ServerPlugins") })
            {
                var full = Path.GetFullPath(extra);
                if (Directory.Exists(full)) resolver.AddSearchDirectory(full);
            }

            var rp = new ReaderParameters { InMemory = true, AssemblyResolver = resolver };
            var asm = AssemblyDefinition.ReadAssembly(path, rp);
            var count = 0;

            foreach (var type in AllTypes(asm.MainModule.Types))
            foreach (var m in type.Methods)
            {
                if (!m.HasBody) continue;
                var il = m.Body.GetILProcessor();
                foreach (var ins in m.Body.Instructions.ToList())
                {
                    var r = ins.Operand as MethodReference;
                    if (r == null || r.DeclaringType?.FullName != "System.Console") continue;

                    if (VoidWithArg.Contains(r.Name) && r.Parameters.Count >= 1)
                    {
                        il.Replace(ins, il.Create(OpCodes.Pop));
                        count++;
                    }
                    else if (VoidNoArg.Contains(r.Name) && r.Parameters.Count == 0)
                    {
                        il.Replace(ins, il.Create(OpCodes.Nop));
                        count++;
                    }
                    else if (EnumGetter.Contains(r.Name) && r.Parameters.Count == 0)
                    {
                        il.Replace(ins, il.Create(OpCodes.Ldc_I4_7));   // Gray
                        count++;
                    }
                }
            }

            if (count == 0) { Console.WriteLine("  " + Path.GetFileName(path) + "：无需处理"); continue; }
            asm.Write(path);
            Console.WriteLine("  " + Path.GetFileName(path) + "：处理 " + count + " 处");
            total += count;
        }
        Console.WriteLine("合计处理 " + total + " 处");
        return 0;
    }

    private static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> ts)
    { foreach (var t in ts) { yield return t; foreach (var n in AllTypes(t.NestedTypes)) yield return n; } }
}