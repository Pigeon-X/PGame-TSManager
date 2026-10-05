using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

// 给 Terraria.Program::DisplayException 打补丁：
// 在原方法开头插入 File.AppendAllText("<exe目录>\server-start-exception.log", ex.ToString())
// 目的：TShock 把启动异常吞掉只打印到控制台，没有控制台时就完全看不到，导致“静默 exit 0”。
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 1) { Console.WriteLine("用法: TShockExceptionPatch <OTAPI.dll>"); return 1; }
        var path = args[0];
        var asm = AssemblyDefinition.ReadAssembly(path);
        var module = asm.MainModule;

        var target = AllTypes(module.Types)
            .SelectMany(t => t.Methods.Select(m => new { t, m }))
            .FirstOrDefault(x => x.t.FullName == "Terraria.Program" && x.m.Name == "DisplayException" && x.m.HasBody);
        if (target == null) { Console.WriteLine("找不到 Terraria.Program::DisplayException"); return 1; }

        var m = target.m;
        Console.WriteLine($"找到 {target.t.FullName}::{m.Name}  参数: {string.Join(", ", m.Parameters.Select(p => p.ParameterType.Name + " " + p.Name))}");
        if (m.Body.Instructions.Any(i => i.OpCode == OpCodes.Ldstr && (i.Operand as string)?.Contains("server-start-exception") == true))
        {
            Console.WriteLine("已经打过补丁，跳过。"); return 0;
        }

        var il = m.Body.GetILProcessor();
        var first = m.Body.Instructions.First();
        var one = module.ImportReference(typeof(System.Exception).GetMethod("ToString", Type.EmptyTypes));
        var append = module.ImportReference(typeof(System.IO.File).GetMethod("AppendAllText", new[] { typeof(string), typeof(string) }));

        // File.AppendAllText(AppContext.BaseDirectory + "server-start-exception.log", ex.ToString() + "\n")
        var baseDir = module.ImportReference(typeof(AppContext).GetProperty("BaseDirectory").GetGetMethod());
        var combine = module.ImportReference(typeof(System.IO.Path).GetMethod("Combine", new[] { typeof(string), typeof(string) }));
        var concat = module.ImportReference(typeof(string).GetMethod("Concat", new[] { typeof(string), typeof(string) }));

        var ins = new[]
        {
            il.Create(OpCodes.Call, baseDir),
            il.Create(OpCodes.Ldstr, "server-start-exception.log"),
            il.Create(OpCodes.Call, combine),
            il.Create(OpCodes.Ldarg_0),                      // 第一个参数 = Exception
            il.Create(OpCodes.Callvirt, one),
            il.Create(OpCodes.Ldstr, "\r\n"),
            il.Create(OpCodes.Call, concat),
            il.Create(OpCodes.Call, append),
            il.Create(OpCodes.Pop),
        };
        foreach (var i in ins) il.InsertBefore(first, i);

        asm.Write(path);
        Console.WriteLine("补丁完成: " + path);
        return 0;
    }

    private static System.Collections.Generic.IEnumerable<TypeDefinition> AllTypes(System.Collections.Generic.IEnumerable<TypeDefinition> ts)
    { foreach (var t in ts) { yield return t; foreach (var n in AllTypes(t.NestedTypes)) yield return n; } }
}