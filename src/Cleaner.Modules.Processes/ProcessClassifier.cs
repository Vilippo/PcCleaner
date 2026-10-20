using Cleaner.Core.Models;

namespace Cleaner.Modules.Processes;

/// <summary>
/// Pudottaa suojatut prosessit, sulauttaa saman exen lapset vanhempaansa ja antaa jokaiselle juurelle
/// täsmälleen yhden kategorian. Puhdasta logiikkaa, ei Windows-kutsuja.
/// </summary>
public static class ProcessClassifier
{
    public static IReadOnlyList<ClassifiedProcess> Classify(ProcessSnapshot snapshot, IReadOnlyList<ProcessRule> rules)
    {
        // 1. Kaikki elossa olevat, myös suojatut: orpotarkistus tarvitsee ne.
        var byPid = new Dictionary<int, ProcessInfo>();
        foreach (var p in snapshot.Processes)
            byPid.TryAdd(p.Pid, p);

        // 2. Suojatut pois ehdokkaista.
        var candidates = new Dictionary<int, ProcessInfo>();
        foreach (var p in byPid.Values)
        {
            if (ProcessGuard.Protects(p, snapshot) is null)
                candidates[p.Pid] = p;
        }

        // 3. Sulautus: seuraa ketjua ylimpään saman exen ehdokasvanhempaan. Jo ratkaistu juuri muistetaan,
        //    jolloin sykli (PID-kierrätys yhtä suurilla käynnistysajoilla) päätyy aina samaan juureen eikä ryhmä katoa.
        var children = new Dictionary<int, List<ProcessInfo>>();
        var rootOf = new Dictionary<int, int>();
        foreach (var x in candidates.Values.OrderBy(c => c.Pid))
        {
            if (rootOf.ContainsKey(x.Pid)) continue;

            var path = new List<ProcessInfo> { x };
            var current = x;
            int rootPid;
            while (true)
            {
                if (!candidates.TryGetValue(current.ParentPid, out var parent) || !IsSameExeChild(current, parent))
                {
                    rootPid = current.Pid;
                    break;
                }
                if (rootOf.TryGetValue(parent.Pid, out var known))
                {
                    rootPid = known;
                    break;
                }
                if (path.Any(p => p.Pid == parent.Pid))
                {
                    rootPid = current.Pid; // sykli: nykyinen jää juureksi
                    break;
                }
                path.Add(parent);
                current = parent;
            }

            foreach (var member in path)
            {
                rootOf[member.Pid] = rootPid;
                if (member.Pid == rootPid) continue;
                if (!children.TryGetValue(rootPid, out var list))
                    children[rootPid] = list = new List<ProcessInfo>();
                list.Add(member);
            }
        }

        var merged = new HashSet<int>(rootOf.Where(kv => kv.Key != kv.Value).Select(kv => kv.Key));

        var result = new List<ClassifiedProcess>();
        foreach (var root in candidates.Values)
        {
            if (merged.Contains(root.Pid)) continue;

            var kids = children.TryGetValue(root.Pid, out var list)
                ? list.OrderBy(c => c.Pid).ToList()
                : new List<ProcessInfo>();

            // 4. Orpo: vanhempi ei ole elossa, tai elossa oleva vanhempi on nuorempi (PID kierrätetty).
            ProcessInfo? parent = null;
            var orphan = true;
            if (byPid.TryGetValue(root.ParentPid, out var candidateParent) && candidateParent.StartFileTime <= root.StartFileTime)
            {
                parent = candidateParent;
                orphan = false;
            }

            // 5. Kategoria: ensimmäinen osuva voittaa. Sääntö haetaan aina kuvausta varten.
            var rule = rules.FirstOrDefault(r => r.Matches(root.Name));
            var group = new ClassifiedProcess(root, kids, ProcessCategory.ActiveBackground, RiskLevel.Medium, false, rule, parent);

            ProcessCategory category;
            RiskLevel risk;
            bool preselected;
            if (group.HasVisibleWindow)
                (category, risk, preselected) = (ProcessCategory.Windowed, RiskLevel.High, false);
            else if (root.ExeMissing)
                (category, risk, preselected) = (ProcessCategory.MissingExe, RiskLevel.Medium, true);
            else if (orphan)
                (category, risk, preselected) = (ProcessCategory.Orphan, RiskLevel.Medium, group.IsIdle);
            else if (rule is not null)
                (category, risk, preselected) = (ProcessCategory.Known, rule.Risk, false);
            else if (group.IsIdle)
                (category, risk, preselected) = (ProcessCategory.IdleBackground, RiskLevel.Medium, false);
            else
                (category, risk, preselected) = (ProcessCategory.ActiveBackground, RiskLevel.Medium, false);

            result.Add(group with { Category = category, Risk = risk, Preselected = preselected });
        }

        // 6. Kategorian enum-järjestys, sitten muisti laskevasti.
        return result
            .OrderBy(c => c.Category)
            .ThenByDescending(c => c.TotalPrivateBytes)
            .ThenBy(c => c.Root.Pid)
            .ToList();
    }

    /// <summary>
    /// Pid:n saman exen jälkeläiset transitiivisesti (sama ExePath OrdinalIgnoreCase, StartFileTime &gt;= vanhemman),
    /// syvyysjärjestyksessä lapsista lapsenlapsiin, suojattuja ei suodateta. Tyhjä, jos pid ei ole näytteessä.
    /// </summary>
    public static IReadOnlyList<ProcessInfo> CollectSameExeDescendants(ProcessSnapshot snapshot, int pid)
    {
        var root = snapshot.Processes.FirstOrDefault(p => p.Pid == pid);
        if (root is null) return Array.Empty<ProcessInfo>();

        var result = new List<ProcessInfo>();
        var visited = new HashSet<int> { pid };
        var level = new List<ProcessInfo> { root };

        while (level.Count > 0)
        {
            var next = new List<ProcessInfo>();
            foreach (var parent in level)
            {
                foreach (var child in snapshot.Processes)
                {
                    if (child.ParentPid != parent.Pid || !IsSameExeChild(child, parent)) continue;
                    if (!visited.Add(child.Pid)) continue;
                    next.Add(child);
                }
            }
            next.Sort((a, b) => a.Pid.CompareTo(b.Pid));
            result.AddRange(next);
            level = next;
        }

        return result;
    }

    /// <summary>Sulautusehto: sama exe-polku ja lapsi käynnistynyt aikaisintaan vanhemman kanssa.</summary>
    private static bool IsSameExeChild(ProcessInfo child, ProcessInfo parent)
        => child.ExePath is not null
           && parent.ExePath is not null
           && string.Equals(child.ExePath, parent.ExePath, StringComparison.OrdinalIgnoreCase)
           && parent.StartFileTime <= child.StartFileTime;
}
