// Checks messagingForCore's own code against CONTRIBUTING.md rules 8 and 9: no reflection into non-public members, no
// unsafe code or P/Invoke, nothing that steers the garbage collector. (webForCore's tools/ci/check_code_rules.cs,
// with this repository's directories.) The native boundary of rule 9 is the store's PropertyBlock, which reads and
// writes the caller's buffers with Marshal only, which these patterns allow.
// Usage: dotnet run tools/ci/check_code_rules.cs [-- <repository root>]
// Prints each line that breaks a rule, and exits with 1 if there is one.
#:property NuGetAudit=false
using System.Text.RegularExpressions;

var root = Path.GetFullPath(args.Length > 0 ? args[0] : ".");

// Code messagingForCore wrote itself. Microsoft's code is in external/ and keeps its own interop (principle 3).
string[] ownDirectories =
[
    "src/messagingForCore.System.Messaging/Store",
    "src/messagingForCore.Msmq",
    "test/messagingForCore.System.Messaging.Tests",
    "test/messagingForCore.Gate",
    "test/messagingForCore.Gate.Peer",
    "tools",
];
string[] ownFiles =
[
    "src/messagingForCore.System.Messaging/AssemblyRef.cs",
    "test/messagingForCore.System.Messaging.MonoTests/QueueDatabase.cs",
];

(string Name, Regex Pattern)[] rules =
[
    ("non-public reflection", new(@"BindingFlags\.NonPublic|UnsafeAccessor|\.InvokeMember\(|Activator\.CreateInstance\([^;]*,\s*(true|nonPublic:\s*true)\s*\)")),
    ("unsafe code", new(@"\bunsafe\b|\bstackalloc\b|\bfixed\s*\(|\bUnsafe\.|\bMemoryMarshal\.")),
    ("P/Invoke", new(@"\[\s*(DllImport|LibraryImport)\b|\bextern\s+(static\s+)?\w+\s+\w+\s*\(")),
    ("GC steering", new(@"\bGC\.(Collect|WaitForPendingFinalizers|AddMemoryPressure)\b|\bGCHandle\b|\bWeakReference\b")),
];

var files = ownDirectories
    .Select(d => Path.Combine(root, d))
    .Where(Directory.Exists)
    .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
    .Concat(ownFiles.Select(f => Path.Combine(root, f)).Where(File.Exists))
    .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
    .Where(f => !f.Contains("/obj/") && !f.Contains("/bin/") && f != "tools/ci/check_code_rules.cs")
    .Distinct()
    .Order(StringComparer.Ordinal)
    .ToList();

int violations = 0;
foreach (var file in files)
{
    var lines = File.ReadAllLines(Path.Combine(root, file));
    for (int i = 0; i < lines.Length; i++)
    {
        var code = StripComment(lines[i]);
        foreach (var (name, pattern) in rules)
        {
            if (!pattern.IsMatch(code)) continue;
            violations++;
            Console.WriteLine($"{file}:{i + 1}: {name}: {lines[i].Trim()}");
        }
    }
}

Console.WriteLine($"{files.Count} files checked: {violations} rule breaks");
return violations > 0 ? 1 : 0;

// Drops a // comment (not inside a string) so that prose about a rule doesn't count as breaking it.
static string StripComment(string line)
{
    bool inString = false;
    for (int i = 0; i < line.Length - 1; i++)
    {
        if (line[i] == '"' && (i == 0 || line[i - 1] != '\\')) inString = !inString;
        else if (!inString && line[i] == '/' && line[i + 1] == '/') return line[..i];
    }
    return line;
}
