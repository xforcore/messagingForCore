// Licensed under the MIT License. Copyright (c) messagingForCore contributors.
//
// Waking receivers. SQLite has no notifications between processes, so a receive that waits for a message wakes when
// this process sends, commits or aborts (Signal), and otherwise polls the database every PollInterval for messages
// from other processes. Every wait is bounded (CONTRIBUTING.md, rule 10).
using System.Threading;

namespace System.Messaging.Store
{
    internal static class Arrivals
    {
        /// <summary>How long a waiting receive goes without looking at the database (other processes' messages).</summary>
        internal const int PollInterval = 100;

        private static readonly object gate = new object();
        private static long version;

        /// <summary>Changes on each signal; a waiter reads it before it looks at the database.</summary>
        internal static long Version
        {
            get { lock (gate) return version; }
        }

        internal static void Signal()
        {
            lock (gate)
            {
                version++;
                Monitor.PulseAll(gate);
            }
        }

        /// <summary>Waits until a signal after <paramref name="seen"/>, or for at most the given time.</summary>
        internal static void Wait(long seen, int milliseconds)
        {
            lock (gate)
            {
                if (version == seen)
                    Monitor.Wait(gate, Math.Max(0, Math.Min(milliseconds, PollInterval)));
            }
        }
    }
}
