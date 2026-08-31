import { useEffect, useState } from "react";
import { TabList, Tab, Text, MessageBar, MessageBarBody, makeStyles, tokens } from "@fluentui/react-components";
import { HistoryRegular, BranchRegular, DataBarVerticalAscendingRegular } from "@fluentui/react-icons";
import { api, SiteInfo } from "./api";
import { HistorySearch } from "./pages/HistorySearch";
import { FlowVersions } from "./pages/FlowVersions";
import { Dashboard } from "./pages/Dashboard";

const useStyles = makeStyles({
  root: { display: "flex", flexDirection: "column", height: "100vh" },
  header: { padding: "12px 20px", borderBottom: "1px solid " + tokens.colorNeutralStroke2, display: "flex", alignItems: "center", gap: "16px" },
  headerSide: { display: "flex", alignItems: "center", gap: "16px", flex: 1, minWidth: 0 },
  headerSideRight: { justifyContent: "flex-end" },
  body: { flex: 1, overflow: "auto", padding: "16px 20px" },
});

export function App() {
  const s = useStyles();
  const [tab, setTab] = useState("history");
  const [site, setSite] = useState<SiteInfo | null>(null);

  useEffect(() => { api.site().then(setSite).catch(() => {}); }, []);

  return (
    <div className={s.root}>
      <div className={s.header}>
        <div className={s.headerSide}>
          <Text weight="semibold" size={500}>Logic App Storage Inspector</Text>
        </div>
        <TabList selectedValue={tab} onTabSelect={(_, d) => setTab(d.value as string)}>
          <Tab value="history" icon={<HistoryRegular />}>Flow History Search</Tab>
          <Tab value="versions" icon={<BranchRegular />}>Flow Versions</Tab>
          <Tab value="dashboard" icon={<DataBarVerticalAscendingRegular />}>Information Dashboard</Tab>
        </TabList>
        <div className={`${s.headerSide} ${s.headerSideRight}`}>
          {site && (
            <Text size={200} style={{ opacity: 0.7 }}>
              site: {site.siteName || "(unknown)"} &nbsp;|&nbsp; prefix: {site.tablePrefix || "(unresolved)"}
            </Text>
          )}
        </div>
      </div>
      {site && !site.resolved && (
        <MessageBar intent="warning">
          <MessageBarBody>Site scope not resolved: {site.message}</MessageBarBody>
        </MessageBar>
      )}
      {site && site.resolved && site.siteCount > 1 && (
        <MessageBar intent="warning">
          <MessageBarBody>
            This storage account hosts {site.siteCount} Logic App sites. Only the current site
            ({site.siteName || "unknown"}, prefix {site.tablePrefix}) is shown.
          </MessageBarBody>
        </MessageBar>
      )}
      <div className={s.body}>
        {tab === "history" && <HistorySearch />}
        {tab === "versions" && <FlowVersions />}
        {tab === "dashboard" && <Dashboard />}
      </div>
    </div>
  );
}