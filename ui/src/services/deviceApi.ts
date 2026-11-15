import type { Device } from "../types/device";
import { API_BASE_URL } from "./apiConfig";

export const fetchDevices = async (): Promise<Device[]> => {
  const response = await fetch(`${API_BASE_URL}/devices`);
  if (!response.ok) throw new Error("Failed to fetch devices");
  return response.json();
};
