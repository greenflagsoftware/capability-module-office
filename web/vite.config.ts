import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      "/search": "http://localhost:8083",
      "/view": "http://localhost:8083",
      "/upload": "http://localhost:8083",
      "/edit": "http://localhost:8083",
      "/delete": "http://localhost:8083",
      "/health": "http://localhost:8083",
    },
  },
  build: {
    outDir: "dist",
    emptyOutDir: true,
  },
});
