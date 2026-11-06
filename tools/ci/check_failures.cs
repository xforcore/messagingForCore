// Fails when a .trx has a failed test that isn't in the suite's known-failures list.
// Usage: dotnet run tools/ci/check_failures.cs -- results.trx [known-failures.txt]
// Known failures are one "Class.Method(arguments)" per line, as this tool prints them; a line that starts with '#' is a
// comment (test names can contain '#', so it isn't a comment elsewhere). An entry that ends in '*' is a prefix, for
// theory cases whose names change from run to run or machine to machine (window handles, absolute paths): it covers
// every case whose name starts with it. Tests on the list that now pass are reported, so the list can shrink; a
// prefix is reported when its cases ran and none of them failed.
#:property NuGetAudit=false
using System.Xml.Linq;

XNamespace t = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
var trx = args[0];
var known = new HashSet<string>();
var prefixes = new List<string>();
if (args.Length > 1)
    foreach (var line in File.ReadLines(args[1]))
    {
        var entry = line.Trim();
        if (entry.Length == 0 || entry.StartsWith('#')) continue;
        if (entry.EndsWith('*')) prefixes.Add(entry[..^1]);
        else known.Add(entry);
    }
bool IsKnown(string name) => known.Contains(name) || prefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal));

var root = XDocument.Load(trx).Root!;
// A crashed test host ends the run early, and the tests it didn't reach have no results to fail.
var aborted = root.Descendants(t + "RunInfo").Select(i => (string?)i.Element(t + "Text") ?? "")
    .Where(s => s.StartsWith("The active test run was aborted", StringComparison.OrdinalIgnoreCase)).ToList();
var classes = root.Descendants(t + "UnitTest").ToDictionary(
    u => (string)u.Attribute("id")!, u => (string?)u.Element(t + "TestMethod")?.Attribute("className") ?? "");
var pairs = root.Descendants(t + "UnitTestResult").Select(r =>
{
    var name = (string)r.Attribute("testName")!;
    var cls = classes.GetValueOrDefault((string)r.Attribute("testId")!, "");
    return (Name: name.StartsWith(cls, StringComparison.Ordinal) ? name : cls + "." + name, Outcome: (string?)r.Attribute("outcome"));
}).ToList();

// Theory cases can share a display name: any failing case counts as a failure.
var failed = pairs.Where(p => p.Outcome == "Failed").Select(p => p.Name).ToHashSet();
var passed = pairs.Where(p => p.Outcome == "Passed").Select(p => p.Name).Where(n => !failed.Contains(n)).ToHashSet();
var newFailures = failed.Where(n => !IsKnown(n)).Order(StringComparer.Ordinal).ToList();
var fixedTests = known.Where(passed.Contains)
    .Concat(prefixes.Where(p => passed.Any(n => n.StartsWith(p, StringComparison.Ordinal))
        && !failed.Any(n => n.StartsWith(p, StringComparison.Ordinal))).Select(p => p + "*"))
    .Order(StringComparer.Ordinal).ToList();

Console.WriteLine($"{trx}: {pairs.Count} results, {failed.Count} failed ({failed.Count(IsKnown)} known, {newFailures.Count} new)");
foreach (var n in fixedTests) Console.WriteLine($"  now passes (remove from the known list): {n}");
foreach (var n in newFailures) Console.WriteLine($"::error::{n} failed");
foreach (var a in aborted) Console.WriteLine($"::error::{a}");
return newFailures.Count > 0 || aborted.Count > 0 ? 1 : 0;
