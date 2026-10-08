using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace PGameTSManager
{
    internal sealed class ControlEvent
    {
        public string Type { get; init; } = "";
        public string? ServerId { get; init; }
        public string At { get; init; } = DateTimeOffset.Now.ToString("o");
        public long Seq { get; init; }
        public object? Payload { get; init; }
    }

    internal static class ControlEventHub
    {
        private static readonly object Gate = new();
        private static readonly Queue<ControlEvent> Buffer = new();
        private static readonly ConcurrentDictionary<Guid, Action<ControlEvent>> Subscribers = new();
        private static long _seq;

        /// <summary>
        /// 本进程的事件纪元（= 启动时的 Seq 下界）。客户端可用它区分
        /// “没有新事件” 与 “TSM 重启过”，从而安全地做 Last-Event-ID 断线补发。
        /// </summary>
        public static long EventEpoch { get; private set; }

        static ControlEventHub()
        {
            // 关键：Seq 跨进程重启也必须单调递增，否则客户端把 seq 落盘去重时会丢事件。
            // 用毫秒时间戳作为下界：重启后新 seq 一定大于上一次运行的所有 seq。
            var floor = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _seq = floor;
            EventEpoch = floor;
        }

        public static int BufferSize { get; set; } = 512;
        public static long LatestSeq
        {
            get { lock (Gate) return _seq; }
        }

        public static ControlEvent Publish(string type, string? serverId, object? payload)
        {
            ControlEvent ev;
            lock (Gate)
            {
                ev = new ControlEvent
                {
                    Type = type,
                    ServerId = serverId,
                    At = DateTimeOffset.Now.ToString("o"),
                    Seq = ++_seq,
                    Payload = payload
                };
                Buffer.Enqueue(ev);
                while (Buffer.Count > Math.Max(32, BufferSize)) Buffer.Dequeue();
            }

            foreach (var subscriber in Subscribers.Values)
            {
                try { subscriber(ev); } catch { }
            }
            return ev;
        }

        public static IReadOnlyList<ControlEvent> After(long seq)
        {
            lock (Gate)
            {
                return Buffer.Where(x => x.Seq > seq).ToList();
            }
        }

        public static IDisposable Subscribe(Action<ControlEvent> handler)
        {
            var id = Guid.NewGuid();
            Subscribers[id] = handler;
            return new Subscription(id);
        }

        private sealed class Subscription(Guid id) : IDisposable
        {
            public void Dispose() => Subscribers.TryRemove(id, out _);
        }
    }
}
