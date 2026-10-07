using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace PGameTSManager
{
    public sealed class ItemNameEntry
    {
        public int Id { get; set; }
        public string Chinese { get; set; } = "";
        public string English { get; set; } = "";
        public string Internal { get; set; } = "";
        public string Display => Chinese + "  #" + Id;
        public string Detail => English + "  /  " + Internal;
    }

    public sealed class PrefixOption
    {
        public int Id { get; set; }
        public string Display { get; set; } = "";
        public override string ToString() => Display;
    }

    /// <summary>内置中文物品表，数据来自 TSWeb ID.json。</summary>
    public static class ItemCatalog
    {
        private static readonly Lazy<List<ItemNameEntry>> _all = new(Load);
        private static readonly Lazy<Dictionary<int, ItemNameEntry>> _byId =
            new(() => _all.Value.ToDictionary(x => x.Id));

        public static IReadOnlyList<ItemNameEntry> All => _all.Value;

        public static IReadOnlyList<PrefixOption> CommonPrefixes { get; } = new[]
        {
            new PrefixOption { Id = 0, Display = "0 · 无前缀" },
            new PrefixOption { Id = 3, Display = "3 · 危险" },
            new PrefixOption { Id = 19, Display = "19 · 致命" },
            new PrefixOption { Id = 20, Display = "20 · 坚固" },
            new PrefixOption { Id = 24, Display = "24 · 强大" },
            new PrefixOption { Id = 27, Display = "27 · 大师" },
            new PrefixOption { Id = 36, Display = "36 · 优越" },
            new PrefixOption { Id = 58, Display = "58 · 神级" },
            new PrefixOption { Id = 59, Display = "59 · 恶魔" },
            new PrefixOption { Id = 64, Display = "64 · 护佑" },
            new PrefixOption { Id = 65, Display = "65 · 奥术" },
            new PrefixOption { Id = 71, Display = "71 · 险恶" },
            new PrefixOption { Id = 81, Display = "81 · 传说" }
        };

        public static string NameOf(int id) =>
            _byId.Value.TryGetValue(id, out var item) && !string.IsNullOrWhiteSpace(item.Chinese)
                ? item.Chinese
                : "物品 #" + id;

        public static string PrefixText(int id)
        {
            var option = CommonPrefixes.FirstOrDefault(x => x.Id == id);
            return option?.Display ?? ("# " + id);
        }

        public static List<ItemNameEntry> Search(string query, int limit = 500)
        {
            var text = (query ?? "").Trim();
            if (text.Length == 0)
                return _all.Value.Take(limit).ToList();

            if (int.TryParse(text, out var id))
            {
                var exact = _all.Value.Where(x => x.Id == id).ToList();
                if (exact.Count > 0) return exact;
            }

            return _all.Value
                .Where(x =>
                    x.Id.ToString().Contains(text, StringComparison.OrdinalIgnoreCase) ||
                    x.Chinese.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                    x.English.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                    x.Internal.Contains(text, StringComparison.OrdinalIgnoreCase))
                .Take(limit)
                .ToList();
        }

        private static List<ItemNameEntry> Load()
        {
            var result = new List<ItemNameEntry>();
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "Data", "item-names.zh-CN.json");
                if (!File.Exists(path)) return result;
                var root = JObject.Parse(File.ReadAllText(path, System.Text.Encoding.UTF8));
                if (root["list"] is not JArray list) return result;

                foreach (var token in list.OfType<JObject>())
                {
                    var id = token.Value<int?>("id") ?? 0;
                    if (id <= 0) continue;
                    result.Add(new ItemNameEntry
                    {
                        Id = id,
                        Chinese = token.Value<string>("chinese") ?? "",
                        English = token.Value<string>("english") ?? "",
                        Internal = token.Value<string>("internal") ?? ""
                    });
                }
            }
            catch { }
            return result.OrderBy(x => x.Id).ToList();
        }
    }
}
