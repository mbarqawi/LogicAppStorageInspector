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
        private const string VersionRowMarker = "_FLOWVERSION-";

        private readonly StorageContext _storage;
        private readonly SiteScope _scope;

        public VersionService(StorageContext storage, SiteScope scope) { _storage = storage; _scope = scope; }

        public async Task<List<FlowVersionNode>> GetTreeAsync(CancellationToken ct)
        {
            await _scope.EnsureAsync(ct).ConfigureAwait(false);
            var rows = new List<TableEntity>();
            await foreach (var tableName in ListFlowsTablesAsync(ct).ConfigureAwait(false))
            {
                var tc = _storage.Tables.GetTableClient(tableName);
                await foreach (var e in tc.QueryAsync<TableEntity>(cancellationToken: ct).ConfigureAwait(false))
                {
                    ct.ThrowIfCancellationRequested();
                    rows.Add(e);
                }
            }

            var nodes = new List<FlowVersionNode>();
            foreach (var grp in rows
                .Where(e => !string.IsNullOrEmpty(e.GetString("FlowId")))
                .GroupBy(e => e.GetString("FlowId"), StringComparer.OrdinalIgnoreCase))
            {
                var flowId = grp.Key;
                var name = grp.Select(e => e.GetString("FlowName")).FirstOrDefault(n => !string.IsNullOrEmpty(n)) ?? "(unknown)";

                // Created date is the flow's creation, taken from its lookup/identifier row.
                var flowRow = PickFlowRow(grp);
                var created = flowRow != null ? FirstTime(flowRow, "CreatedTime", "ChangedTime") : "";
                var author = flowRow != null ? (FirstNonEmpty(flowRow, "CreatedBy", "Author", "ChangedBy") ?? "") : "";

                var seqs = grp.Where(IsVersionRow)
                    .Select(e => FirstNonEmpty(e, "FlowSequenceId", "FlowVersion", "Version"))
                    .Where(v => !string.IsNullOrEmpty(v))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // Draft-only flows have no version row; surface a single entry so they still appear.
                if (seqs.Count == 0)
                {
                    var s = grp.Select(e => FirstNonEmpty(e, "FlowSequenceId", "FlowVersion", "Version"))
                        .FirstOrDefault(v => !string.IsNullOrEmpty(v));
                    if (!string.IsNullOrEmpty(s)) seqs.Add(s);
                }

                var versions = seqs
                    .Select(sq => new VersionInfo(sq, created, author))
                    .OrderBy(v => v.VersionId, StringComparer.Ordinal)
                    .ToArray();

                nodes.Add(new FlowVersionNode(name, flowId, versions));
            }

            return nodes
                .OrderBy(n => n.FlowName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(n => n.FlowId, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool IsVersionRow(TableEntity e) =>
            (e.RowKey ?? "").IndexOf(VersionRowMarker, StringComparison.OrdinalIgnoreCase) >= 0;

        private static TableEntity PickFlowRow(IEnumerable<TableEntity> grp)
        {
            var list = grp as ICollection<TableEntity> ?? grp.ToList();
            TableEntity ByMarker(string marker) =>
                list.FirstOrDefault(e => (e.RowKey ?? "").IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0);

            return ByMarker("_FLOWLOOKUP-")
                ?? ByMarker("_FLOWDRAFTLOOKUP-")
                ?? ByMarker("_FLOWIDENTIFIER-")
                ?? list.OrderBy(e => FirstTime(e, "CreatedTime", "ChangedTime"), StringComparer.Ordinal).FirstOrDefault();
        }

        public async Task<VersionContent> GetContentAsync(string flowName, string versionId, string flowId, CancellationToken ct)
        {
            await _scope.EnsureAsync(ct).ConfigureAwait(false);
            var filter = string.IsNullOrEmpty(flowId)
                ? $"FlowSequenceId eq '{Esc(versionId)}' and FlowName eq '{Esc(flowName)}'"
                : $"FlowSequenceId eq '{Esc(versionId)}' and FlowId eq '{Esc(flowId)}'";

            string fallbackContent = null;
            TableEntity fallbackEntity = null;
            await foreach (var tableName in ListFlowsTablesAsync(ct).ConfigureAwait(false))
            {
                var tc = _storage.Tables.GetTableClient(tableName);
                await foreach (var e in tc.QueryAsync<TableEntity>(filter: filter, cancellationToken: ct).ConfigureAwait(false))
                {
                    if (e.ContainsKey("DefinitionCompressed") && e["DefinitionCompressed"] is byte[] def && def.Length > 0)
                    {
                        var decoded = ScanEngine.DecodeField(def, _storage.Blobs);
                        if (!string.IsNullOrEmpty(decoded))
                        {
                            if (IsVersionRow(e)) return new VersionContent(flowName, versionId, ScanEngine.Pretty(decoded));
                            fallbackContent ??= decoded;
                        }
                    }
                    fallbackEntity ??= e;
                }
            }
            if (fallbackContent != null) return new VersionContent(flowName, versionId, ScanEngine.Pretty(fallbackContent));
            return fallbackEntity != null
                ? new VersionContent(flowName, versionId, ScanEngine.Pretty(ExtractDefinition(fallbackEntity)))
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