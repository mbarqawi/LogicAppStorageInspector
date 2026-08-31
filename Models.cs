using System;

namespace LogicAppStorageInspector
{
    public record FlowInfo(string Name);

    public record HistoryRow(string FlowName, string RunId, string ActionName, string Timestamp, string Content, string Direction);

    public record HistorySearchRequest(string[] Flows, bool AllFlows, DateTimeOffset? From, DateTimeOffset? To, string Query, int Page, int PageSize);

    public record HistorySearchResponse(HistoryRow[] Items, int Page, int PageSize, bool HasMore, int Scanned, int Matches);

    public record VersionInfo(string VersionId, string CreatedTime, string Author);

    public record FlowVersionNode(string FlowName, string FlowId, VersionInfo[] Versions);

    public record VersionContent(string FlowName, string VersionId, string Content);

    public record DiffResponse(string FlowName, string LeftVersionId, string RightVersionId, string LeftContent, string RightContent);

    public record TableStat(string Name, string RowCount, string LastModified);

    public record QueueStat(string Name, long MessageCount, string Status);

    public record SiteInfo(string SiteName, string TablePrefix, bool Resolved, string Message, int SiteCount);
}