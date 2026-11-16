import { defineConfig, devices } from "@playwright/test";

export default defineConfig({
  testDir: "./tests",
  globalSetup: "./global-setup.ts",
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: 0,
  reporter: [["html"], ["list"]],
  use: {
    baseURL: process.env.E2E_BASE_URL ?? "http://localhost:5173",
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
    video: "retain-on-failure",
    actionTimeout: 10000,
    navigationTimeout: 30000,
  },

  projects: [
    {
      name: "api",
      testDir: "./tests/api",
      use: {
        baseURL: process.env.API_BASE_URL ?? "http://localhost:5000",
        trace: "retain-on-failure",
      },
    },
    {
      name: "ui",
      testDir: "./tests/ui",
      use: devices["Desktop Chrome"],
    },
  ],

  webServer: undefined, // Hệ thống đã được khởi động bằng docker compose trước khi chạy test
});
