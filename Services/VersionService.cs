using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.Data.Tables;

namespace LogicAppStorageInspector.Services
{
    // Reads each per-workflow <sitePrefix><flowHash>flows table.
    public sealed class VersionService
    {
        private readonly StorageContext _storage;
        private readonly SiteScope _scope;

        public VersionService(StorageContext storage, SiteScope scope) { _storage = storage; _scope = scope; }

        public async Task<List<FlowVersionNode>> GetTreeAsync(CancellationToken ct)
        {
            await _scope.EnsureAsync(ct).ConfigureAwait(false);
            // Real identity is the FlowId column; the table-name hash segment is not the flow id.
            var byId = new Dictionary<string, FlowAccumulator>(StringComparer.OrdinalIgnoreCase);

            await foreach (var tableName in ListFlowsTablesAsync(ct).ConfigureAwait(false))
            {
                var tc = _storage.Tables.GetTableClient(tableName);
                await foreach (var e in tc.QueryAsync<TableEntity>(cancellationToken: ct).ConfigureAwait(false))
                {
                    ct.ThrowIfCancellationRequested();
                    var flowId = e.GetString("FlowId");
                    if (string.IsNullOrEmpty(flowId)) continue;
                    var version = FirstNonEmpty(e, "FlowSequenceId", "FlowVersion", "Version");
                    if (string.IsNullOrEmpty(version)) continue;

                    var flow = e.GetString("FlowName") ?? "(unknown)";
                    var created = FirstTime(e, "CreatedTime", "ChangedTime");
                    var author = FirstNonEmpty(e, "CreatedBy", "Author", "ChangedBy") ?? "";

                    if (!byId.TryGetValue(flowId, out var acc)) { acc = new FlowAccumulator(flow); byId[flowId] = acc; }
                    if (acc.FlowName == "(unknown)" && flow != "(unknown)") acc.FlowName = flow;
                    if (!acc.Versions.ContainsKey(version)) acc.Versions[version] = new VersionInfo(version, created, author);
                }
            }

            return byId
                .OrderBy(kv => kv.Value.FlowName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kv => new FlowVersionNode(
                    kv.Value.FlowName,
                    kv.Key,
                    kv.Value.Versions.Values
                        .OrderByDescending(v => v.VersionId, StringComparer.Ordinal).ToArray()))
                .ToList();
        }

        private sealed class FlowAccumulator
        {
            public string FlowName;
            public readonly Dictionary<string, VersionInfo> Versions = new(StringComparer.OrdinalIgnoreCase);
            public FlowAccumulator(string flowName) { FlowName = flowName; }
        }

        public async Task<VersionContent> GetContentAsync(string flowName, string versionId, string flowId, CancellationToken ct)
        {
            await _scope.EnsureAsync(ct).ConfigureAwait(false);
            var filter = string.IsNullOrEmpty(flowId)
                ? $"FlowSequenceId eq '{Esc(versionId)}' and FlowName eq '{Esc(flowName)}'"
                : $"FlowSequenceId eq '{Esc(versionId)}' and FlowId eq '{Esc(flowId)}'";

            TableEntity fallback = null;
            await foreach (var tableName in ListFlowsTablesAsync(ct).ConfigureAwait(false))
            {
                var tc = _storage.Tables.GetTableClient(tableName);
                await foreach (var e in tc.QueryAsync<TableEntity>(filter: filter, cancellationToken: ct).ConfigureAwait(false))
                {
                    if (e.ContainsKey("DefinitionCompressed") && e["DefinitionCompressed"] is byte[] def && def.Length > 0)
                    {
                        var decoded = ScanEngine.DecodeField(def, _storage.Blobs);
                        if (!string.IsNullOrEmpty(decoded))
                            return new VersionContent(flowName, versionId, ScanEngine.Pretty(decoded));
                    }
                    fallback ??= e;
                }
            }
            return fallback != null
                ? new VersionContent(flowName, versionId, ScanEngine.Pretty(ExtractDefinition(fallback)))
                : new VersionContent(flowName, versionId, "(version not found)");
        }

        private async IAsyncEnumerable<string> ListFlowsTablesAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (var table in _storage.Tables.QueryAsync(cancellationToken: ct).ConfigureAwait(false))
            {
                if (table.Name.StartsWith(_scope.Prefix, StringComparison.OrdinalIgnoreCase) &&
                    table.Name.EndsWith("flows", StringComparison.OrdinalIgnoreCase))
                    yield return table.Name;
            }
        }

        private static string Esc(string s) => (s ?? "").Replace("'", "''");

        public async Task<DiffResponse> DiffAsync(string flowName, string leftId, string rightId, string flowId, CancellationToken ct)
        {
            var left = await GetContentAsync(flowName, leftId, flowId, ct).ConfigureAwait(false);
            var right = await GetContentAsync(flowName, rightId, flowId, ct).ConfigureAwait(false);
            return new DiffResponse(flowName, leftId, rightId, left.Content, right.Content);
        }

        private string ExtractDefinition(TableEntity e)
        {
            if (e.ContainsKey("DefinitionCompressed") && e["DefinitionCompressed"] is byte[] def && def.Length > 0)
            {
                var d = ScanEngine.DecodeField(def, _storage.Blobs);
                if (!string.IsNullOrEmpty(d)) return d;
            }
            foreach (var key in e.Keys)
            {
                if (key.EndsWith("Compressed", StringComparison.OrdinalIgnoreCase) && e[key] is byte[] bytes)
                {
                    var decoded = ScanEngine.DecodeField(bytes, _storage.Blobs);
                    if (!string.IsNullOrEmpty(decoded)) return decoded;
                }
            }
            var sb = new System.Text.StringBuilder();
            foreach (var kv in e)
            {
                if (kv.Value is byte[]) continue;
                sb.AppendLine($"{kv.Key}: {kv.Value}");
            }
            return sb.ToString();
        }

        private static string FirstNonEmpty(TableEntity e, params string[] keys)
        {
            foreach (var k in keys) { var v = e.GetString(k); if (!string.IsNullOrEmpty(v)) return v; }
            return null;
        }

        private static string FirstTime(TableEntity e, params string[] keys)
        {
            foreach (var k in keys) { var v = ScanEngine.AsText(e, k); if (!string.IsNullOrEmpty(v)) return v; }
            return "";
        }
    }
}