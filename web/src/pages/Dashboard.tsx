import { useCallback, useEffect, useRef, useState } from "react";
import {
  Button, Spinner, Text, Switch, Badge,
  Table, TableHeader, TableRow, TableHeaderCell, TableBody, TableCell,
  makeStyles, tokens,
} from "@fluentui/react-components";
import { ArrowClockwiseRegular } from "@fluentui/react-icons";
import { api, TableStat, QueueStat } from "../api";

const useStyles = makeStyles({
  bar: { display: "flex", gap: "12px", alignItems: "center", marginBottom: "12px" },
  cols: { display: "flex", gap: "20px", alignItems: "flex-start", flexWrap: "wrap" },
  col: { flex: 1, minWidth: "360px" },
  h: { marginBottom: "8px" },
});

function statusColor(s: string): "success" | "warning" | "danger" | "informative" {
  if (s === "Healthy") return "success";
  if (s === "Empty") return "informative";
  if (s === "Backlogged") return "warning";
  return "danger";
}

export function Dashboard() {
  const s = useStyles();
  const [tables, setTables] = useState<TableStat[]>([]);
  const [queues, setQueues] = useState<QueueStat[]>([]);
  const [loading, setLoading] = useState(false);
  const [err, setErr] = useState("");
  const [last, setLast] = useState("");
  const [auto, setAuto] = useState(false);
  const timer = useRef<number | null>(null);

  const refresh = useCallback(async () => {
    setLoading(true); setErr("");
    try {
      const [t, q] = await Promise.all([api.tables(), api.queues()]);
      setTables(t); setQueues(q); setLast(new Date().toLocaleTimeString());
    } catch (e: any) { setErr(String(e.message || e)); }
    finally { setLoading(false); }
  }, []);

  useEffect(() => { refresh(); }, [refresh]);
  useEffect(() => {
    if (auto) { timer.current = window.setInterval(refresh, 15000); }
    return () => { if (timer.current) window.clearInterval(timer.current); };
  }, [auto, refresh]);

  return (
    <div>
      <div className={s.bar}>
        <Button appearance="primary" icon={<ArrowClockwiseRegular />} onClick={refresh} disabled={loading}>Refresh</Button>
        <Switch label="Auto refresh (15s)" checked={auto} onChange={(_, d) => setAuto(d.checked)} />
        {loading && <Spinner size="tiny" />}
        <div style={{ flex: 1 }} />
        {last && <Text size={200} style={{ opacity: 0.7 }}>Last refresh: {last}</Text>}
      </div>
      {err && <Text style={{ color: tokens.colorPaletteRedForeground1 }}>{err}</Text>}

      <div className={s.cols}>
        <div className={s.col}>
          <Text weight="semibold" className={s.h}>Tables ({tables.length})</Text>
          <Table size="small">
            <TableHeader><TableRow><TableHeaderCell>Name</TableHeaderCell><TableHeaderCell>Rows</TableHeaderCell><TableHeaderCell>Last modified</TableHeaderCell></TableRow></TableHeader>
            <TableBody>
              {tables.map(t => <TableRow key={t.name}><TableCell>{t.name}</TableCell><TableCell>{t.rowCount}</TableCell><TableCell>{t.lastModified}</TableCell></TableRow>)}
            </TableBody>
          </Table>
        </div>

        <div className={s.col}>
          <Text weight="semibold" className={s.h}>Queues ({queues.length})</Text>
          <Table size="small">
            <TableHeader><TableRow><TableHeaderCell>Name</TableHeaderCell><TableHeaderCell>Messages</TableHeaderCell><TableHeaderCell>Status</TableHeaderCell></TableRow></TableHeader>
            <TableBody>
              {queues.map(q => <TableRow key={q.name}><TableCell>{q.name}</TableCell><TableCell>{q.messageCount}</TableCell><TableCell><Badge appearance="filled" color={statusColor(q.status)}>{q.status}</Badge></TableCell></TableRow>)}
            </TableBody>
          </Table>
        </div>
      </div>
    </div>
  );
}