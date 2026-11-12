import { test, expect } from '@playwright/test'

// auth E2E — যা শুধু আসল ব্রাউজারেই প্রমাণ হয়: CORS, httpOnly refresh cookie,
// page-reload-এ silent refresh, react-router redirect। integration test এগুলো ছুঁতে পারে না।

const password = 'Str0ng!Passw0rd'

test('register → login → silent refresh survives reload → logout', async ({ page }) => {
  // প্রতি run-এ ইউনিক email — dev DB শেয়ার্ড, আর register rate-limit (5/min) বাঁচাতে এক টেস্টে পুরো journey
  const email = `e2e-${Date.now()}@test.local`

  // --- register (UI দিয়ে — form validation + API wiring দুটোই টেস্ট হয়) ---
  await page.goto('/register')
  await page.locator('#fullName').fill('E2E User')
  await page.locator('#reg-email').fill(email)
  await page.locator('#reg-password').fill(password)
  await page.getByRole('button', { name: 'অ্যাকাউন্ট তৈরি করুন' }).click()

  // সফল হলে toast + login form-এ switch
  await expect(page.getByText('অ্যাকাউন্ট তৈরি হয়েছে')).toBeVisible()
  await expect(page.locator('#email')).toBeVisible()

  // --- login ---
  await page.locator('#email').fill(email)
  await page.locator('#password').fill(password)
  await page.getByRole('button', { name: 'লগইন', exact: true }).click()

  // home-এ পৌঁছে shell sidebar-এ user-এর নাম — মানে access token কাজ করছে (/me সফল)।
  // sidebar (<aside> = complementary role)-scoped: "E2E User" heading-এও আছে ("স্বাগতম, ..."),
  // plain getByText দুটোতে মিলে strict mode violation দেয়।
  await page.waitForURL('/')
  await expect(page.getByRole('complementary').getByText('E2E User')).toBeVisible()

  // --- silent refresh: পুরো E2E-র সবচেয়ে দামি assert ---
  // access token শুধু মেমোরিতে (Redux) — reload-এ হারায়। টিকে থাকার একমাত্র পথ:
  // httpOnly refresh cookie → AppBootstrap-এর /auth/refresh → নতুন token। এই chain-এর
  // যেকোনো অংশ ভাঙলে (cookie attribute, CORS credentials, bootstrap লজিক) ইউজার ছিটকে যাবে।
  await page.reload()
  await expect(page.getByRole('complementary').getByText('E2E User')).toBeVisible()
  await expect(page).toHaveURL('/')

  // --- logout ---
  await page.getByRole('button', { name: /লগআউট|Log out/i }).click()
  await page.waitForURL('/login')
  await expect(page.locator('#email')).toBeVisible()
})

test('remember me controls refresh cookie persistence', async ({ page, context }) => {
  const email = `e2e-rm-${Date.now()}@test.local`

  // register (UI)
  await page.goto('/register')
  await page.locator('#fullName').fill('Remember User')
  await page.locator('#reg-email').fill(email)
  await page.locator('#reg-password').fill(password)
  await page.getByRole('button', { name: 'অ্যাকাউন্ট তৈরি করুন' }).click()
  await expect(page.locator('#email')).toBeVisible()

  const refreshCookie = async () =>
    (await context.cookies('http://localhost:5076/api/v1/auth')).find((c) => c.name === 'refreshToken')

  // --- login WITHOUT remember me → session cookie (expires নেই = -1) ---
  await page.locator('#email').fill(email)
  await page.locator('#password').fill(password)
  await page.getByRole('button', { name: 'লগইন', exact: true }).click()
  await page.waitForURL('/')

  let cookie = await refreshCookie()
  expect(cookie).toBeDefined()
  expect(cookie!.expires).toBe(-1) // session cookie — browser বন্ধে মুছে যায়

  // --- logout, login WITH remember me → persistent cookie (ভবিষ্যৎ expiry) ---
  await page.getByRole('button', { name: /লগআউট|Log out/i }).click()
  await page.waitForURL('/login')

  await page.locator('#email').fill(email)
  await page.locator('#password').fill(password)
  await page.getByText('মনে রাখো').click()
  await page.getByRole('button', { name: 'লগইন', exact: true }).click()
  await page.waitForURL('/')

  cookie = await refreshCookie()
  expect(cookie).toBeDefined()
  expect(cookie!.expires).toBeGreaterThan(Date.now() / 1000) // future epoch = persistent
})

test('forgot password page shows generic success', async ({ page }) => {
  await page.goto('/login')
  await page.getByRole('button', { name: 'পাসওয়ার্ড ভুলে গেছেন?' }).click()
  await page.waitForURL('/forgot-password')

  await page.locator('#forgot-email').fill('whoever@test.local')
  await page.getByRole('button', { name: 'Reset link পাঠান' }).click()

  // account থাকুক না থাকুক একই বার্তা (enumeration রোধ) — server-ও generic 200 দেয়
  await expect(page.getByText('reset link পাঠানো হয়েছে')).toBeVisible()
})

test('reset password page rejects broken link and bad token', async ({ page }) => {
  // token/email ছাড়া এলে ভাঙা-link বার্তা
  await page.goto('/reset-password')
  await expect(page.getByText('Reset link অসম্পূর্ণ বা ভাঙা')).toBeVisible()

  // ভুল token-এ generic error toast, page-এই থাকে
  await page.goto('/reset-password?email=whoever%40test.local&token=WRONG')
  await page.locator('#new-password').fill('N3w!Passw0rd')
  await page.locator('#confirm-password').fill('N3w!Passw0rd')
  await page.getByRole('button', { name: 'পাসওয়ার্ড বদলান' }).click()
  await expect(page.getByText('Reset করা যায়নি')).toBeVisible()
})

test('protected route redirects anonymous user to /login', async ({ page }) => {
  await page.goto('/chat')
  await page.waitForURL('/login')
  await expect(page.locator('#email')).toBeVisible()
})

test('wrong credentials shows error and stays on login', async ({ page }) => {
  await page.goto('/login')
  await page.locator('#email').fill('nobody@test.local')
  await page.locator('#password').fill('Wr0ng!Passw0rd')
  await page.getByRole('button', { name: 'লগইন', exact: true }).click()

  await expect(page.getByText('ইমেইল বা পাসওয়ার্ড ভুল')).toBeVisible()
  await expect(page).toHaveURL(/\/login/)
})
