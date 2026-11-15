import { useEffect, useState } from "react";
import * as signalR from "@microsoft/signalr";
import type { LatestData } from "../types/device";
import { WEATHER_HUB_URL } from "../services/apiConfig";

export function useSignalR(hubUrl = WEATHER_HUB_URL) {
  const [weatherData, setWeatherData] = useState<LatestData | null>(null);
  const [isConnected, setIsConnected] = useState(false);
  const [error, setError] = useState<Error | null>(null);

  useEffect(() => {
    let disposed = false;
    let retry: ReturnType<typeof setTimeout> | undefined;
    const connection = new signalR.HubConnectionBuilder()
      .withUrl(hubUrl, { withCredentials: false })
      .withAutomaticReconnect([0, 1000, 3000, 5000, 10000])
      .configureLogging(signalR.LogLevel.None)
      .build();

    connection.on("weatherData", (data: LatestData) => {
      if (!disposed)
        setWeatherData((previous) =>
          !previous || Date.parse(data.date) >= Date.parse(previous.date)
            ? data
            : previous,
        );
    });
    connection.onreconnecting(() => {
      if (!disposed) setIsConnected(false);
    });
    connection.onreconnected(() => {
      if (!disposed) {
        setIsConnected(true);
        setError(null);
      }
    });

    async function start() {
      try {
        await connection.start();
        if (disposed) {
          await connection.stop();
          return;
        }
        setIsConnected(true);
        setError(null);
      } catch (err) {
        if (!disposed) {
          setIsConnected(false);
          setError(err instanceof Error ? err : new Error(String(err)));
          retry = setTimeout(start, 5000);
        }
      }
    }
    connection.onclose(() => {
      if (!disposed) {
        setIsConnected(false);
        retry = setTimeout(start, 5000);
      }
    });
    void start();
    return () => {
      disposed = true;
      clearTimeout(retry);
      // Stop connecting as well as connected sockets (including StrictMode cleanup).
      void connection.stop();
    };
  }, [hubUrl]);

  return { weatherData, isConnected, error };
}
