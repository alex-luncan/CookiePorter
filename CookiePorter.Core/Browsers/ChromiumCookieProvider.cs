using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CookiePorter.Core.Models;
using Microsoft.Playwright;

namespace CookiePorter.Core.Browsers
{
    public sealed class ChromiumCookieProvider
    {
        private readonly string _browserKey;   // "chrome", "edge", ...
        private readonly string _profileName;  // e.g. "Default", "Profile 1"

        private static readonly Dictionary<string, string> ChannelMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["chrome"] = "chrome",
                ["edge"] = "msedge",
            };

        public ChromiumCookieProvider(string browserKey, string profileName)
        {
            _browserKey = browserKey ?? throw new ArgumentNullException(nameof(browserKey));
            _profileName = profileName ?? throw new ArgumentNullException(nameof(profileName));
        }

        public async Task<List<CookieDto>> ExportAsync()
        {
            // userDataRoot = ...\User Data
            var dirs = BrowserDetector.ChromiumUserDataDirs();
            if (!dirs.TryGetValue(_browserKey.ToLowerInvariant(), out var userDataRoot))
                throw new InvalidOperationException($"Browser '{_browserKey}' not detected on this machine.");

            if (!ChannelMap.TryGetValue(_browserKey.ToLowerInvariant(), out var channel))
                throw new InvalidOperationException(
                    $"No Playwright channel configured for browser key '{_browserKey}'.");

            // Validate that the requested profile directory exists under User Data
            var profileDir = Path.Combine(userDataRoot, _profileName);
            if (!Directory.Exists(profileDir))
                throw new DirectoryNotFoundException($"Profile '{_profileName}' not found at '{profileDir}'.");

            Console.WriteLine($"[DEBUG] Using Playwright channel '{channel}'");
            Console.WriteLine($"[DEBUG] User Data root: {userDataRoot}");
            Console.WriteLine($"[DEBUG] Profile name: {_profileName}");

            using var playwright = await Playwright.CreateAsync();

            // IMPORTANT:
            //  - userDataDir  = root "User Data" folder
            //  - profile is chosen via --profile-directory=ProfileName
            var options = new BrowserTypeLaunchPersistentContextOptions
            {
                Channel = channel,
                Headless = false,
                Args = new[] { $"--profile-directory={_profileName}",
                                "--window-position=-32000,-32000" }
            };

            await using var context = await playwright.Chromium.LaunchPersistentContextAsync(
                userDataDir: userDataRoot,
                options);

            var pwCookies = await context.CookiesAsync();

            var result = pwCookies.Select(c =>
            {
                long expiresMicro = 0;
                // Expires is seconds since Unix epoch; 0 => session cookie
                if (c.Expires > 0)
                    expiresMicro = (long)(c.Expires * 1_000_000L);

                return new CookieDto
                {
                    Name = c.Name,
                    Value = c.Value,
                    Domain = c.Domain,
                    Path = c.Path,
                    HttpOnly = c.HttpOnly,
                    Secure = c.Secure,
                    ExpiresUtcChrome = expiresMicro
                };
            }).ToList();

            return result;
        }
    }
}
