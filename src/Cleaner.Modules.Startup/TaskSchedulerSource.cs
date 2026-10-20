using System.Runtime.Versioning;
using Cleaner.Core.Localization;

namespace Cleaner.Modules.Startup;

/// <summary>
/// Ajoitetut tehtävät, joilla on kirjautumistriggeri. Käyttää Task Schedulerin COM-rajapintaa
/// (Schedule.Service) myöhäissidonnaisesti, jotta ei tarvita NuGet-paketteja tai interop-kokoonpanoja.
///
/// Tarkoituksella vain juurikansio ("\"): kolmannen osapuolen ohjelmat rekisteröivät tehtävänsä sinne,
/// Windowsin omat ovat alikansiossa \Microsoft\, eikä niitä pidä poistaa käytöstä siivouksen nimissä.
/// Piilotetut tehtävät ohitetaan.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class TaskSchedulerSource
{
    public sealed record LogonTask(string Path, string Name, string Command);

    private const int TaskTriggerLogon = 9;   // TASK_TRIGGER_LOGON
    private const int TaskActionExec = 0;     // TASK_ACTION_EXEC
    private const int EnumVisibleOnly = 0;    // ei TASK_ENUM_HIDDEN

    public static List<LogonTask> ListLogonTasks()
    {
        var list = new List<LogonTask>();
        dynamic service = CreateService();
        dynamic root = service.GetFolder("\\");
        dynamic tasks = root.GetTasks(EnumVisibleOnly);
        int count = tasks.Count;

        for (int i = 1; i <= count; i++)
        {
            dynamic task = tasks.Item(i);
            try
            {
                if (!(bool)task.Enabled) continue;
                dynamic definition = task.Definition;
                if (!HasEnabledLogonTrigger(definition.Triggers)) continue;

                list.Add(new LogonTask((string)task.Path, (string)task.Name, DescribeActions(definition.Actions)));
            }
            catch (Exception)
            {
                // Yksittäisen tehtävän lukuvirhe (oikeudet, rikkinäinen määrittely) ei kaada listausta.
            }
        }

        return list;
    }

    /// <summary>Poistaa tehtävän käytöstä (Enabled = false). Tehtävä säilyy ja näkyy Tehtävien ajoituksessa.</summary>
    public static void Disable(string taskPath)
    {
        dynamic service = CreateService();
        dynamic root = service.GetFolder("\\");
        dynamic task = root.GetTask(taskPath.TrimStart('\\'));
        task.Enabled = false;
    }

    public static bool Exists(string taskPath)
    {
        try
        {
            dynamic service = CreateService();
            dynamic root = service.GetFolder("\\");
            dynamic task = root.GetTask(taskPath.TrimStart('\\'));
            return task is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static dynamic CreateService()
    {
        var type = Type.GetTypeFromProgID("Schedule.Service")
                   ?? throw new InvalidOperationException(Loc.T("Task Scheduler service (Schedule.Service) not found.", "Task Scheduler -palvelua (Schedule.Service) ei löydy."));
        dynamic service = Activator.CreateInstance(type)!;
        service.Connect();
        return service;
    }

    private static bool HasEnabledLogonTrigger(dynamic triggers)
    {
        int count = triggers.Count;
        for (int j = 1; j <= count; j++)
        {
            dynamic trigger = triggers.Item(j);
            if ((int)trigger.Type == TaskTriggerLogon && (bool)trigger.Enabled)
                return true;
        }
        return false;
    }

    private static string DescribeActions(dynamic actions)
    {
        int count = actions.Count;
        var parts = new List<string>();
        for (int j = 1; j <= count; j++)
        {
            dynamic action = actions.Item(j);
            if ((int)action.Type != TaskActionExec) { parts.Add(Loc.T("(not a program launch)", "(muu kuin ohjelman käynnistys)")); continue; }
            string path = action.Path ?? "";
            string args = action.Arguments ?? "";
            parts.Add(string.IsNullOrWhiteSpace(args) ? path : $"{path} {args}");
        }
        return string.Join(" ; ", parts);
    }
}
