import { useEffect, useMemo, useRef, useState } from "react";
import {
  Tree, TreeItem, TreeItemLayout, Button, Spinner, Text, Radio, RadioGroup,
  Tooltip, MessageBar, MessageBarBody, makeStyles, tokens,
} from "@fluentui/react-components";
import {
  CopyRegular, ArrowDownloadRegular, TextAlignLeftRegular, TextAlignRightRegular,
  FolderZipRegular, WarningRegular,
} from "@fluentui/react-icons";
import Editor, { DiffEditor } from "@monaco-editor/react";
import { api, FlowVersionNode, download } from "../api";
import { buildZip, downloadBlob, safeName } from "../zip";

const useStyles = makeStyles({
  wrap: { display: "flex", gap: "0", height: "calc(100vh - 130px)" },
  tree: { overflow: "auto", paddingRight: "8px", flexShrink: 0 },
  resizer: {
    width: "6px", cursor: "col-resize", flexShrink: 0,
    borderLeft: "1px solid " + tokens.colorNeutralStroke2,
    ":hover": { backgroundColor: tokens.colorNeutralBackground3 },
  },
  right: { flex: 1, display: "flex", flexDirection: "column", gap: "8px", minWidth: 0, paddingLeft: "12px" },
  bar: { display: "flex", gap: "8px", alignItems: "center", flexWrap: "wrap" },
  editor: { flex: 1, border: "1px solid " + tokens.colorNeutralStroke2, minHeight: 0 },
  ver: { display: "flex", gap: "6px", alignItems: "center" },
});

interface Sel { flow: string; version: string; flowId: string; }

export function FlowVersions() {
  const s = useStyles();
  const [tree, setTree] = useState<FlowVersionNode[]>([]);
  const [loading, setLoading] = useState(false);
  const [err, setErr] = useState("");
  const [current, setCurrent] = useState<{ sel: Sel; content: string } | null>(null);
  const [left, setLeft] = useState<Sel | null>(null);
  const [right, setRight] = useState<Sel | null>(null);
  const [diff, setDiff] = useState<{ left: string; right: string } | null>(null);
  const [mode, setMode] = useState<"inline" | "side">("side");
  const [treeWidth, setTreeWidth] = useState(320);
  const wrapRef = useRef<HTMLDivElement>(null);

  function startResize(e: React.MouseEvent) {
    e.preventDefault();
    const onMove = (ev: MouseEvent) => {
      const left = wrapRef.current?.getBoundingClientRect().left ?? 0;
      const w = ev.clientX - left;
      setTreeWidth(Math.min(Math.max(w, 200), 700));
    };
    const onUp = () => {
      window.removeEventListener("mousemove", onMove);
      window.removeEventListener("mouseup", onUp);
    };
    window.addEventListener("mousemove", onMove);
    window.addEventListener("mouseup", onUp);
  }

  useEffect(() => { setLoading(true); api.versions().then(setTree).catch(e => setErr(String(e.message || e))).finally(() => setLoading(false)); }, []);

  async function open(flow: string, version: string, flowId: string) {
    setDiff(null);
    const c = await api.versionContent(flow, version, flowId);
    setCurrent({ sel: { flow, version, flowId }, content: c.content });
  }
  async function compare() {
    if (!left || !right || left.flow !== right.flow || left.flowId !== right.flowId) { setErr("Select two versions from the same flow."); return; }
    setErr("");
    const d = await api.diff(left.flow, left.version, right.version, left.flowId);
    setDiff({ left: d.leftContent, right: d.rightContent });
    setCurrent(null);
  }
  function copyCurrent() { if (current) navigator.clipboard.writeText(current.content); }
  function exportCurrent() { if (current) download(`${current.sel.flow}-${current.sel.version}.json`, current.content, "application/json"); }
  function copyDiff() { if (diff) navigator.clipboard.writeText("--- LEFT ---\n" + diff.left + "\n\n--- RIGHT ---\n" + diff.right); }
  function exportDiff() { if (diff) download("diff.txt", "--- LEFT ---\n" + diff.left + "\n\n--- RIGHT ---\n" + diff.right, "text/plain"); }

  async function downloadZip(flow: string, version: string, flowId: string) {
    const c = await api.versionContent(flow, version, flowId);
    const folder = safeName(flow);
    const zip = buildZip([{ name: `${folder}/workflow.json`, data: new TextEncoder().encode(c.content) }]);
    downloadBlob(`${folder}.zip`, zip);
  }

  const groups = useMemo(() => {
    const m = new Map<string, FlowVersionNode[]>();
    for (const n of tree) {
      const list = m.get(n.flowName) ?? [];
      list.push(n);
      m.set(n.flowName, list);
    }
    return m;
  }, [tree]);
  const duplicateNames = [...groups].filter(([, v]) => v.length > 1).map(([k]) => k);

  function renderVersions(node: FlowVersionNode) {
    return node.versions.map(v => (
      <TreeItem key={node.flowId + "|" + v.versionId} itemType="leaf">
        <TreeItemLayout>
          <div className={s.ver}>
            <Tooltip content="Download as ZIP" relationship="label">
              <Button size="small" icon={<FolderZipRegular />} appearance="subtle" onClick={() => downloadZip(node.flowName, v.versionId, node.flowId)} />
            </Tooltip>
            <Button size="small" appearance="subtle" onClick={() => open(node.flowName, v.versionId, node.flowId)}>
              {v.versionId}
            </Button>
            <Text size={100} style={{ opacity: 0.6 }}>{v.createdTime}{v.author ? " - " + v.author : ""}</Text>
            <Tooltip content="Set as Left" relationship="label">
              <Button size="small" icon={<TextAlignLeftRegular />} appearance={left && left.flowId === node.flowId && left.version === v.versionId ? "primary" : "subtle"} onClick={() => setLeft({ flow: node.flowName, version: v.versionId, flowId: node.flowId })} />
            </Tooltip>
            <Tooltip content="Set as Right" relationship="label">
              <Button size="small" icon={<TextAlignRightRegular />} appearance={right && right.flowId === node.flowId && right.version === v.versionId ? "primary" : "subtle"} onClick={() => setRight({ flow: node.flowName, version: v.versionId, flowId: node.flowId })} />
            </Tooltip>
          </div>
        </TreeItemLayout>
      </TreeItem>
    ));
  }

  return (
    <div className={s.wrap} ref={wrapRef}>
      <div className={s.tree} style={{ width: treeWidth }}>
        {loading && <Spinner size="tiny" label="Loading flows" />}
        {err && <Text style={{ color: tokens.colorPaletteRedForeground1 }}>{err}</Text>}
        {duplicateNames.length > 0 && (
          <MessageBar intent="warning" style={{ marginBottom: "8px" }}>
            <MessageBarBody>
              Duplicate flow name{duplicateNames.length > 1 ? "s" : ""} detected: {duplicateNames.join(", ")}.
              Each distinct flow id is listed separately below.
            </MessageBarBody>
          </MessageBar>
        )}
        <Tree aria-label="Flows">
          {[...groups.entries()].map(([flowName, nodes]) =>
            nodes.length === 1 ? (
              <TreeItem key={flowName} itemType="branch">
                <TreeItemLayout>{flowName}</TreeItemLayout>
                <Tree>{renderVersions(nodes[0])}</Tree>
              </TreeItem>
            ) : (
              <TreeItem key={flowName} itemType="branch">
                <TreeItemLayout
                  iconAfter={<WarningRegular style={{ color: tokens.colorStatusWarningForeground1 }} />}>
                  {flowName} ({nodes.length} flows share this name)
                </TreeItemLayout>
                <Tree>
                  {nodes.map(n => (
                    <TreeItem key={n.flowId} itemType="branch">
                      <TreeItemLayout>flow id: {n.flowId ? n.flowId.substring(0, 8) : "(default)"}</TreeItemLayout>
                      <Tree>{renderVersions(n)}</Tree>
                    </TreeItem>
                  ))}
                </Tree>
              </TreeItem>
            )
          )}
        </Tree>
      </div>

      <div className={s.resizer} onMouseDown={startResize} role="separator" aria-orientation="vertical" />

      <div className={s.right}>
        <div className={s.bar}>
          <Button appearance="primary" disabled={!left || !right} onClick={compare}>Compare L vs R</Button>
          {diff && (
            <RadioGroup layout="horizontal" value={mode} onChange={(_, d) => setMode(d.value as any)}>
              <Radio value="side" label="Side-by-side" />
              <Radio value="inline" label="Inline" />
            </RadioGroup>
          )}
          <div style={{ flex: 1 }} />
          {current && <><Button size="small" icon={<CopyRegular />} onClick={copyCurrent}>Copy</Button><Button size="small" icon={<ArrowDownloadRegular />} onClick={exportCurrent}>Export</Button></>}
          {diff && <><Button size="small" icon={<CopyRegular />} onClick={copyDiff}>Copy diff</Button><Button size="small" icon={<ArrowDownloadRegular />} onClick={exportDiff}>Export diff</Button></>}
        </div>

        <div className={s.editor}>
          {current && (
            <Editor height="100%" theme="vs-dark" language="json" value={current.content}
              options={{ readOnly: true, minimap: { enabled: false }, wordWrap: "on" }} />
          )}
          {diff && (
            <DiffEditor height="100%" theme="vs-dark" language="json"
              original={diff.left} modified={diff.right}
              options={{ readOnly: true, renderSideBySide: mode === "side", minimap: { enabled: false }, wordWrap: "on" }} />
          )}
          {!current && !diff && <div style={{ padding: "20px", opacity: 0.6 }}>Select a version to view, or set L and R and click Compare.</div>}
        </div>
      </div>
    </div>
  );
}