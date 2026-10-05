using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
internal static class Program
{
    private static int Main(string[] a)
    {
        foreach (var path in a)
        {
            if (!File.Exists(path)) continue;
            Console.WriteLine("===== " + Path.GetFileName(path) + " =====");
            var asm = AssemblyDefinition.ReadAssembly(path);
            var hits = new Dictionary<string,int>();
            foreach (var t in All(asm.MainModule.Types))
            foreach (var m in t.Methods)
            {
                if (!m.HasBody) continue;
                foreach (var i in m.Body.Instructions)
                {
                    var r = i.Operand as MethodReference;
                    if (r == null) continue;
                    if (r.DeclaringType?.FullName != "System.Console") continue;
                    hits.TryGetValue(r.Name, out var c);
                    hits[r.Name] = c + 1;
                }
            }
            foreach (var kv in hits.OrderByDescending(k => k.Value))
                Console.WriteLine($"  {kv.Key,-24} x{kv.Value}");
        }
        return 0;
    }
    private static IEnumerable<TypeDefinition> All(IEnumerable<TypeDefinition> ts)
    { foreach (var t in ts) { yield return t; foreach (var n in All(t.NestedTypes)) yield return n; } }
}