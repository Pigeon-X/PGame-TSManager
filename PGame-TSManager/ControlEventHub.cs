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
