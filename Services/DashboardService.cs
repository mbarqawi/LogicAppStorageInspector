using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.Data.Tables;

namespace LogicAppStorageInspector.Services
{
    public sealed class DashboardService
    {
        private const int CountCap = 2000;

        private readonly StorageContext _storage;
        private readonly SiteScope _scope;

        public DashboardService(StorageContext storage, SiteScope scope) { _storage = storage; _scope = scope; }

        public async Task<List<TableStat>> GetTablesAsync(CancellationToken ct)
        {
            await _scope.EnsureAsync(ct).ConfigureAwait(false);
            var prefix = _scope.Prefix;
            var stats = new List<TableStat>();

            await foreach (var t in _storage.Tables.QueryAsync(cancellationToken: ct).ConfigureAwait(false))
            {
                if (!t.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                var tc = _storage.Tables.GetTableClient(t.Name);
                int n = 0; DateTimeOffset? last = null;
                await foreach (var e in tc.QueryAsync<TableEntity>(select: new[] { "Timestamp" }, cancellationToken: ct).ConfigureAwait(false))
                {
                    n++;
                    if (e.Timestamp.HasValue && (last == null || e.Timestamp > last)) last = e.Timestamp;
                    if (n >= CountCap) break;
                }
                stats.Add(new TableStat(t.Name, n >= CountCap ? CountCap + "+" : n.ToString(), last?.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss") ?? ""));
            }
            return stats.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public async Task<List<QueueStat>> GetQueuesAsync(CancellationToken ct)
        {
            await _scope.EnsureAsync(ct).ConfigureAwait(false);
            var prefix = _scope.Prefix;
            var stats = new List<QueueStat>();

            await foreach (var q in _storage.Queues.GetQueuesAsync(cancellationToken: ct).ConfigureAwait(false))
            {
                if (!q.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                long count = 0; string status = "Unknown";
                try
                {
                    var qc = _storage.Queues.GetQueueClient(q.Name);
                    var props = await qc.GetPropertiesAsync(ct).ConfigureAwait(false);
                    count = props.Value.ApproximateMessagesCount;
                    status = count == 0 ? "Empty" : count > 1000 ? "Backlogged" : "Healthy";
                }
                catch (Exception ex) { status = "Error: " + ex.Message; }
                stats.Add(new QueueStat(q.Name, count, status));
            }
            return stats.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}