using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

// 根治补丁：去掉 TShockAPI 里所有 Console.set_ForegroundColor / set_BackgroundColor 调用。
//
// 原因：TShockAPI.TextLog.ConsoleError/ConsoleWarn/ConsoleInfo 会用
//   Console.ForegroundColor = ConsoleColor.Xxx;
// 在“没有真实控制台”的进程里（GUI 父进程拉起来的），这个 setter 会抛 IOException。
// 插件 catch 了业务异常后调用 ConsoleError 报告，结果这个 IOException 逃出去，
// 被 Terraria 主循环的 catch 吞掉 -> DisplayException -> 进程静默 exit 0。
//
// 颜色只是装饰，直接删掉调用即可（用 pop 保持栈平衡）。
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 1) { Console.WriteLine("用法: TShockConsoleColorPatch <TShockAPI.dll>"); return 1; }
        var path = args[0];
        var asm = AssemblyDefinition.ReadAssembly(path);
        var module = asm.MainModule;
        var count = 0;

        foreach (var type in AllTypes(module.Types))
        foreach (var m in type.Methods)
        {
            if (!m.HasBody) continue;
            var il = m.Body.GetILProcessor();
            foreach (var ins in m.Body.Instructions.ToList())
            {
                var r = ins.Operand as MethodReference;
                if (r == null) continue;
                if (r.DeclaringType?.FullName != "System.Console") continue;
                if (r.Name != "set_ForegroundColor" && r.Name != "set_BackgroundColor") continue;
                il.Replace(ins, il.Create(OpCodes.Pop));
                count++;
                Console.WriteLine($"  {type.FullName}::{m.Name}  -> 去掉 {r.Name}");
            }
        }

        if (count == 0) { Console.WriteLine("没找到颜色调用（可能已打过补丁）"); return 0; }
        asm.Write(path);
        Console.WriteLine($"补丁完成：去掉 {count} 处 Console 颜色调用 -> {path}");
        return 0;
    }

    private static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> ts)
    { foreach (var t in ts) { yield return t; foreach (var n in AllTypes(t.NestedTypes)) yield return n; } }
}