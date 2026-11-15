import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { Route, Routes } from "react-router";
import { Home } from "./pages";
import "./App.css";
import { AppHeader } from "./components";
import { useDevices, useSignalR } from "./hooks";

const queryClient = new QueryClient();

function AppContent() {
  const { data: devices, isLoading, error } = useDevices();
  const { weatherData, isConnected } = useSignalR();
  const device = devices?.[0];
  // Use real-time weatherData from SignalR if available, otherwise fall back to initial device data
  const initialData = device?.latestData;
  const deviceData =
    weatherData &&
    (!initialData ||
      Date.parse(weatherData.date) >= Date.parse(initialData.date))
      ? weatherData
      : initialData;
  const lastUpdated = deviceData ? new Date(deviceData.date) : null;

  return (
    <div>
      <AppHeader
        stationName={device?.name || "Weather station"}
        location={device?.location || ""}
        lastUpdated={lastUpdated}
      />
      <p role="status" className="px-4 py-1 text-xs text-slate-500">
        {isConnected ? "Live updates connected" : "Live updates reconnecting"}
      </p>
      <div className="flex h-screen overflow-hidden">
        <Routes>
          <Route
            path="/"
            element={
              <Home data={deviceData} isLoading={isLoading} error={error} />
            }
          />
        </Routes>
      </div>
    </div>
  );
}

function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <AppContent />
    </QueryClientProvider>
  );
}

export default App;
