# User Secrets — beginner guide (বাংলা)

> .NET-এ dev-time গোপন তথ্য (API key, password, connection string) নিরাপদে রাখার উপায়। git-এ যায় না।

---

## ১. সমস্যা যা এটা সমাধান করে

Secret কোডে বা `appsettings.json`-এ রাখলে → git-এ commit হয়ে যায় → leak (ঠিক যেমন আমাদের demo-তে হয়েছিল)। User-secrets ঐ secret কে **repo-র বাইরে, তোমার মেশিনের এক ফাইলে** রাখে। কোড ঐ ফাইল থেকে পড়ে, কিন্তু ফাইলটা কখনো repo-তে থাকে না।

---

## ২. ফাইলটা কোথায়

প্রতি project-এর `.csproj`-এ একটা `<UserSecretsId>` (GUID) থাকে। সেই GUID-এর নামে ফাইল:

```
Windows: C:\Users\<you>\AppData\Roaming\Microsoft\UserSecrets\<UserSecretsId>\secrets.json
Linux/Mac: ~/.microsoft/usersecrets/<UserSecretsId>/secrets.json
```

এই project-এ (Api):
```
C:\Users\DATABIZ\AppData\Roaming\Microsoft\UserSecrets\553d5adb-9d3a-4a49-98a4-d72b03a78c5f\secrets.json
```

> ভুল GUID-এর ফোল্ডার খুললে খালি দেখাবে — GUID `Api.csproj`-এর সাথে মেলাও।

---

## ৩. ফাইলটা দেখতে কেমন

সাধারণ flat JSON। key-তে `:` দিয়ে config-এর স্তর বোঝায়:

```json
{
  "Jwt:SecretKey": "huXCd5...",
  "Llm:Providers:gemini:ApiKey": "AQ.Ab8...",
  "Llm:Embedding:ApiKey": "AQ.Ab8..."
}
```

`"Llm:Providers:gemini:ApiKey"` মানে `appsettings`-এর:
```json
{ "Llm": { "Providers": { "gemini": { "ApiKey": "..." } } } }
```
অর্থাৎ `:` = nesting। appsettings + secrets **merge** হয়ে যায়; secrets জেতে (override করে)।

---

## ৪. secret যোগ/দেখা — ৩ উপায়

**(ক) CLI (সবচেয়ে নিরাপদ, recommended)** — project folder থেকে:
```bash
dotnet user-secrets set "Llm:Providers:gemini:ApiKey" "your-key-here" --project src/Api
dotnet user-secrets list --project src/Api          # সব দেখাও
dotnet user-secrets remove "Llm:Providers:gemini:ApiKey" --project src/Api
dotnet user-secrets clear --project src/Api          # সব মুছে দাও
```

**(খ) Visual Studio** — Solution Explorer → project-এ right-click → **Manage User Secrets** → secrets.json খুলবে, হাতে edit করো।

**(গ) সরাসরি ফাইল edit** — উপরের path-এর secrets.json খুলে JSON হাতে লেখো (valid JSON রাখতে হবে)।

---

## ৫. কোড কীভাবে পড়ে

`WebApplication.CreateBuilder` **Development environment-এ** user-secrets auto-load করে (production-এ নয়)। তারপর সাধারণ config হিসেবেই পাওয়া যায়:
```csharp
builder.Configuration["Llm:Providers:gemini:ApiKey"];
// বা Options binding: IOptions<LlmSettings>
```
এই project-এ key গুলো `LlmSettings`/`JwtSettings`-এ bind হয়।

> মনে রাখো: user-secrets **শুধু dev**। Production-এ একই key **environment variable** দিয়ে আসবে — যেমন `Llm__Providers__gemini__ApiKey` (`:` → `__` double underscore)।

---

## ৬. লাভ (সংক্ষেপে)

- Secret git-এ যায় না → leak নেই।
- প্রতি dev নিজের key রাখে, একে অন্যেরটা দরকার নেই।
- appsettings পরিষ্কার থাকে (শুধু non-secret config)।
- dev → prod-এ একই কোড, শুধু secret-এর উৎস বদলায় (secrets → env var)।

---

## ৭. ⚠️ সীমা / সতর্কতা

- এটা **encryption নয়** — plain JSON, শুধু repo-র বাইরে। মেশিনে যার access আছে সে পড়তে পারে। তাই "commit হওয়া থেকে বাঁচানো" এর মূল কাজ, "hacker থেকে বাঁচানো" নয়।
- Production secret এখানে রেখো না — ওখানে vault/env var (Azure Key Vault, AWS Secrets Manager ইত্যাদি)।
