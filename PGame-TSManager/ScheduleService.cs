using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace PGameTSManager
{
    public sealed class ScheduledTask
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string ServerName { get; set; } = "";
        public string Action { get; set; } = "save";
        public string Time { get; set; } = "04:00";
        public string Message { get; set; } = "";
        public bool Enabled { get; set; } = true;
        public string LastRunDate { get; set; } = "";
        public string Summary => ServerName + " · " + Action;
        public string EnabledText => Enabled ? "启用" : "停用";
    }

    public static class ScheduledTaskStore
    {
        public static string Path => System.IO.Path.Combine(ManagerConfig.BaseDir, "schedules.json");

        public static List<ScheduledTask> Load()
        {
            try
            {
                if (!File.Exists(Path)) return new List<ScheduledTask>();
                return JsonConvert.DeserializeObject<List<ScheduledTask>>(File.ReadAllText(Path)) ?? new List<ScheduledTask>();
            }
            catch { return new List<ScheduledTask>(); }
        }

        public static void Save(IEnumerable<ScheduledTask> tasks)
        {
            File.WriteAllText(Path, JsonConvert.SerializeObject(tasks.ToList(), Formatting.Indented), new System.Text.UTF8Encoding(false));
        }
    }

    public static class ScheduleService
    {
        public static async Task TickAsync(IEnumerable<ServerContainer> containers,
            Action<string> log, Func<ServerContainer, Task> restart)
        {
            var now = DateTime.Now;
            var clock = now.ToString("HH:mm");
            var today = now.ToString("yyyy-MM-dd");
            var tasks = ScheduledTaskStore.Load();
            var changed = false;

            foreach (var task in tasks.Where(x => x.Enabled && x.Time == clock && x.LastRunDate != today))
            {
                var server = containers.FirstOrDefault(x =>
                    string.Equals(x.Name, task.ServerName, StringComparison.OrdinalIgnoreCase));
                if (server == null)
                {
                    log($"[计划任务] 找不到服务器：{task.ServerName}");
                    continue;
                }

                try
                {
                    switch (task.Action)
                    {
                        case "save":
                            if (server.IsRunning) server.SendText("/save");
                            log($"[计划任务] {server.Name} 已执行保存世界");
                            break;
                        case "broadcast":
                            if (server.IsRunning) server.SendText("/bc " + task.Message);
                            log($"[计划任务] {server.Name} 已执行广播");
                            break;
                        case "restart":
                            if (server.IsRunning)
                            {
                                server.IsRunning = false;
                                await server.WaitForStoppedAsync(TimeSpan.FromSeconds(30));
                                await restart(server);
                                log($"[计划任务] {server.Name} 已执行重启");
                            }
                            break;
                    }
                    task.LastRunDate = today;
                    changed = true;
                }
                catch (Exception ex)
                {
                    log($"[计划任务] {server.Name} 执行失败：{ex.Message}");
                }
            }

            if (changed) ScheduledTaskStore.Save(tasks);
        }
    }
}
