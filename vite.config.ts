import { defineConfig } from "vitest/config";
import tailwindcss from "@tailwindcss/vite";
import react from "@vitejs/plugin-react";

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    proxy: {
      "/api": {
        target: process.env.WEATHER_API_URL || "http://localhost:5001",
        changeOrigin: true,
        ws: true,
        rewrite: (path) => path.replace(/^\/api/, ""),
      },
    },
  },
  build: {
    rolldownOptions: {
      onLog(level, log, handler) {
        const isSignalRInvalidAnnotationWarning =
          level === "warn" &&
          log.code === "INVALID_ANNOTATION" &&
          typeof log.id === "string" &&
          log.id.includes("@microsoft/signalr/dist/esm/Utils.js");

        if (isSignalRInvalidAnnotationWarning) {
          return;
        }

        handler(level, log);
      },
    },
  },
  test: {
    environment: "jsdom",
    globals: true,
  },
});
