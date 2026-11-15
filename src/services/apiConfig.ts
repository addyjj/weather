export const API_BASE_URL = (
  import.meta.env.VITE_API_BASE_URL || "/api"
).replace(/\/$/, "");
export const WEATHER_HUB_URL = `${API_BASE_URL}/weatherHub`;
