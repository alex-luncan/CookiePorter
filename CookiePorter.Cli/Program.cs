using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CookiePorter.Core.Browsers;
using CookiePorter.Core.Models;

namespace CookiePorter.Cli
{
    internal static class Program
    {
        static async Task Main(string[] args)
        {
            if (args.Length == 0)
            {
                PrintHelp();
                return;
            }

            var opts = ParseArgs(args);

            switch (opts.Command)
            {
                case "detect":
                    RunDetect();
                    break;

                case "export":
                    await RunExportAsync(opts);
                    break;

                case "import":
                    await RunImportAsync(opts);
                    break;

                case "transfer":
                    await RunTransferAsync(opts);
                    break;

                case "export-test":
                    // keep your old test command for convenience
                    await RunExportTestAsync(args);
                    break;

                default:
                    Console.WriteLine($"Unknown command '{opts.Command}'.");
                    PrintHelp();
                    break;
            }
        }

        private static bool CookieMatchesFilters(
    CookieDto c,
    string[] domains,
    string[] names,
    bool includeSession,
    bool all)
        {
            // Session vs persistent
            if (!includeSession && !all)
            {
                // Chrome/Edge: expires_utc == 0 => session cookie
                if (c.ExpiresUtcChrome == 0)
                    return false;
            }

            // Domain filter
            if (domains.Length > 0)
            {
                bool any = domains.Any(pattern =>
                {
                    pattern = pattern.ToLowerInvariant();
                    var domain = c.Domain.ToLowerInvariant();

                    if (pattern.StartsWith("*.", StringComparison.Ordinal))
                    {
                        var suffix = pattern[1..]; // ".example.com"
                        return domain.EndsWith(suffix, StringComparison.Ordinal);
                    }

                    return domain == pattern ||
                           domain.EndsWith("." + pattern, StringComparison.Ordinal);
                });

                if (!any) return false;
            }

            // Name filter
            if (names.Length > 0)
            {
                bool any = names.Any(n => string.Equals(c.Name, n, StringComparison.Ordinal));
                if (!any) return false;
            }

            return true;
        }


        private static CliOptions ParseArgs(string[] args)
        {
            var opts = new CliOptions
            {
                Command = args[0].ToLowerInvariant()
            };

            // For legacy positional export: export edge Default out.json
            if (opts.Command == "export" && args.Length >= 4 && !args[1].StartsWith("--"))
            {
                opts.From = args[1].ToLowerInvariant();
                opts.FromProfile = args[2];
                opts.OutPath = args[3];
                return opts;
            }

            // For legacy export-test we still use positional parsing inside RunExportTestAsync.
            if (opts.Command == "export-test")
                return opts;

            // Generic --key value parser
            for (int i = 1; i < args.Length; i++)
            {
                var token = args[i];

                if (!token.StartsWith("--", StringComparison.Ordinal))
                    continue;

                string name = token.Substring(2).ToLowerInvariant();
                string? value = null;

                // boolean flags
                if (name is "include-session" or "all")
                {
                    switch (name)
                    {
                        case "include-session": opts.IncludeSession = true; break;
                        case "all": opts.All = true; break;
                    }
                    continue;
                }

                // expect a value
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                {
                    value = args[++i];
                }

                switch (name)
                {
                    case "from": opts.From = value?.ToLowerInvariant(); break;
                    case "to": opts.To = value?.ToLowerInvariant(); break;
                    case "profile": opts.Profile = value; break;
                    case "from-profile": opts.FromProfile = value; break;
                    case "to-profile": opts.ToProfile = value; break;
                    case "out": opts.OutPath = value; break;
                    case "in": opts.InPath = value; break;
                    case "domains":
                        opts.DomainFilters = (value ?? "")
                            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        break;
                    case "names":
                        opts.NameFilters = (value ?? "")
                            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        break;
                }
            }

            // normalise profiles
            if (opts.FromProfile == null && opts.Profile != null)
                opts.FromProfile = opts.Profile;
            if (opts.ToProfile == null && opts.Profile != null)
                opts.ToProfile = opts.Profile;

            return opts;
        }


        private static void KillEdgeProcesses()
            {
                try
                {
                    var names = new[] { "msedge", "msedgewebview2" };

                    foreach (var name in names)
                    {
                        var procs = Process.GetProcessesByName(name);
                        foreach (var p in procs)
                        {
                            try
                            {
                                Console.WriteLine($"[DEBUG] Killing {name} (PID {p.Id})");
                                p.Kill(true); // true = kill entire process tree
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[WARN] Could not kill {name} (PID {p.Id}): {ex.Message}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[WARN] Failed to enumerate Edge processes: " + ex.Message);
                }
            }

        private static void RunDetect()
        {
            var dirs = BrowserDetector.ChromiumUserDataDirs();
            if (dirs.Count == 0)
            {
                Console.WriteLine("No Chromium browsers detected.");
                return;
            }

            foreach (var kv in dirs)
            {
                Console.WriteLine($"{kv.Key} -> {kv.Value}");
                var profiles = BrowserDetector.ChromiumProfiles(kv.Value).ToArray();
                Console.WriteLine("  Profiles: " + (profiles.Length == 0 ? "(none)" : string.Join(", ", profiles)));
            }
        }

        private static async Task RunExportTestAsync(string[] args)
        {
            if (args.Length < 3)
            {
                Console.WriteLine("Usage: CookiePorter.Cli export-test <browser> <profile>");
                Console.WriteLine("Example: CookiePorter.Cli export-test edge Default");
                return;
            }

            var browserKey = args[1].ToLowerInvariant();
            var profile = args[2];

            if (browserKey != "edge")
            {
                Console.WriteLine("For now, export-test only supports 'edge' on this machine.");
                return;
            }

            Console.WriteLine($"Exporting cookies from {browserKey}:{profile} using EdgeCookieProvider...");
            KillEdgeProcesses();

            try
            {
                var provider = new EdgeCookieProvider(profile);
                var cookies = provider.Export();

                Console.WriteLine($"Found {cookies.Count} cookies.");
                foreach (var c in cookies.Take(10))
                {
                    Console.WriteLine($"- {c.Name} @ {c.Domain} = {Truncate(c.Value, 60)}");
                }

                if (cookies.Count > 10)
                    Console.WriteLine($"... and {cookies.Count - 10} more.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Export failed: " + ex.Message);
            }

            await Task.CompletedTask;
        }

        private static async Task RunExportAsync(CliOptions opts)
        {
            // From browser + profile
            var fromBrowser = opts.From ?? "edge";          // default edge for now
            var fromProfile = opts.FromProfile ?? "Default";

            if (string.IsNullOrWhiteSpace(opts.OutPath))
            {
                Console.WriteLine("Missing --out <file>. Example:");
                Console.WriteLine("  CookiePorter.Cli export --from edge --profile Default --out out.json");
                return;
            }

            var fullOutputPath = Path.GetFullPath(opts.OutPath);

            if (!string.Equals(fromBrowser, "edge", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Right now 'export' only supports --from edge.");
                return;
            }

            Console.WriteLine($"Exporting cookies from {fromBrowser}:{fromProfile} to {fullOutputPath} ...");
            Console.WriteLine("Filters:");
            Console.WriteLine($"  Domains: {(opts.DomainFilters.Length == 0 ? "(none)" : string.Join(", ", opts.DomainFilters))}");
            Console.WriteLine($"  Names:   {(opts.NameFilters.Length == 0 ? "(none)" : string.Join(", ", opts.NameFilters))}");
            Console.WriteLine($"  Include session: {(opts.IncludeSession || opts.All ? "yes" : "no")}");

            KillEdgeProcesses();

            try
            {
                var provider = new EdgeCookieProvider(fromProfile);
                var cookies = provider.Export();

                var filtered = cookies
                    .Where(c => CookieMatchesFilters(
                        c,
                        opts.DomainFilters,
                        opts.NameFilters,
                        opts.IncludeSession,
                        opts.All))
                    .ToList();

                Console.WriteLine($"Collected {cookies.Count} cookies, {filtered.Count} after filters. Writing JSON...");

                var exportObject = new
                {
                    browser = fromBrowser,
                    profile = fromProfile,
                    exportedAtUtc = DateTime.UtcNow,
                    filters = new
                    {
                        domains = opts.DomainFilters,
                        names = opts.NameFilters,
                        includeSession = opts.IncludeSession,
                        all = opts.All
                    },
                    cookies = filtered
                };

                var json = JsonSerializer.Serialize(exportObject,
                    new JsonSerializerOptions { WriteIndented = true });

                Directory.CreateDirectory(Path.GetDirectoryName(fullOutputPath)!);
                await File.WriteAllTextAsync(fullOutputPath, json);

                Console.WriteLine("Done.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Export failed: " + ex.Message);
            }
        }


        private static async Task RunImportAsync(CliOptions opts)
        {
            if (string.IsNullOrWhiteSpace(opts.InPath))
            {
                Console.WriteLine("Missing --in <file>. Example:");
                Console.WriteLine("  CookiePorter.Cli import --to edge --profile Default --in out.json");
                return;
            }

            var toBrowser = opts.To ?? "edge";
            var toProfile = opts.ToProfile ?? "Default";
            var fullInputPath = Path.GetFullPath(opts.InPath);

            if (!File.Exists(fullInputPath))
            {
                Console.WriteLine($"Input file not found: {fullInputPath}");
                return;
            }

            Console.WriteLine($"Loading cookies from {fullInputPath} ...");

            try
            {
                using var stream = File.OpenRead(fullInputPath);
                using var doc = await JsonDocument.ParseAsync(stream);

                var root = doc.RootElement;

                var cookiesElem = root.GetProperty("cookies");
                var cookies = JsonSerializer.Deserialize<CookieDto[]>(cookiesElem.GetRawText())
                              ?? Array.Empty<CookieDto>();

                var filtered = cookies
                    .Where(c => CookieMatchesFilters(
                        c,
                        opts.DomainFilters,
                        opts.NameFilters,
                        opts.IncludeSession,
                        opts.All))
                    .ToList();

                Console.WriteLine($"File contains {cookies.Length} cookies, {filtered.Count} after filters.");
                Console.WriteLine($"Target browser: {toBrowser}, profile: {toProfile}");

                if (!string.Equals(toBrowser, "edge", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("Right now 'import' is only designed for --to edge and is NOT yet writing to the DB.");
                }

                Console.WriteLine();
                Console.WriteLine("NOTE: For safety, this command currently DOES NOT modify your browser profile.");
                Console.WriteLine("      It shows how many cookies would be imported so we can later wire a safe DB writer.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Import failed: " + ex.Message);
            }
        }


        private static async Task RunTransferAsync(CliOptions opts)
        {
            var fromBrowser = opts.From ?? "edge";
            var toBrowser = opts.To ?? "edge";

            var fromProfile = opts.FromProfile ?? "Default";
            var toProfile = opts.ToProfile ?? fromProfile;

            if (!string.Equals(fromBrowser, "edge", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(toBrowser, "edge", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Right now 'transfer' is only wired for edge→edge in-memory transfer.");
            }

            Console.WriteLine($"Transferring cookies {fromBrowser}:{fromProfile} → {toBrowser}:{toProfile}");
            Console.WriteLine("Filters:");
            Console.WriteLine($"  Domains: {(opts.DomainFilters.Length == 0 ? "(none)" : string.Join(", ", opts.DomainFilters))}");
            Console.WriteLine($"  Names:   {(opts.NameFilters.Length == 0 ? "(none)" : string.Join(", ", opts.NameFilters))}");
            Console.WriteLine($"  Include session: {(opts.IncludeSession || opts.All ? "yes" : "no")}");

            KillEdgeProcesses();

            try
            {
                var provider = new EdgeCookieProvider(fromProfile);
                var cookies = provider.Export();

                var filtered = cookies
                    .Where(c => CookieMatchesFilters(
                        c,
                        opts.DomainFilters,
                        opts.NameFilters,
                        opts.IncludeSession,
                        opts.All))
                    .ToList();

                Console.WriteLine($"Source has {cookies.Count} cookies, {filtered.Count} after filters.");
                Console.WriteLine($"Target browser: {toBrowser}, profile: {toProfile}");
                Console.WriteLine();
                Console.WriteLine("NOTE: For safety, 'transfer' currently does NOT write to the target browser DB.");
                Console.WriteLine("      When we are ready, this will reuse the same DB-writer as 'import'.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Transfer failed: " + ex.Message);
            }

            await Task.CompletedTask;
        }



        private static string Truncate(string? s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max) + "...";
        }

        private static void PrintHelp()
        {
            Console.WriteLine("CookiePorter CLI");
            Console.WriteLine();
            Console.WriteLine("Commands:");
            Console.WriteLine("  detect                          Detect installed browsers and their profiles.");
            Console.WriteLine("  export                          Export cookies to a JSON file.");
            Console.WriteLine("  import                          Import cookies from a JSON file into a browser profile (no DB write yet).");
            Console.WriteLine("  transfer                        Directly transfer cookies between browsers (planned).");
            Console.WriteLine();
            Console.WriteLine("Common options:");
            Console.WriteLine("  --from, --to                    Browser key (chrome, edge, brave, opera, firefox)");
            Console.WriteLine("  --profile                       Browser profile name (applied to from/to when relevant)");
            Console.WriteLine("  --from-profile, --to-profile    Source/target profile names");
            Console.WriteLine("  --domains                       Comma-separated domain filters (\"*.example.com,foo.com\")");
            Console.WriteLine("  --names                         Comma-separated cookie name filters");
            Console.WriteLine("  --include-session               Include session cookies");
            Console.WriteLine("  --all                           Include ALL cookies including session ones");
            Console.WriteLine("  --out                           Export file path (JSON)");
            Console.WriteLine("  --in                            Import file path (JSON)");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  CookiePorter.Cli detect");
            Console.WriteLine("  CookiePorter.Cli export --from edge --profile Default --out out.json");
            Console.WriteLine("  CookiePorter.Cli import --to edge --profile Default --in out.json");
            Console.WriteLine("  CookiePorter.Cli transfer --from edge --from-profile Default --to edge --to-profile TestProfile");
        }

    }

    internal sealed class CliOptions
        {
            public string Command { get; set; } = "";

            public string? From { get; set; }
            public string? To { get; set; }

            public string? Profile { get; set; }
            public string? FromProfile { get; set; }
            public string? ToProfile { get; set; }

            public string? OutPath { get; set; }
            public string? InPath { get; set; }

            public string[] DomainFilters { get; set; } = Array.Empty<string>();
            public string[] NameFilters { get; set; } = Array.Empty<string>();

            public bool IncludeSession { get; set; }
            public bool All { get; set; }
        }

}
