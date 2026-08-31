import { useEffect, useRef, useState } from "react";
import {
  Dropdown, Option, Input, Button, Spinner, Text, Checkbox,
  Table, TableHeader, TableRow, TableHeaderCell, TableBody, TableCell,
  Dialog, DialogSurface, DialogBody, DialogTitle, DialogContent, DialogActions,
  makeStyles, tokens, Tooltip,
} from "@fluentui/react-components";
import { CopyRegular, ArrowDownloadRegular, SearchRegular, DismissRegular } from "@fluentui/react-icons";
import { api, FlowInfo, HistoryRow, download, toCsv } from "../api";

const useStyles = makeStyles({
  filters: { display: "flex", flexWrap: "wrap", gap: "12px", alignItems: "flex-end", marginBottom: "12px" },
  field: { display: "flex", flexDirection: "column", gap: "4px" },
  actions: { display: "flex", gap: "8px", marginBottom: "8px", alignItems: "center", flexWrap: "wrap" },
  content: { fontFamily: "Consolas, monospace", fontSize: "12px", whiteSpace: "pre-wrap", maxHeight: "120px", overflow: "auto", display: "block", cursor: "pointer" },
  mark: { backgroundColor: "#ffd400", color: "#000", borderRadius: "2px" },
  dialogJson: { fontFamily: "Consolas, monospace", fontSize: "12px", whiteSpace: "pre-wrap", maxHeight: "60vh", overflow: "auto", margin: 0 },
  date: { background: tokens.colorNeutralBackground1, color: tokens.colorNeutralForeground1, border: "1px solid " + tokens.colorNeutralStroke1, borderRadius: "4px", padding: "5px", colorScheme: "dark" },
});

const ALL = "__all__";
type SortKey = "flowName" | "runId" | "actionName" | "timestamp" | "direction";

function prettyJson(text: string): string {
  try { return JSON.stringify(JSON.parse(text), null, 2); }
  catch { return text; }
}

export function HistorySearch() {
  const s = useStyles();
  const [flows, setFlows] = useState<FlowInfo[]>([]);
  const [selFlows, setSelFlows] = useState<string[]>([ALL]);
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [query, setQuery] = useState("");
  const [rows, setRows] = useState<HistoryRow[]>([]);
  const [loading, setLoading] = useState(false);
  const [err, setErr] = useState("");
  const [page, setPage] = useState(1);
  const [hasMore, setHasMore] = useState(false);
  const [scanned, setScanned] = useState(0);
  const [sortKey, setSortKey] = useState<SortKey>("timestamp");
  const [sortAsc, setSortAsc] = useState(false);
  const [selected, setSelected] = useState<Set<number>>(new Set());
  const [detail, setDetail] = useState<HistoryRow | null>(null);
  const abortRef = useRef<AbortController | null>(null);

  useEffect(() => { api.flows().then(setFlows).catch(e => setErr(String(e.message || e))); }, []);

  const allFlows = selFlows.includes(ALL);

  async function runSearch(nextPage = 1) {
    abortRef.current?.abort();
    const ac = new AbortController();
    abortRef.current = ac;
    setLoading(true); setErr(""); setSelected(new Set());
    try {
      const req = {
        flows: allFlows ? [] : selFlows,
        allFlows,
        from: from ? new Date(from).toISOString() : null,
        to: to ? new Date(to).toISOString() : null,
        query,
        page: nextPage,
        pageSize: 50,
      };
      const res = await api.search(req, ac.signal);
      setRows(res.items); setPage(res.page); setHasMore(res.hasMore); setScanned(res.scanned);
    } catch (e: any) {
      if (e.name !== "AbortError") setErr(String(e.message || e));
    } finally {
      if (abortRef.current === ac) setLoading(false);
    }
  }

  function cancel() { abortRef.current?.abort(); setLoading(false); }

  const sorted = [...rows].sort((a, b) => {
    const av = (a[sortKey] || "").toString(); const bv = (b[sortKey] || "").toString();
    return sortAsc ? av.localeCompare(bv) : bv.localeCompare(av);
  });

  function toggleSort(k: SortKey) { if (k === sortKey) setSortAsc(!sortAsc); else { setSortKey(k); setSortAsc(true); } }
  function toggleRow(i: number) { const n = new Set(selected); n.has(i) ? n.delete(i) : n.add(i); setSelected(n); }

  function highlight(text: string) {
    if (!query) return text;
    const lower = text.toLowerCase();
    const term = query.toLowerCase();
    const parts: (string | JSX.Element)[] = [];
    let i = 0, key = 0;
    while (i < text.length) {
      const at = lower.indexOf(term, i);
      if (at < 0) { parts.push(text.slice(i)); break; }
      if (at > i) parts.push(text.slice(i, at));
      parts.push(<mark key={key++} className={s.mark}>{text.slice(at, at + term.length)}</mark>);
      i = at + term.length;
    }
    return parts;
  }
  function rowText(r: HistoryRow) { return `${r.flowName} | ${r.runId} | ${r.actionName} | ${r.timestamp} | ${r.direction}\n${r.content}`; }
  function copyRow(r: HistoryRow) { navigator.clipboard.writeText(rowText(r)); }
  function copySelected() { navigator.clipboard.writeText(sorted.filter((_, i) => selected.has(i)).map(rowText).join("\n\n")); }
  function exportRows(which: "all" | "selected", fmt: "csv" | "json") {
    const data = which === "all" ? sorted : sorted.filter((_, i) => selected.has(i));
    if (fmt === "csv") download("history.csv", toCsv(data), "text/csv");
    else download("history.json", JSON.stringify(data, null, 2), "application/json");
  }

  return (
    <div>
      <div className={s.filters}>
        <div className={s.field}>
          <Text size={200}>Flows</Text>
          <Dropdown multiselect style={{ minWidth: "260px" }} selectedOptions={selFlows}
            value={allFlows ? "All Flows" : selFlows.join(", ")}
            onOptionSelect={(_, d) => {
              let v = d.selectedOptions as string[];
              if (d.optionValue === ALL) v = v.includes(ALL) ? [ALL] : [];
              else v = v.filter(x => x !== ALL);
              setSelFlows(v.length ? v : [ALL]);
            }}>
            <Option value={ALL}>All Flows</Option>
            {flows.map(f => <Option key={f.name} value={f.name}>{f.name}</Option>)}
          </Dropdown>
        </div>
        <div className={s.field}><Text size={200}>From</Text><input type="date" className={s.date} value={from} onChange={e => setFrom(e.target.value)} /></div>
        <div className={s.field}><Text size={200}>To</Text><input type="date" className={s.date} value={to} onChange={e => setTo(e.target.value)} /></div>
        <div className={s.field} style={{ flex: 1, minWidth: "220px" }}>
          <Text size={200}>Action content contains</Text>
          <Input value={query} placeholder="e.g. search21" onChange={(_, d) => setQuery(d.value)}
            onKeyDown={e => { if (e.key === "Enter") runSearch(1); }} />
        </div>
        <Button appearance="primary" icon={<SearchRegular />} disabled={loading} onClick={() => runSearch(1)}>Search</Button>
        {loading && <Button icon={<DismissRegular />} onClick={cancel}>Cancel</Button>}
        {loading && <Spinner size="tiny" />}
      </div>

      {err && <Text style={{ color: tokens.colorPaletteRedForeground1 }}>{err}</Text>}

      <div className={s.actions}>
        <Text size={200}>{rows.length} shown &nbsp;|&nbsp; {scanned} scanned &nbsp;|&nbsp; {selected.size} selected</Text>
        <div style={{ flex: 1 }} />
        <Button size="small" icon={<CopyRegular />} disabled={!selected.size} onClick={copySelected}>Copy selected</Button>
        <Button size="small" icon={<ArrowDownloadRegular />} onClick={() => exportRows("all", "csv")}>CSV (all)</Button>
        <Button size="small" icon={<ArrowDownloadRegular />} onClick={() => exportRows("all", "json")}>JSON (all)</Button>
        <Button size="small" icon={<ArrowDownloadRegular />} disabled={!selected.size} onClick={() => exportRows("selected", "csv")}>CSV (sel)</Button>
      </div>

      <Table size="small">
        <TableHeader>
          <TableRow>
            <TableHeaderCell style={{ width: "36px" }} />
            <TableHeaderCell onClick={() => toggleSort("flowName")}>Flow</TableHeaderCell>
            <TableHeaderCell onClick={() => toggleSort("runId")}>Run ID</TableHeaderCell>
            <TableHeaderCell onClick={() => toggleSort("actionName")}>Action</TableHeaderCell>
            <TableHeaderCell onClick={() => toggleSort("timestamp")}>Timestamp</TableHeaderCell>
            <TableHeaderCell onClick={() => toggleSort("direction")}>Dir</TableHeaderCell>
            <TableHeaderCell>Content</TableHeaderCell>
            <TableHeaderCell style={{ width: "44px" }} />
          </TableRow>
        </TableHeader>
        <TableBody>
          {sorted.map((r, i) => (
            <TableRow key={i}>
              <TableCell><Checkbox checked={selected.has(i)} onChange={() => toggleRow(i)} /></TableCell>
              <TableCell>{r.flowName}</TableCell>
              <TableCell>{r.runId}</TableCell>
              <TableCell>{r.actionName}</TableCell>
              <TableCell>{r.timestamp}</TableCell>
              <TableCell>{r.direction}</TableCell>
              <TableCell><code className={s.content} onClick={() => setDetail(r)} title="Click to view formatted JSON">{highlight(r.content)}</code></TableCell>
              <TableCell><Tooltip content="Copy" relationship="label"><Button size="small" appearance="subtle" icon={<CopyRegular />} onClick={() => copyRow(r)} /></Tooltip></TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>

      <div className={s.actions} style={{ marginTop: "10px" }}>
        <Button size="small" disabled={page <= 1 || loading} onClick={() => runSearch(page - 1)}>Previous</Button>
        <Text size={200}>Page {page}</Text>
        <Button size="small" disabled={!hasMore || loading} onClick={() => runSearch(page + 1)}>Next</Button>
      </div>

      <Dialog open={!!detail} onOpenChange={(_, d) => { if (!d.open) setDetail(null); }}>
        <DialogSurface style={{ maxWidth: "80vw" }}>
          <DialogBody>
            <DialogTitle>
              {detail ? `${detail.flowName} / ${detail.actionName} (${detail.direction})` : ""}
            </DialogTitle>
            <DialogContent>
              <pre className={s.dialogJson}>{detail ? prettyJson(detail.content) : ""}</pre>
            </DialogContent>
            <DialogActions>
              <Button size="small" icon={<CopyRegular />} onClick={() => detail && navigator.clipboard.writeText(prettyJson(detail.content))}>Copy</Button>
              <Button size="small" appearance="primary" onClick={() => setDetail(null)}>Close</Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>
    </div>
  );
}