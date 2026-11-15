import { test, expect } from "playwright/test";

test("loads weather through the real API and receives live SignalR updates", async ({
  page,
  request,
}) => {
  const errors: string[] = [];
  page.on("pageerror", (error) => errors.push(error.message));
  const rest = page.waitForResponse(
    (r) => r.url().endsWith("/api/devices") && r.ok(),
  );
  let resolveOldFrame!: () => void;
  const oldFrame = new Promise<void>((resolve) => {
    resolveOldFrame = resolve;
  });
  page.on("websocket", (socket) =>
    socket.on("framereceived", ({ payload }) => {
      if (String(payload).includes('"tempOut":20')) resolveOldFrame();
    }),
  );
  await page.goto("/");
  await rest;
  await expect(
    page.getByRole("heading", { name: "Backyard station" }),
  ).toBeVisible();
  await expect(page.getByText("Chicago, IL")).toBeVisible();
  await expect(
    page.getByRole("heading", { name: "68°", exact: true }),
  ).toBeVisible();
  await expect(page.getByText(/Humidity\s*55%/)).toBeVisible();
  await expect(
    page.getByText(/Precipitation \(24h\)\s*0\.2 inches/),
  ).toBeVisible();
  await expect(
    page.getByText("Live updates connected", { exact: true }),
  ).toBeVisible();
  await expect
    .poll(
      async () =>
        (await (await request.get("http://127.0.0.1:5129/state")).json())
          .subscribers,
    )
    .toBeGreaterThan(0);
  await request.post("http://127.0.0.1:5129/push", {
    data: {
      date: "2026-11-14T12:05:00Z",
      tempf: 74,
      humidity: 61,
      windspeedmph: 6,
    },
  });
  await expect(
    page.getByRole("heading", { name: "74°", exact: true }),
  ).toBeVisible();
  await expect(page.getByText(/Humidity\s*61%/)).toBeVisible();
  await expect(
    page.getByRole("heading", { name: "6", exact: true }),
  ).toBeVisible();
  // Older upstream events must not replace newer readings.
  await request.post("http://127.0.0.1:5129/push", {
    data: { date: "2026-11-14T11:00:00Z", tempf: 20 },
  });
  await oldFrame;
  await expect(
    page.getByRole("heading", { name: "74°", exact: true }),
  ).toBeVisible();
  const previous = (
    await (await request.get("http://127.0.0.1:5129/state")).json()
  ).subscribers;
  await request.post("http://127.0.0.1:5129/disconnect");
  await expect
    .poll(
      async () =>
        (await (await request.get("http://127.0.0.1:5129/state")).json())
          .subscribers,
      { timeout: 20000 },
    )
    .toBeGreaterThan(previous);
  await request.post("http://127.0.0.1:5129/push", {
    data: { date: "2026-11-14T12:10:00Z", tempf: 76 },
  });
  await expect(
    page.getByRole("heading", { name: "76°", exact: true }),
  ).toBeVisible();
  expect(errors).toEqual([]);
  await page.screenshot({
    path: "test-results/weather-dashboard.png",
    fullPage: true,
  });
});

test("shows loading and an empty state without fabricated readings", async ({
  page,
}) => {
  let release!: () => void;
  const ready = new Promise<void>((resolve) => {
    release = resolve;
  });
  await page.route("**/api/devices", async (route) => {
    await ready;
    await route.fulfill({ json: [] });
  });
  await page.goto("/");
  await expect(
    page.getByText("Loading weather data...", { exact: true }),
  ).toBeVisible();
  await expect(page.getByText("Awaiting weather data")).toBeVisible();
  await expect(
    page.getByRole("heading", { name: "0°", exact: true }),
  ).toHaveCount(0);
  release();
  await expect(page.getByText("No weather stations found.")).toBeVisible();
});

test("shows an API error instead of zero weather measurements", async ({
  page,
}) => {
  await page.route("**/api/devices", (route) =>
    route.fulfill({
      status: 502,
      json: { title: "Weather service unavailable" },
    }),
  );
  await page.goto("/");
  await expect(page.getByRole("alert")).toContainText(
    "Failed to fetch devices",
    { timeout: 15000 },
  );
  await expect(
    page.getByRole("heading", { name: "0°", exact: true }),
  ).toHaveCount(0);
});
