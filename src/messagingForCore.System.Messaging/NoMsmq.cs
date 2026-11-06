// Licensed under the MIT License. Copyright (c) messagingForCore contributors.
//
// messagingForCore never calls MSMQ, on Windows either (issue #1): a migrated application must behave the same
// wherever it runs, and the queues it sees are messagingForCore's. Until the SQLite store replaces Microsoft's
// [DllImport(ExternDll.Mqrt)] declarations, this keeps them from loading MSMQ's runtime where it's installed (it is on
// GitHub's Windows runners): mqrt.dll is "not found" everywhere, and Microsoft's code turns that into its own
// "Message Queuing has not been installed on this computer", as on Linux. Every other library resolves as usual.
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace System.Messaging
{
    internal static class NoMsmq
    {
        [ModuleInitializer]
        internal static void Initialize()
        {
            NativeLibrary.SetDllImportResolver(typeof(NoMsmq).Assembly, Resolve);
        }

        private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (string.Equals(libraryName, ExternDll.Mqrt, StringComparison.OrdinalIgnoreCase))
                throw new DllNotFoundException("messagingForCore doesn't use MSMQ (" + ExternDll.Mqrt + ").");
            return IntPtr.Zero;
        }
    }
}
