using System.Globalization;
using System.Text;

namespace Cleaner.Ai;

/// <summary>
/// Koneen tiedot ja moduulien skannaustulosten yhteenveto mallille lähetettävänä tekstinä (englanniksi,
/// koot <see cref="AiFormat.Bytes"/>-muodossa). Teksti ei näy käyttäjälle.
/// </summary>
public static class HealthDigest
{
    private const int MaxCategories = 5;
    private const string NotSummed = "size not summed (nested items)";

    public static string Build(SystemFacts facts, IReadOnlyList<ModuleScanSummary> modules)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();

        sb.Append("# System\n");
        sb.Append("- OS: ").Append(facts.OsVersion).Append('\n');
        sb.Append("- CPU cores: ").Append(facts.ProcessorCount.ToString(inv)).Append('\n');
        sb.Append("- Memory total: ").Append(facts.TotalMemoryBytes is long t ? AiFormat.Bytes(t) : "unknown")
          .Append(", available: ").Append(facts.AvailableMemoryBytes is long a ? AiFormat.Bytes(a) : "unknown").Append('\n');
        var up = facts.Uptime;
        sb.Append("- Uptime: ").Append(((int)up.TotalDays).ToString(inv)).Append(" d ")
          .Append(up.Hours.ToString(inv)).Append(" h ").Append(up.Minutes.ToString(inv)).Append(" min\n");
        sb.Append("- Running elevated: ").Append(facts.IsElevated ? "yes" : "no").Append('\n');

        sb.Append("\n# Drives\n");
        if (facts.Drives.Count == 0) sb.Append("- (none found)\n");
        foreach (var d in facts.Drives)
        {
            var percent = d.TotalBytes > 0 ? 100.0 * d.FreeBytes / d.TotalBytes : 0;
            sb.Append("- ").Append(d.Name);
            if (!string.IsNullOrEmpty(d.Label)) sb.Append(" \"").Append(d.Label).Append('"');
            sb.Append(": size ").Append(AiFormat.Bytes(d.TotalBytes))
              .Append(", free ").Append(AiFormat.Bytes(d.FreeBytes))
              .Append(" (").Append(percent.ToString("0.0", inv)).Append("% free)\n");
        }

        sb.Append("\n# Cleanup modules\n");
        foreach (var m in modules)
        {
            sb.Append("- ").Append(m.DisplayName).Append(" (").Append(m.ModuleId).Append("): ");
            if (!m.Scanned)
            {
                sb.Append("not scanned\n");
                continue;
            }
            sb.Append(m.Count.ToString(inv)).Append(" items, ")
              .Append(m.SizesSummable ? AiFormat.Bytes(m.TotalBytes) : NotSummed).Append('\n');

            var top = m.SizesSummable
                ? m.Categories.OrderByDescending(c => c.Bytes).ThenByDescending(c => c.Count)
                : m.Categories.OrderByDescending(c => c.Count).ThenBy(c => c.Name, StringComparer.Ordinal);
            foreach (var c in top.Take(MaxCategories))
            {
                sb.Append("  - ").Append(c.Name).Append(": ").Append(c.Count.ToString(inv)).Append(" items, ")
                  .Append(m.SizesSummable ? AiFormat.Bytes(c.Bytes) : NotSummed).Append('\n');
            }
        }

        return sb.ToString();
    }
}
