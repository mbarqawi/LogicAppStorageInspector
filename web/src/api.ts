export interface FlowInfo { name: string; }
export interface HistoryRow { flowName: string; runId: string; actionName: string; timestamp: string; content: string; direction: string; }
export interface HistorySearchResponse { items: HistoryRow[]; page: number; pageSize: number; hasMore: boolean; scanned: number; matches: number; }
export interface VersionInfo { versionId: string; createdTime: string; author: string; }
export interface FlowVersionNode { flowName: string; flowId: string; versions: VersionInfo[]; }
export interface VersionContent { flowName: string; versionId: string; content: string; }
export interface DiffResponse { flowName: string; leftVersionId: string; rightVersionId: string; leftContent: string; rightContent: string; }
export interface TableStat { name: string; rowCount: string; lastModified: string; }
export interface QueueStat { name: string; messageCount: number; status: string; }
export interface SiteInfo { siteName: string; tablePrefix: string; resolved: boolean; message: string; siteCount: number; }

const base = "api";

async function get<T>(path: string, signal?: AbortSignal): Promise<T> {
  const r = await fetch(base + "/" + path, { signal });
  if (!r.ok) throw new Error(await r.text());
  return r.json() as Promise<T>;
}
async function post<T>(path: string, body: unknown, signal?: AbortSignal): Promise<T> {
  const r = await fetch(base + "/" + path, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body), signal });
  if (!r.ok) throw new Error(await r.text());
  return r.json() as Promise<T>;
}

export const api = {
  site: (s?: AbortSignal) => get<SiteInfo>("site", s),
  flows: (s?: AbortSignal) => get<FlowInfo[]>("flows", s),
  search: (req: unknown, s?: AbortSignal) => post<HistorySearchResponse>("history/search", req, s),
  versions: (s?: AbortSignal) => get<FlowVersionNode[]>("versions", s),
  versionContent: (flow: string, version: string, flowId: string, s?: AbortSignal) =>
    get<VersionContent>("versions/content?flow=" + encodeURIComponent(flow) + "&version=" + encodeURIComponent(version) + "&flowId=" + encodeURIComponent(flowId), s),
  diff: (flow: string, left: string, right: string, flowId: string, s?: AbortSignal) =>
    get<DiffResponse>("versions/diff?flow=" + encodeURIComponent(flow) + "&left=" + encodeURIComponent(left) + "&right=" + encodeURIComponent(right) + "&flowId=" + encodeURIComponent(flowId), s),
  tables: (s?: AbortSignal) => get<TableStat[]>("dashboard/tables", s),
  queues: (s?: AbortSignal) => get<QueueStat[]>("dashboard/queues", s),
};

export function download(name: string, text: string, mime: string) {
  const blob = new Blob([text], { type: mime });
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url; a.download = name; a.click();
  URL.revokeObjectURL(url);
}

export function toCsv(rows: HistoryRow[]): string {
  const esc = (v: string) => '"' + (v ?? "").replace(/"/g, '""') + '"';
  const head = ["FlowName", "RunId", "ActionName", "Timestamp", "Direction", "Content"].join(",");
  const body = rows.map(r => [r.flowName, r.runId, r.actionName, r.timestamp, r.direction, r.content].map(esc).join(","));
  return [head, ...body].join("\r\n");
}