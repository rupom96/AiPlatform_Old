# Auth Guide — setup ও টেস্ট (বাংলা)

> Remember Me, Forgot/Reset Password, Google Login — কীভাবে setup করবে, কীভাবে টেস্ট করবে।
> Auth-এর ভেতরের কারিগরি (JWT, refresh rotation, reuse-detection): `architecture.md` + Phase 1 (TODO.md)।

---

## সূচি
1. [Remember Me — কীভাবে কাজ করে ও টেস্ট](#১-remember-me)
2. [Forgot / Reset Password — flow ও টেস্ট](#২-forgot--reset-password)
3. [আসল Email পাঠানো (SMTP setup)](#৩-আসল-email-পাঠানো-smtp-setup)
4. [Google Login — setup ও টেস্ট](#৪-google-login)
5. [সাধারণ ফাঁদ](#৫-সাধারণ-ফাঁদ)

---

## ১. Remember Me

### কীভাবে কাজ করে
Login-এর `rememberMe` → Redis-এর refresh-token family-তে জমা থাকে (token rotate হলেও পছন্দ টেকে) → refresh cookie:

| চেকবক্স | Cookie | ফলাফল |
|---|---|---|
| ☐ টিক নেই | **session** cookie (Expires নেই) | browser পুরো বন্ধ করলে logout |
| ☑ টিক আছে | **persistent** cookie (Expires = ৭ দিন) | browser বন্ধ-খোলাতেও logged-in |

### টেস্ট — step by step
1. Login করো **টিক না দিয়ে**
2. Browser **সম্পূর্ণ বন্ধ** (সব window; tray-তে থাকলে quit)
3. খুলে `http://localhost:5173` → **login page-এ পাঠাবে** ✓
4. এবার login **টিক দিয়ে** → আবার browser সম্পূর্ণ বন্ধ → খোলো → **logged-in থাকবে** ✓

চোখে দেখা: `F12 → Application → Cookies → http://localhost:5076` → `refreshToken` — Expires column-এ **Session** vs **তারিখ**।

> ⚠️ **F5/reload দুই ক্ষেত্রেই logged-in রাখে** — ওটা silent refresh (in-memory token হারালে httpOnly cookie দিয়ে নতুন নেয়)। Remember me-র টেস্ট শুধুই browser-বন্ধ।

---

## ২. Forgot / Reset Password

### Flow
1. `/forgot-password` → email দাও → server **সবসময় একই generic উত্তর** (account আছে কিনা বলে না — enumeration রোধ)
2. Account থাকলে email-এ reset link যায়: `http://localhost:5173/reset-password?email=..&token=..`
3. Link খুলে নতুন password (৮+, বড়+ছোট হাত, সংখ্যা, চিহ্ন) → login page → নতুন password-এ login

### টেস্ট
- SMTP configure **না থাকলে** (default dev): email যায় না, link **API-র console/log-এ** ছাপে:
  ```
  EMAIL (dev, not sent) → you@example.com | Reset your password
  Click the link to reset your password:
  http://localhost:5173/reset-password?email=...&token=...
  ```
  ওই লাইনটা কপি করে browser-এ খোলো।
- SMTP configure **থাকলে** (§৩): সত্যিকার inbox-এ যাবে (spam folder-ও দেখো)।
- Token **এক-বার-ই** চলে; ব্যবহারের পর বা নতুন forgot-request-এর পর পুরনোটা invalid।

---

## ৩. আসল Email পাঠানো (SMTP setup)

Default-এ email **log-এ** যায় (`LoggingEmailSender`)। `Smtp:Host` সেট করলেই আসল email (`SmtpEmailSender`, MailKit) — কোড বদলাতে হয় না, DI config দেখে বেছে নেয়।

### Gmail দিয়ে (সবচেয়ে সহজ)
1. Gmail account-এ **2-Step Verification চালু** থাকতে হবে
2. [myaccount.google.com/apppasswords](https://myaccount.google.com/apppasswords) → App password বানাও (নাম: `AiPlatform`) → ১৬-অক্ষরের password কপি
3. Secrets সেট করো (password **কখনো** appsettings/git-এ না):
   ```powershell
   cd D:\Saiful\AiPlatform\backend\src\Api
   dotnet user-secrets set "Smtp:Host" "smtp.gmail.com"
   dotnet user-secrets set "Smtp:User" "tomar-gmail@gmail.com"
   dotnet user-secrets set "Smtp:Password" "<app-password-১৬-অক্ষর>"
   dotnet user-secrets set "Smtp:FromEmail" "tomar-gmail@gmail.com"
   ```
   (Port 587 + FromName default-ই আছে)
4. API restart → forgot-password চালাও → console-এ `Email sent → ...` + inbox-এ email ✓

### অন্য provider
Resend/SendGrid/Mailgun-এর SMTP credential একই চার key-তে বসাও। Docker/production-এ env var: `Smtp__Host`, `Smtp__User`, `Smtp__Password`, `Smtp__FromEmail`।

---

## ৪. Google Login

### কীভাবে কাজ করে
Frontend-এ Google-এর নিজের button (GIS) → Google popup → **ID token** → `POST /auth/google` → backend token verify করে (signature, audience, email_verified) → email-এ account থাকলে সেটাই, নইলে password-হীন নতুন account → আমাদের নিজস্ব JWT + refresh cookie (সবসময় persistent)।

### Setup — step by step
1. [console.cloud.google.com/apis/credentials](https://console.cloud.google.com/apis/credentials) → project বানাও/বাছো
2. **OAuth consent screen** (প্রথমবার): External → app name + support email → নিজের Gmail **Test users**-এ যোগ করো
3. **Create Credentials → OAuth client ID → Web application**:
   - Authorized JavaScript origins: `http://localhost:5173`
   - Redirect URI **লাগবে না** (GIS popup flow)
4. Client ID (দেখতে `12345-abc.apps.googleusercontent.com`) **দুই জায়গায়** — `<>` ছাড়া:
   ```
   # frontend/.env
   VITE_GOOGLE_CLIENT_ID=12345-abc.apps.googleusercontent.com
   ```
   ```powershell
   # backend
   cd D:\Saiful\AiPlatform\backend\src\Api
   dotnet user-secrets set "Authentication:Google:ClientId" "12345-abc.apps.googleusercontent.com"
   ```
5. **দুই server-ই restart** (vite `.env` শুধু start-এ পড়ে)

### টেস্ট
1. Login page → Google-এর নিজের রঙিন button দেখাবে (configure না থাকলে ধূসর placeholder + toast)
2. ক্লিক → Google account বাছো (consent screen Testing mode-এ থাকলে **Test users-এ যোগ করা** account-ই লাগবে)
3. ঢুকে যাবে; sidebar-এ Google-এর নাম/email ✓
4. একই email-এ আগে password দিয়ে register করা থাকলে সেই account-এই ঢুকবে (নতুন বানায় না)

---

## ৫. সাধারণ ফাঁদ

| লক্ষণ | কারণ | ফিক্স |
|---|---|---|
| কিছুই বদলায়নি মনে হচ্ছে | পুরনো server চলছে | দুটোই restart + `Ctrl+Shift+R` |
| Google button ধূসর placeholder | `VITE_GOOGLE_CLIENT_ID` নেই/ভুল | `.env` দেখো, `<>` নেই তো? restart দিয়েছ? |
| Google popup: "unregistered origin" | Console-এ origin যোগ হয়নি | JavaScript origins-এ `http://localhost:5173` |
| Google-এ ঢুকলে 400 not-configured | backend ClientId নেই | user-secrets কমান্ডটা `src/Api` folder-এ চালাও |
| Reset email আসে না | SMTP configure নেই | §৩, নয়তো API log থেকে link |
| Reset-এ "link expired" | token একবার ব্যবহৃত/নতুন request হয়েছে | আবার forgot-password করো |
| Remember me "কাজ করে না" | tab বন্ধ করে টেস্ট করছ | browser **পুরো** বন্ধ করতে হবে |
