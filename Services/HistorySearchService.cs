using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Azure.Data.Tables;

namespace LogicAppStorageInspector.Services
{
    public sealed class HistorySearchService
    {
        private static readonly Regex DatedActions = new(@"(\d{8})t000000zactions$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly string[] Cols = { "ActionName", "TriggerName", "HistoryName", "Status", "FlowName", "FlowRunSequenceId", "CreatedTime", "InputsLinkCompressed", "OutputsLinkCompressed" };

        private readonly StorageContext _storage;
        private readonly SiteScope _scope;

        public HistorySearchService(StorageContext storage, SiteScope scope) { _storage = storage; _scope = scope; }

        public async Task<List<FlowInfo>> ListFlowsAsync(CancellationToken ct)
        {
            await _scope.EnsureAsync(ct).ConfigureAwait(false);
            var flows = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            await foreach (var tbl in ListContentTablesAsync(null, null, ct).ConfigureAwait(false))
            {
                var tc = _storage.Tables.GetTableClient(tbl);
                await foreach (var e in tc.QueryAsync<TableEntity>(select: new[] { "FlowName" }, cancellationToken: ct).ConfigureAwait(false))
                {
                    var f = e.GetString("FlowName");
                    if (!string.IsNullOrEmpty(f)) flows.Add(f);
                }
            }
            return flows.Select(f => new FlowInfo(f)).ToList();
        }

        public async Task<HistorySearchResponse> SearchAsync(HistorySearchRequest req, CancellationToken ct)
        {
            await _scope.EnsureAsync(ct).ConfigureAwait(false);

            var flowSet = req.AllFlows ? null : new HashSet<string>(req.Flows ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var query = req.Query ?? "";
            int page = Math.Max(1, req.Page);
            int pageSize = req.PageSize <= 0 ? 50 : Math.Min(req.PageSize, 500);
            int skip = (page - 1) * pageSize;
            int need = skip + pageSize + 1;

            var results = new List<HistoryRow>();
            int scanned = 0;
            var filter = BuildTimeFilter(req.From, req.To);

            await foreach (var tbl in ListContentTablesAsync(req.From, req.To, ct).ConfigureAwait(false))
            {
                if (results.Count >= need) break;
                var isTrigger = tbl.EndsWith("histories", StringComparison.OrdinalIgnoreCase);
                var tc = _storage.Tables.GetTableClient(tbl);
                await foreach (var e in tc.QueryAsync<TableEntity>(filter: filter, select: Cols, cancellationToken: ct).ConfigureAwait(false))
                {
                    ct.ThrowIfCancellationRequested();
                    var flow = e.GetString("FlowName") ?? "";
                    if (flowSet != null && !flowSet.Contains(flow)) continue;

                    scanned++;
                    var created = ScanEngine.AsText(e, "CreatedTime");
                    var runId = e.GetString("FlowRunSequenceId") ?? "";
                    var name = e.GetString("ActionName");
                    if (string.IsNullOrEmpty(name)) name = e.GetString("TriggerName");
                    if (string.IsNullOrEmpty(name)) name = e.GetString("HistoryName");
                    name ??= "";
                    var inKind = isTrigger ? "trigger-input" : "input";
                    var outKind = isTrigger ? "trigger-output" : "output";

                    var inputs = ScanEngine.DecodeField(e.ContainsKey("InputsLinkCompressed") ? e.GetBinary("InputsLinkCompressed") : null, _storage.Blobs);
                    var outputs = ScanEngine.DecodeField(e.ContainsKey("OutputsLinkCompressed") ? e.GetBinary("OutputsLinkCompressed") : null, _storage.Blobs);

                    if (string.IsNullOrEmpty(query))
                    {
                        if (!string.IsNullOrEmpty(inputs)) results.Add(new HistoryRow(flow, runId, name, created, inputs, inKind));
                        else if (!string.IsNullOrEmpty(outputs)) results.Add(new HistoryRow(flow, runId, name, created, outputs, outKind));
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(inputs) && inputs.Contains(query, StringComparison.OrdinalIgnoreCase))
                            results.Add(new HistoryRow(flow, runId, name, created, inputs, inKind));
                        if (!string.IsNullOrEmpty(outputs) && outputs.Contains(query, StringComparison.OrdinalIgnoreCase))
                            results.Add(new HistoryRow(flow, runId, name, created, outputs, outKind));
                    }
                    if (results.Count >= need) break;
                }
            }

            bool hasMore = results.Count > skip + pageSize;
            var pageItems = results.Skip(skip).Take(pageSize).ToArray();
            return new HistorySearchResponse(pageItems, page, pageSize, hasMore, scanned, results.Count);
        }

        // Action tables are dated and filtered by day; history (trigger) tables are undated.
        private async IAsyncEnumerable<string> ListContentTablesAsync(DateTimeOffset? from, DateTimeOffset? to, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            var prefix = _scope.Prefix;
            await foreach (var t in _storage.Tables.QueryAsync(cancellationToken: ct).ConfigureAwait(false))
            {
                var name = t.Name;
                if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

                if (name.EndsWith("histories", StringComparison.OrdinalIgnoreCase))
                {
                    yield return name;
                    continue;
                }
                if (!name.EndsWith("actions", StringComparison.OrdinalIgnoreCase)) continue;

                var m = DatedActions.Match(name);
                if (m.Success && (from.HasValue || to.HasValue))
                {
                    if (DateTime.TryParseExact(m.Groups[1].Value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var day))
                    {
                        if (from.HasValue && day.Date < from.Value.UtcDateTime.Date) continue;
                        if (to.HasValue && day.Date > to.Value.UtcDateTime.Date) continue;
                    }
                }
                yield return name;
            }
        }

        private static string BuildTimeFilter(DateTimeOffset? from, DateTimeOffset? to)
        {
            var parts = new List<string>();
            if (from.HasValue) parts.Add($"CreatedTime ge datetime'{from.Value.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}'");
            if (to.HasValue) parts.Add($"CreatedTime le datetime'{to.Value.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}'");
            return parts.Count == 0 ? null : string.Join(" and ", parts);
        }
    }
}