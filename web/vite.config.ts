import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// Build into the backend wwwroot; served by ASP.NET Core. Relative base for site-extension subpaths.
export default defineConfig({
  base: "./",
  plugins: [react()],
  build: { outDir: "../wwwroot", emptyOutDir: true },
  server: { proxy: { "/api": "http://localhost:5080" } },
});