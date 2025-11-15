using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using CookiePorter.Core.Crypto;
using CookiePorter.Core.Models;
using Microsoft.Data.Sqlite;

namespace CookiePorter.Core.Browsers
{
    public sealed class EdgeCookieProvider
    {
        private readonly string _profileName; // e.g. "Default"

        public EdgeCookieProvider(string profileName)
        {
            _profileName = profileName ?? throw new ArgumentNullException(nameof(profileName));
        }

        public List<CookieDto> Export()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            // Paths
            var userDataRoot = Path.Combine(localAppData, "Microsoft", "Edge", "User Data");
            var profileDir = Path.Combine(userDataRoot, _profileName);

            var localState = Path.Combine(userDataRoot, "Local State");
            var candidate1 = Path.Combine(profileDir, "Network", "Cookies");
            var candidate2 = Path.Combine(profileDir, "Cookies");

            var dbPath = File.Exists(candidate1)
                ? candidate1
                : File.Exists(candidate2)
                    ? candidate2
                    : throw new FileNotFoundException("Edge cookies DB not found.", candidate1);

            if (!File.Exists(localState))
                throw new FileNotFoundException("Edge Local State file not found.", localState);

            Console.WriteLine($"[DEBUG] Local State: {localState}");
            Console.WriteLine($"[DEBUG] Cookies DB (source): {dbPath}");

            // 1) Get AES key from Local State
            var aesKey = ChromiumCrypto.GetUnwrappedAesKey(localState);

            // 2) Copy DB (and wal/shm) to a private temp folder so no locks can hurt us
            var tempRoot = Path.Combine(Path.GetTempPath(), "CookiePorter", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            string CopyWithShare(string sourcePath)
            {
                if (!File.Exists(sourcePath))
                    return string.Empty;

                var destPath = Path.Combine(tempRoot, Path.GetFileName(sourcePath));

                using (var src = new FileStream(
                           sourcePath,
                           FileMode.Open,
                           FileAccess.Read,
                           FileShare.ReadWrite | FileShare.Delete))  // allow Edge to have it open
                using (var dst = new FileStream(
                           destPath,
                           FileMode.Create,
                           FileAccess.Write,
                           FileShare.None))
                {
                    src.CopyTo(dst);
                }

                return destPath;
            }

            var tempDbPath = CopyWithShare(dbPath);
            var tempWalPath = CopyWithShare(dbPath + "-wal");
            var tempShmPath = CopyWithShare(dbPath + "-shm");

            Console.WriteLine($"[DEBUG] Cookies DB (temp): {tempDbPath}");
            if (!string.IsNullOrEmpty(tempWalPath)) Console.WriteLine($"[DEBUG] Cookies WAL (temp): {tempWalPath}");
            if (!string.IsNullOrEmpty(tempShmPath)) Console.WriteLine($"[DEBUG] Cookies SHM (temp): {tempShmPath}");

            if (!File.Exists(tempDbPath))
                throw new FileNotFoundException("Temp cookies DB copy was not created.", tempDbPath);

            var result = new List<CookieDto>();

            try
            {
                // 3) Open ONLY the temp DB
                var csb = new SqliteConnectionStringBuilder
                {
                    DataSource = tempDbPath,
                    Mode = SqliteOpenMode.ReadOnly,
                    Cache = SqliteCacheMode.Shared
                };

                using (var conn = new SqliteConnection(csb.ToString()))
                {
                    conn.Open();

                    using var cmd = conn.CreateCommand();
                    cmd.CommandText =
                        "SELECT name, encrypted_value, host_key, path, is_httponly, is_secure, expires_utc FROM cookies";

                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        var name = reader.GetString(0);
                        var encBytes = (byte[])reader[1];
                        var domain = reader.GetString(2);
                        var path = reader.GetString(3);
                        var httpOnly = reader.GetBoolean(4);
                        var secure = reader.GetBoolean(5);
                        var expires = reader.GetInt64(6);

                        var value = DecryptCookie(encBytes, aesKey);

                        var dto = new CookieDto
                        {
                            Name = name,
                            Value = value,
                            Domain = domain,
                            Path = path,
                            HttpOnly = httpOnly,
                            Secure = secure,
                            ExpiresUtcChrome = expires
                        };

                        result.Add(dto);
                    }
                }
            }
            finally
            {
                try
                {
                    // Clean up temp folder
                    Directory.Delete(tempRoot, recursive: true);
                }
                catch
                {
                    // Ignore clean-up errors
                }
            }

            return result;
        }


        private static string DecryptCookie(byte[] encryptedValue, byte[] aesKey)
        {
            if (encryptedValue == null || encryptedValue.Length == 0)
                return string.Empty;

            // New format: "v10" or "v11" + nonce + ciphertext + tag (AES-GCM)
            if (encryptedValue.Length > 3 &&
                encryptedValue[0] == (byte)'v' &&
                encryptedValue[1] == (byte)'1' &&
                (encryptedValue[2] == (byte)'0' || encryptedValue[2] == (byte)'1'))
            {
                const int versionLength = 3;
                const int nonceLength = 12;
                const int tagLength = 16;

                var nonce = encryptedValue.AsSpan(versionLength, nonceLength);
                var cipherPlus = encryptedValue.AsSpan(versionLength + nonceLength);

                if (cipherPlus.Length < tagLength)
                    return string.Empty;

                var ciphertext = cipherPlus[..^tagLength];
                var tag = cipherPlus[^tagLength..];

                var plaintext = new byte[ciphertext.Length];

                using var aesGcm = new AesGcm(aesKey);
                aesGcm.Decrypt(nonce, ciphertext, tag, plaintext, null);

                return Encoding.UTF8.GetString(plaintext);
            }

            // Old DPAPI format
            try
            {
                var decrypted = ProtectedData.Unprotect(
                    encryptedValue,
                    null,
                    DataProtectionScope.CurrentUser);

                return Encoding.UTF8.GetString(decrypted);
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
