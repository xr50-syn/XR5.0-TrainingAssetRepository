using System.Text.Json;
using System.Text.Json.Serialization;

namespace XR50TrainingAssetRepo.Services.Migrations
{
    /// <summary>
    /// <c>dotnet XR50TrainingAssetRepo.dll migrate [options]</c>: runs the schema migrator from
    /// the command line instead of serving HTTP, for operators and container entrypoints.
    /// </summary>
    public static class MigrateCli
    {
        public const string Verb = "migrate";

        public const int ExitOk = 0;
        public const int ExitFailed = 1;
        public const int ExitUsage = 2;
        public const int ExitManualIntervention = 3;

        public enum Mode { All, Central, Tenants, Status }

        public sealed record Command(
            Mode Mode,
            IReadOnlyList<string> Tenants,
            bool AdoptLegacy,
            bool TolerateTenantFailures,
            string? TargetMigration,
            bool Json)
        {
            public MigrateOptions ToOptions() => new()
            {
                AdoptLegacy = AdoptLegacy,
                TolerateTenantFailures = TolerateTenantFailures,
                TargetMigration = TargetMigration
            };
        }

        public const string Usage =
            "usage: migrate [--status] [--all | --central | --tenant <name> ...] [--target <migrationId>]\n" +
            "               [--adopt-legacy | --no-adopt-legacy] [--tolerate-tenant-failures] [--json]\n" +
            "  --status                    report state, applied and pending migrations; never changes anything\n" +
            "  --all                       central database then every registered tenant (default)\n" +
            "  --central                   registry and training schema of the base database only\n" +
            "  --tenant <name>             one tenant database (repeatable); the tenant need not be registered\n" +
            "  --target <migrationId>      migrate a single tenant to this migration (also downgrades); requires exactly one --tenant\n" +
            "  --no-adopt-legacy           refuse databases provisioned before migrations instead of adopting them\n" +
            "  --tolerate-tenant-failures  with --all: exit 0 even if some tenants failed (central must succeed)\n" +
            "  --json                      machine-readable output\n" +
            "exit codes: 0 ok, 1 failed, 2 usage, 3 manual intervention required";

        /// <summary>Separates the verb from the rest of the process arguments.</summary>
        public static (bool IsMigrate, string[] Remaining) Split(string[] args) =>
            args.Length > 0 && string.Equals(args[0], Verb, StringComparison.Ordinal)
                ? (true, args[1..])
                : (false, args);

        public static bool TryParse(string[] args, out Command command, out string? error)
        {
            var mode = (Mode?)null;
            var tenants = new List<string>();
            var adopt = true;
            var tolerate = false;
            string? target = null;
            var json = false;
            var status = false;

            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--status": status = true; break;
                    case "--all": mode = SetMode(mode, Mode.All, out error); if (error is not null) { command = null!; return false; } break;
                    case "--central": mode = SetMode(mode, Mode.Central, out error); if (error is not null) { command = null!; return false; } break;
                    case "--tenant":
                        if (++i >= args.Length) { error = "--tenant needs a tenant name"; command = null!; return false; }
                        mode = SetMode(mode, Mode.Tenants, out error); if (error is not null) { command = null!; return false; }
                        tenants.Add(args[i]);
                        break;
                    case "--target":
                        if (++i >= args.Length) { error = "--target needs a migration id"; command = null!; return false; }
                        target = args[i];
                        break;
                    case "--adopt-legacy": adopt = true; break;
                    case "--no-adopt-legacy": adopt = false; break;
                    case "--tolerate-tenant-failures": tolerate = true; break;
                    case "--json": json = true; break;
                    case "--help": case "-h": error = Usage; command = null!; return false;
                    default: error = $"unknown argument '{args[i]}'\n{Usage}"; command = null!; return false;
                }
            }

            if (status)
            {
                if (tenants.Count > 1 || target is not null || mode is Mode.Central)
                {
                    error = "--status takes at most one --tenant and no other options"; command = null!; return false;
                }

                command = new Command(Mode.Status, tenants, adopt, tolerate, null, json);
                error = null;
                return true;
            }

            if (target is not null && (mode != Mode.Tenants || tenants.Count != 1))
            {
                error = "--target requires exactly one --tenant"; command = null!; return false;
            }

            command = new Command(mode ?? Mode.All, tenants, adopt, tolerate, target, json);
            error = null;
            return true;
        }

        private static Mode? SetMode(Mode? current, Mode requested, out string? error)
        {
            error = current is null || current == requested ? null : $"--all, --central and --tenant cannot be combined\n{Usage}";
            return requested;
        }

        public static async Task<int> RunAsync(IXR50SchemaMigrator migrator, string[] args, TextWriter output, CancellationToken cancellationToken)
        {
            if (!TryParse(args, out var command, out var error))
            {
                await output.WriteLineAsync(error);
                return ExitUsage;
            }

            return await RunAsync(migrator, command, output, cancellationToken);
        }

        public static async Task<int> RunAsync(IXR50SchemaMigrator migrator, Command command, TextWriter output, CancellationToken cancellationToken)
        {
            if (command.Mode == Mode.Status)
            {
                var statuses = await migrator.GetStatusAsync(command.Tenants.FirstOrDefault(), cancellationToken);
                await output.WriteLineAsync(command.Json ? ToJson(statuses) : FormatStatuses(statuses));
                return statuses.Any(s => s.State is SchemaState.Unknown) ? ExitManualIntervention : ExitOk;
            }

            IReadOnlyList<MigrationRunResult> results;
            IReadOnlyList<string> orphans = Array.Empty<string>();
            bool succeeded;
            var options = command.ToOptions();

            switch (command.Mode)
            {
                case Mode.All:
                    var report = await migrator.MigrateAllAsync(options, cancellationToken);
                    results = report.Results;
                    orphans = report.OrphanSchemas;
                    succeeded = report.Succeeded;
                    break;
                case Mode.Central:
                    results = await migrator.MigrateCentralAsync(options, cancellationToken);
                    succeeded = results.All(r => r.Succeeded);
                    break;
                default:
                    var list = new List<MigrationRunResult>();
                    foreach (var tenant in command.Tenants)
                    {
                        list.Add(await migrator.MigrateTenantAsync(tenant, options, cancellationToken));
                    }
                    results = list;
                    succeeded = results.All(r => r.Succeeded);
                    break;
            }

            await output.WriteLineAsync(command.Json
                ? ToJson(new { succeeded, results, orphanSchemas = orphans })
                : FormatResults(results, orphans, succeeded));

            if (results.Any(r => r.ManualInterventionRequired))
            {
                return ExitManualIntervention;
            }

            return succeeded ? ExitOk : ExitFailed;
        }

        private static string FormatResults(IReadOnlyList<MigrationRunResult> results, IReadOnlyList<string> orphans, bool succeeded)
        {
            var lines = results.Select(r =>
            {
                var verdict = !r.Succeeded ? "FAILED " : r.Adopted ? "ADOPTED" : "OK     ";
                var applied = r.AppliedNow.Count == 0 ? "nothing to apply" : $"applied {r.AppliedNow.Count}: {string.Join(", ", r.AppliedNow)}";
                return $"{verdict} {r.Target} [{r.StateBefore}] {(r.Succeeded ? applied : r.Error)}";
            }).ToList();

            foreach (var orphan in orphans)
            {
                lines.Add($"ORPHAN  {orphan} is not in the tenant registry; left untouched");
            }

            lines.Add(succeeded ? "Migration succeeded." : "Migration FAILED.");
            return string.Join(Environment.NewLine, lines);
        }

        private static string FormatStatuses(IReadOnlyList<MigrationTargetStatus> statuses) =>
            string.Join(Environment.NewLine, statuses.Select(s =>
                $"{s.State,-18} {s.Target} applied {s.Applied.Count}, pending {s.Pending.Count}{(s.Error is null ? "" : $" ({s.Error})")}"));

        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private static string ToJson(object value) => JsonSerializer.Serialize(value, JsonOptions);
    }
}
