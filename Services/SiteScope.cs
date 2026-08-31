using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LogicAppStorageInspector.Services
{
    // Resolves the current site table prefix so queries never cross into another
    // site sharing the same storage account. Fail-safe: refuses rather than guess.
    public sealed class SiteScope
    {
        private readonly StorageContext _storage;
        private readonly SemaphoreSlim _gate = new(1, 1);

        public string SiteName { get; }
        public string Prefix { get; private set; }
        public bool Resolved { get; private set; }
        public string Message { get; private set; } = "";

        // Number of distinct Logic App sites (flow<hostId> groups) found in this storage account.
        public int SiteCount { get; private set; }

        public SiteScope(StorageContext storage)
        {
            _storage = storage;
            SiteName = Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME") ?? "";
        }

        public async Task EnsureAsync(CancellationToken ct)
        {
            if (Resolved) return;
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (Resolved) return;

                var tableNames = new List<string>();
                await foreach (var t in _storage.Tables.QueryAsync(cancellationToken: ct).ConfigureAwait(false))
                {
                    tableNames.Add(t.Name);
                }

                SiteCount = tableNames
                    .Select(ExtractSiteGroup)
                    .Where(g => g != null)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();

                var over = Environment.GetEnvironmentVariable("LASI_TABLE_PREFIX");
                if (!string.IsNullOrWhiteSpace(over))
                {
                    if (tableNames.Any(name => name.StartsWith(over, StringComparison.OrdinalIgnoreCase)))
                    { Set(over, "Resolved from LASI_TABLE_PREFIX override."); return; }
                    throw new InvalidOperationException($"LASI_TABLE_PREFIX '{over}' was not found among tables in this storage account.");
                }

                var derived = GetRuntimeStoragePrefix();
                if (tableNames.Any(name => name.StartsWith(derived, StringComparison.OrdinalIgnoreCase)))
                { Set(derived, "Resolved from the Logic Apps runtime host ID."); return; }

                if (!tableNames.Any(name => name.StartsWith("flow", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("No Logic Apps tables (flow<hostId>...) found in this storage account.");

                throw new InvalidOperationException(
                    $"The Logic Apps storage prefix for '{SiteName}' could not be resolved. " +
                    "Set LASI_TABLE_PREFIX to the correct flow<hostId> prefix.");
            }
            catch (Exception ex)
            {
                Resolved = false;
                Message = ex.Message;
                throw;
            }
            finally { _gate.Release(); }
        }

        private void Set(string prefix, string msg) { Prefix = prefix; Resolved = true; Message = msg; }

        // Site group = flow + first 15 hex chars of the host id embedded in the table name.
        private static string ExtractSiteGroup(string tableName)
        {
            if (tableName == null || !tableName.StartsWith("flow", StringComparison.OrdinalIgnoreCase)) return null;
            var rest = tableName.Substring(4);
            var hex = new string(rest.TakeWhile(Uri.IsHexDigit).ToArray());
            if (hex.Length < 15) return null;
            return "flow" + hex.Substring(0, 15);
        }

        private string GetRuntimeStoragePrefix()
        {
            var hostId = Environment.GetEnvironmentVariable("Microsoft.Azure.Workflows.HostId");
            if (string.IsNullOrWhiteSpace(hostId)) hostId = SiteName?.ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(hostId))
                throw new InvalidOperationException("Neither Microsoft.Azure.Workflows.HostId nor WEBSITE_SITE_NAME is configured.");

            var hash = MurmurHash64(Encoding.UTF8.GetBytes(hostId)).ToString("X", CultureInfo.InvariantCulture);
            return "flow" + hash.Substring(0, Math.Min(hash.Length, 15)).ToLowerInvariant();
        }

        // Must remain byte-for-byte compatible with ResourceStack ComputeHash.MurmurHash64 (seed 0).
        private static ulong MurmurHash64(byte[] data)
        {
            const uint c1 = 0x239b961b;
            const uint c2 = 0xab0e9789;
            const uint c3 = 0x561ccd1b;
            const uint c4 = 0x0bcaa747;
            const uint c5 = 0x85ebca6b;
            const uint c6 = 0xc2b2ae35;

            unchecked
            {
                uint h1 = 0, h2 = 0;
                var index = 0;
                while (index + 7 < data.Length)
                {
                    var k1 = (uint)(data[index] | data[index + 1] << 8 | data[index + 2] << 16 | data[index + 3] << 24);
                    var k2 = (uint)(data[index + 4] | data[index + 5] << 8 | data[index + 6] << 16 | data[index + 7] << 24);
                    k1 *= c1; k1 = RotateLeft(k1, 15); k1 *= c2; h1 ^= k1;
                    h1 = RotateLeft(h1, 19); h1 += h2; h1 = (h1 * 5) + c3;
                    k2 *= c2; k2 = RotateLeft(k2, 17); k2 *= c1; h2 ^= k2;
                    h2 = RotateLeft(h2, 13); h2 += h1; h2 = (h2 * 5) + c4;
                    index += 8;
                }

                var tail = data.Length - index;
                if (tail > 0)
                {
                    var k1 = tail >= 4 ? (uint)(data[index] | data[index + 1] << 8 | data[index + 2] << 16 | data[index + 3] << 24)
                        : tail == 3 ? (uint)(data[index] | data[index + 1] << 8 | data[index + 2] << 16)
                        : tail == 2 ? (uint)(data[index] | data[index + 1] << 8)
                        : data[index];
                    k1 *= c1; k1 = RotateLeft(k1, 15); k1 *= c2; h1 ^= k1;

                    if (tail > 4)
                    {
                        var k2 = tail == 7 ? (uint)(data[index + 4] | data[index + 5] << 8 | data[index + 6] << 16)
                            : tail == 6 ? (uint)(data[index + 4] | data[index + 5] << 8)
                            : data[index + 4];
                        k2 *= c2; k2 = RotateLeft(k2, 17); k2 *= c1; h2 ^= k2;
                    }
                }

                h1 ^= (uint)data.Length; h2 ^= (uint)data.Length;
                h1 += h2; h2 += h1;
                h1 ^= h1 >> 16; h1 *= c5; h1 ^= h1 >> 13; h1 *= c6; h1 ^= h1 >> 16;
                h2 ^= h2 >> 16; h2 *= c5; h2 ^= h2 >> 13; h2 *= c6; h2 ^= h2 >> 16;
                h1 += h2; h2 += h1;
                return ((ulong)h2 << 32) | h1;
            }
        }

        private static uint RotateLeft(uint value, int count) => (value << count) | (value >> (32 - count));
    }
}