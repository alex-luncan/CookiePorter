using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace CookiePorter.Core.Crypto
{
    internal static class ChromiumCrypto
    {
        public static byte[] GetUnwrappedAesKey(string localStatePath)
        {
            var json = File.ReadAllText(localStatePath);
            using var doc = JsonDocument.Parse(json);

            var b64 = doc.RootElement
                .GetProperty("os_crypt")
                .GetProperty("encrypted_key")
                .GetString();

            if (string.IsNullOrEmpty(b64))
                throw new InvalidOperationException("os_crypt.encrypted_key not found in Local State.");

            var raw = Convert.FromBase64String(b64);

            // First 5 bytes are "DPAPI"
            const int prefixLen = 5;
            var dpapiBlob = raw.AsSpan(prefixLen).ToArray();

            var unwrapped = ProtectedData.Unprotect(
                dpapiBlob,
                optionalEntropy: null,
                scope: DataProtectionScope.CurrentUser);

            return unwrapped; // AES key
        }
    }
}
