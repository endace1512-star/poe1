import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// `npm run dev` serves the UI on :5173 and forwards /api to the server on :8787.
// `npm run build` writes straight into the server's wwwroot, which is embedded into poe1.exe.
export default defineConfig({
  plugins: [react()],
  build: {
    outDir: "../src/Poe1.Server/wwwroot",
    emptyOutDir: true,
    chunkSizeWarningLimit: 1500,
  },
  server: {
    proxy: {
      "/api": { target: "http://127.0.0.1:8787", changeOrigin: false },
    },
  },
});
