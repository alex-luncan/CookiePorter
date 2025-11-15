using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CookiePorter.Core.Browsers
{
    public static class BrowserDetector
    {
        public static IReadOnlyDictionary<string, string> ChromiumUserDataDirs()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            var map = new Dictionary<string, string>
            {
                ["chrome"] = Path.Combine(localAppData, "Google", "Chrome", "User Data"),
                ["edge"] = Path.Combine(localAppData, "Microsoft", "Edge", "User Data"),
                ["brave"] = Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "User Data"),
                ["opera"] = Path.Combine(roaming, "Opera Software", "Opera Stable")
            };

            return map
                .Where(kv => Directory.Exists(kv.Value))
                .ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        public static IEnumerable<string> ChromiumProfiles(string userDataDir)
        {
            if (!Directory.Exists(userDataDir))
                yield break;

            foreach (var dir in Directory.EnumerateDirectories(userDataDir))
            {
                var name = Path.GetFileName(dir);

                // Ignore system folders
                if (string.Equals(name, "System Profile", StringComparison.OrdinalIgnoreCase))
                    continue;

                var cookies1 = Path.Combine(dir, "Network", "Cookies");
                var cookies2 = Path.Combine(dir, "Cookies");

                if (File.Exists(cookies1) || File.Exists(cookies2))
                    yield return name;
            }
        }
    }
}