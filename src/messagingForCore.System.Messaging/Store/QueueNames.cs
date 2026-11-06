// Licensed under the MIT License. Copyright (c) messagingForCore contributors.
//
// Path names and format names, as MSMQ parses them, for the queues messagingForCore has: private queues on this
// machine. A queue is named by its path name (.\private$\orders, MACHINE\private$\orders, localhost\private$\orders) or
// by a format name: PRIVATE=<machine id>\<queue number> (what MQPathNameToFormatName returns for a private queue, as
// MSMQ does) or DIRECT=OS:<this machine>\private$\orders. Anything else names something messagingForCore doesn't have,
// and fails with a MessageQueueException that says so.
using System.Globalization;

namespace System.Messaging.Store
{
    internal static class QueueNames
    {
        private const string PrivatePrefix = "private$\\";

        // MQ_MAX_Q_NAME_LEN
        private const int MaxNameLength = 124;

        /// <summary>A queue named either by its name (after private$\) or by its number in the database.</summary>
        internal struct QueueReference
        {
            internal string Name;
            internal long Id;
        }

        internal static bool IsLocalMachine(string machine)
        {
            if (machine == "." || string.Equals(machine, "localhost", StringComparison.OrdinalIgnoreCase))
                return true;
            string name = Environment.MachineName;
            if (string.Equals(machine, name, StringComparison.OrdinalIgnoreCase))
                return true;
            // A fully qualified name of this machine.
            return machine.Length > name.Length && machine[name.Length] == '.' &&
                   machine.StartsWith(name, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The queue name in a path name, or a status: IllegalQueuePathName for a malformed path; it throws for a path
        /// to a queue messagingForCore doesn't have.
        /// </summary>
        internal static int ParsePathName(string path, out string name)
        {
            name = null;
            if (string.IsNullOrEmpty(path))
                return (int)MessageQueueErrorCode.IllegalQueuePathName;
            int separator = path.IndexOf('\\');
            if (separator <= 0)
                return (int)MessageQueueErrorCode.IllegalQueuePathName;
            string machine = path.Substring(0, separator);
            string rest = path.Substring(separator + 1);
            if (!rest.StartsWith(PrivatePrefix, StringComparison.OrdinalIgnoreCase))
                throw Limitations.PublicQueue(path);
            if (!IsLocalMachine(machine))
                throw Limitations.RemoteQueue(path);
            return ParseName(rest.Substring(PrivatePrefix.Length), out name);
        }

        /// <summary>The queue a format name names, or a status (IllegalFormatName); it throws for unsupported ones.</summary>
        internal static int ParseFormatName(string formatName, Guid machineId, out QueueReference queue)
        {
            queue = default;
            if (string.IsNullOrEmpty(formatName))
                return (int)MessageQueueErrorCode.IllegalFormatName;

            if (formatName.IndexOf(';') >= 0)
                throw Limitations.SubQueue(formatName);
            if (formatName.IndexOf(',') >= 0)
                throw Limitations.MultipleDestinations(formatName);

            if (formatName.StartsWith("PRIVATE=", StringComparison.OrdinalIgnoreCase))
            {
                string value = formatName.Substring("PRIVATE=".Length);
                int separator = value.IndexOf('\\');
                if (separator < 0 || !Guid.TryParse(value.Substring(0, separator), out Guid machine) ||
                    !long.TryParse(value.Substring(separator + 1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out long id) ||
                    id <= 0)
                    return (int)MessageQueueErrorCode.IllegalFormatName;
                if (machine != machineId)
                    throw Limitations.RemoteQueue(formatName);
                queue.Id = id;
                return 0;
            }

            if (formatName.StartsWith("DIRECT=", StringComparison.OrdinalIgnoreCase))
            {
                string value = formatName.Substring("DIRECT=".Length);
                if (!value.StartsWith("OS:", StringComparison.OrdinalIgnoreCase))
                    throw Limitations.Protocol(formatName);
                int status = ParsePathName(value.Substring("OS:".Length), out string name);
                if (status != 0)
                    return (int)MessageQueueErrorCode.IllegalFormatName;
                queue.Name = name;
                return 0;
            }

            if (formatName.StartsWith("PUBLIC=", StringComparison.OrdinalIgnoreCase))
                throw Limitations.PublicQueue(formatName);
            if (formatName.StartsWith("MULTICAST=", StringComparison.OrdinalIgnoreCase) ||
                formatName.StartsWith("DL=", StringComparison.OrdinalIgnoreCase))
                throw Limitations.MultipleDestinations(formatName);
            if (formatName.StartsWith("MACHINE=", StringComparison.OrdinalIgnoreCase))
                throw Limitations.SubQueue(formatName);
            return (int)MessageQueueErrorCode.IllegalFormatName;
        }

        internal static string FormatName(Guid machineId, long id)
        {
            return "PRIVATE=" + machineId.ToString("D") + "\\" + id.ToString("x8", CultureInfo.InvariantCulture);
        }

        internal static string PathName(string name)
        {
            return Environment.MachineName.ToLowerInvariant() + "\\" + PrivatePrefix + name;
        }

        private static int ParseName(string name, out string result)
        {
            result = null;
            if (name.Length == 0 || name.Length > MaxNameLength || name.IndexOf('\\') >= 0)
                return (int)MessageQueueErrorCode.IllegalQueuePathName;
            result = name;
            return 0;
        }
    }
}
