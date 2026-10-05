using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
internal static class Program
{
    private static int Main(string[] a)
    {
        var asm = AssemblyDefinition.ReadAssembly(a[0]);
        int from = Convert.ToInt32(a[2], 16), to = Convert.ToInt32(a[3], 16);
        foreach (var t in All(asm.MainModule.Types))
        foreach (var m in t.Methods)
        {
            if (!m.HasBody || (t.FullName + "::" + m.Name) != a[1]) continue;
            foreach (var i in m.Body.Instructions.Where(x => x.Offset >= from && x.Offset <= to))
                Console.WriteLine($"  IL_{i.Offset:X4}: {i}");
            return 0;
        }
        return 1;
    }
    private static IEnumerable<TypeDefinition> All(IEnumerable<TypeDefinition> ts)
    { foreach (var t in ts) { yield return t; foreach (var n in All(t.NestedTypes)) yield return n; } }
}