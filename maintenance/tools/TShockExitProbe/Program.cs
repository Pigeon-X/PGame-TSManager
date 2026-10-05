using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

// 给指定方法插入「进入/退出」埋点：写 AppContext.BaseDirectory\ilprobe.log
// 目的：搞清 exit 0 到底是哪个方法返回导致的（没有任何异常被抛出）。
//
// 用法: TShockExitProbe <OTAPI.dll> "类型::方法" ["类型::方法" ...]
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 2) { Console.WriteLine("用法: TShockExitProbe <dll> <Type::Method> [...]"); return 1; }
        var path = args[0];
        var targets = args.Skip(1).ToHashSet();

        var resolver = new DefaultAssemblyResolver();
        var selfDir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        resolver.AddSearchDirectory(selfDir);
        var bp = Path.GetFullPath(Path.Combine(selfDir, "..", "bin"));
        if (Directory.Exists(bp)) resolver.AddSearchDirectory(bp);

        var asm = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { InMemory = true, AssemblyResolver = resolver });
        var module = asm.MainModule;

        var append = module.ImportReference(typeof(File).GetMethod("AppendAllText", new[] { typeof(string), typeof(string) }));
        var combine = module.ImportReference(typeof(Path).GetMethod("Combine", new[] { typeof(string), typeof(string) }));
        var baseDir = module.ImportReference(typeof(AppContext).GetProperty("BaseDirectory").GetGetMethod());

        var patched = 0;
        foreach (var type in AllTypes(module.Types))
        foreach (var m in type.Methods)
        {
            var key = type.FullName + "::" + m.Name;
            if (!targets.Contains(key) || !m.HasBody) continue;

            var il = m.Body.GetILProcessor();
            var tag = type.Name + "." + m.Name;

            // 进入
            Emit(il, null, il.Body.Instructions.First(), baseDir, combine, append, tag + " ENTER\n");

            // 每个 ret 之前 -> 退出
            foreach (var ret in m.Body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToList())
                Emit(il, il, ret, baseDir, combine, append, tag + " EXIT\n");

            patched++;
            Console.WriteLine("  已插桩: " + key);
        }

        if (patched == 0) { Console.WriteLine("没有匹配到方法；可用方法名请先用 TShockConsoleProbe dump 确认"); return 1; }
        asm.Write(path);
        Console.WriteLine($"完成：插桩 {patched} 个方法 -> {path}");
        return 0;
    }

    private static void Emit(ILProcessor il, object _, Instruction anchor, MethodReference baseDir, MethodReference combine, MethodReference append, string text)
    {
        var ins = new[]
        {
            il.Create(OpCodes.Call, baseDir),
            il.Create(OpCodes.Ldstr, "ilprobe.log"),
            il.Create(OpCodes.Call, combine),
            il.Create(OpCodes.Ldstr, text),
            il.Create(OpCodes.Call, append),
        };
        foreach (var i in ins) il.InsertBefore(anchor, i);
    }

    private static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> ts)
    { foreach (var t in ts) { yield return t; foreach (var n in AllTypes(t.NestedTypes)) yield return n; } }
}