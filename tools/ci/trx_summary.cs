// Summarises VSTest .trx files: totals, then failures grouped by test class and by message. Prints Markdown.
// Usage: dotnet run tools/ci/trx_summary.cs -- results/*.trx
#:property NuGetAudit=false
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

XNamespace t = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
var output = new StringBuilder();
foreach (var path in args)
{
    var root = XDocument.Load(path).Root!;
    var tests = root.Descendants(t + "UnitTest").ToDictionary(u => (string)u.Attribute("id")!);
    var counters = root.Descendants(t + "ResultSummary").Elements(t + "Counters").First();
    int total = (int)counters.Attribute("total")!, passed = (int)counters.Attribute("passed")!,
        failed = (int)counters.Attribute("failed")!;
    var name = path[(path.LastIndexOf('/') + 1)..].Replace(".trx", "");
    var rate = (100.0 * passed / Math.Max(1, total)).ToString("F1", CultureInfo.InvariantCulture);
    output.Append($"## {name}\n\nTotal {total}, passed {passed}, failed {failed}, not run {total - passed - failed} (pass rate {rate}%)\n\n");

    // Counts in first-seen order, so equal counts keep the order the failures came in.
    var byClass = new Counter();
    var byMessage = new Counter();
    var examples = new Dictionary<string, string>();
    foreach (var r in root.Descendants(t + "UnitTestResult"))
    {
        if ((string?)r.Attribute("outcome") != "Failed") continue;
        var method = tests[(string)r.Attribute("testId")!].Element(t + "TestMethod")!;
        var className = (string)method.Attribute("className")!;
        var cls = className[(className.LastIndexOf('.') + 1)..];
        var message = r.Descendants(t + "ErrorInfo").Elements(t + "Message").FirstOrDefault()?.Value ?? "";
        var key = Normalise(message);
        byClass.Add(cls);
        byMessage.Add(key);
        examples.TryAdd(key, $"{cls}.{(string)method.Attribute("name")!}");
    }
    if (byClass.Count > 0)
    {
        output.Append("| Failures | Test class |\n| --- | --- |\n");
        foreach (var (c, n) in byClass.MostCommon(15)) output.Append($"| {n} | `{c}` |\n");
        output.Append("\n| Failures | Message (normalised) | Example |\n| --- | --- | --- |\n");
        foreach (var (m, n) in byMessage.MostCommon(15)) output.Append($"| {n} | {m.Replace('|', '/')} | `{examples[m]}` |\n");
    }
    output.Append('\n');
}
Console.Out.Write(output.ToString());

// A failure message as a groupable key: its first line, with quoted values and numbers blanked.
static string Normalise(string message)
{
    var first = message.Length == 0 ? "(no message)" : Regex.Split(message.Trim(), @"\r\n|[\n\r\v\f\x1c\x1d\x1e\x85  ]")[0];
    first = Regex.Replace(first, "'[^']*'|\"[^\"]*\"", "'…'");
    first = Regex.Replace(first, @"\d+", "N");
    var runes = first.EnumerateRunes().Take(150);
    return string.Concat(runes.Select(r => r.ToString()));
}

sealed class Counter
{
    readonly Dictionary<string, (int Count, int Order)> counts = new();
    public int Count => counts.Count;
    public void Add(string key) =>
        counts[key] = counts.TryGetValue(key, out var v) ? (v.Count + 1, v.Order) : (1, counts.Count);
    public IEnumerable<(string Key, int Count)> MostCommon(int n) =>
        counts.OrderByDescending(kv => kv.Value.Count).ThenBy(kv => kv.Value.Order).Take(n).Select(kv => (kv.Key, kv.Value.Count));
}
