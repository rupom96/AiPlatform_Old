import { defineConfig, devices } from '@playwright/test'

// E2E — আসল ব্রাউজার + আসল frontend + আসল backend + আসল DB। কোনো mock নেই।
// চালাতে: `npm run e2e` (backing services docker compose দিয়ে নিজেই ওঠে — global-setup দেখো)।
// দুটো সার্ভারই Playwright নিজে তোলে; আগে থেকে চললে সেটাই reuse করে।
export default defineConfig({
  testDir: './e2e',
  timeout: 30_000,
  // E2E-তে টেস্টগুলো একই DB/rate-limit শেয়ার করে — serial চালাই, flake কমে
  fullyParallel: false,
  workers: 1,
  reporter: 'list',
  use: {
    baseURL: 'http://localhost:5173',
    trace: 'retain-on-failure', // ফেল করলে Playwright trace — কী ঘটেছিল ধাপে ধাপে দেখা যায়
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],

  webServer: [
    {
      // আগে backing services (compose, --wait = healthy পর্যন্ত), তারপর API।
      // ⚠️ globalSetup-এ compose-up রাখা যায় না — Playwright webServer আগে চালায়, globalSetup পরে;
      // তখন API খালি DB-হীন অবস্থায় উঠে startup migration-এ মরে। তাই এক command-এ চেইন।
      command: 'docker compose -f ../docker-compose.yml up -d --wait && dotnet run --project ../backend/src/Api',
      url: 'http://localhost:5076/health',
      reuseExistingServer: true,
      timeout: 180_000,   // প্রথমবার: image pull + sqlserver healthcheck + dotnet build
    },
    {
      // frontend dev server
      command: 'npm run dev',
      url: 'http://localhost:5173',
      reuseExistingServer: true,
      timeout: 60_000,
    },
  ],
})
