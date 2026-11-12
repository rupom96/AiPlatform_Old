# Docker Cheatsheet — beginner-friendly (বাংলা)

> এই project-এ Docker দিয়ে যা করি তার রেফারেন্স। নতুন হলে উপরের থেকে পড়ো।

---

## ১. Docker আসলে কী (intuition)

তোমার মেশিনে SQL Server, Redis, Qdrant — প্রতিটা আলাদা করে install করা ঝামেলা (version conflict, "আমার মেশিনে চলে তোমারটায় চলে না")।

**Docker = ছোট, আলাদা, throwaway বাক্স (container)** যার ভেতরে software + তার সব dependency প্যাক করা। বাক্সটা যেকোনো মেশিনে হুবহু একইভাবে চলে।

Analogy: container = shipping container. ভেতরে কী আছে জাহাজের দরকার নেই — শুধু বাক্সটা তোলে-নামায়। তোমার মেশিন = জাহাজ, SQL Server = বাক্সের ভেতরের মাল।

### ৪টা শব্দ জানলেই শুরু
| শব্দ | মানে | analogy |
|---|---|---|
| **Image** | software-এর blueprint/template (read-only) | কেকের রেসিপি |
| **Container** | image চালু করলে যে জীবন্ত instance | রেসিপি দিয়ে বানানো কেক |
| **Volume** | container-এর বাইরে data রাখার জায়গা (container মুছলেও data থাকে) | কেক ফ্রিজে রাখা |
| **Port mapping** | container-এর ভেতরের port → তোমার মেশিনের port | বাক্সের জানালা বাইরে বের করা |

> মূল কথা: **container মুছে ফেলা normal ও নিরাপদ** — যদি data volume-এ থাকে। container disposable, volume persistent।

---

## ২. এই project-এ আমরা Docker দিয়ে কী চালাই

`docker-compose.yml` ফাইলটা বলে দেয় কোন কোন container চালাতে হবে। এখন ২টা:

| Service | Image | Port | কাজ |
|---|---|---|---|
| `aiplatform-sqlserver` | SQL Server 2022 | 1433 | main database (users, chat, docs) |
| `aiplatform-redis` | Redis 7 | 6379 | cache + refresh-token store + rate-limit |

(Phase 3 এ Qdrant, Phase 5 এ আমাদের API নিজেও container-এ ঢুকবে + nginx load balancer।)

**compose কী করে দেয়:** এক command-এ দুটোই চালু, নিজেদের মধ্যে network, data volume-এ persist, health check। আলাদা করে SQL Server install করতে হয় না।

---

## ৩. যে command গুলো তুমি আসলে ব্যবহার করবে

সবগুলো project root (`D:\Saiful\AiPlatform`) থেকে চালাও, যেখানে `docker-compose.yml` আছে।

### দৈনন্দিন
```bash
docker compose up -d              # backing services (sqlserver+redis+qdrant) — local dev
docker compose --profile full up -d --build   # full stack + nginx LB (api×2) → http://localhost:8080
docker compose ps                 # কোনটা চলছে দেখো
docker compose stop               # থামাও (data থাকবে, container থাকবে)
docker compose down               # থামাও + container মুছে ফেলো (volume/data থাকবে)
docker compose down -v            # + volume/data মুছে ফেলো (fresh DB)
docker compose logs -f api1       # এক service-এর live log
```

### দুই মোড (profiles)
- **local dev:** `docker compose up -d` → শুধু DB/Redis/Qdrant container-এ; **API `dotnet run --project backend/src/Api`, frontend `npm run dev`** (host থেকে, দ্রুত iterate)।
- **full stack:** `docker compose --profile full up -d --build` → API-ও container-এ, nginx LB পেছনে দুই replica। entry `http://localhost:8080`। এটাই production-এর কাছাকাছি।

### full stack verify (LB কাজ করছে?)
```bash
# বারবার hit করলে X-Instance header api1/api2 এর মধ্যে বদলাবে (round-robin)
curl -i http://localhost:8080/api/v1/ping
```

### একটু গভীরে
```bash
docker ps                   # চলমান সব container (শুধু এই project না)
docker ps -a                # বন্ধগুলোসহ সব
docker inspect --format '{{.State.Health.Status}}' aiplatform-sqlserver   # health
docker exec -it aiplatform-redis redis-cli   # redis-এর ভেতরে ঢুকে command চালাও
docker stats                # live CPU/RAM usage
```

### SQL Server-এর ভেতরে query চালানো
```bash
docker exec -it aiplatform-sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P "Your_strong_Pass123!" -C -d AiPlatform \
  -Q "SELECT name FROM sys.tables"
```
(`-C` = certificate trust, `-d` = database, `-Q` = query চালিয়ে বেরিয়ে যাও)

### পরিষ্কার করা (সাবধান)
```bash
docker compose down -v      # container + VOLUME/DATA-ও মুছে ফেলো (fresh start; DB খালি হবে)
docker system prune         # unused image/container/network মুছে জায়গা খালি করো
```
> ⚠️ `-v` মানে data হারাবে। নতুন করে migration চালাতে হবে। ইচ্ছাকৃত reset ছাড়া `-v` দিও না।

---

## ৪. "কী করলে কী হয়" — mental model

```
docker compose up -d
   └─ image নেই? → download (প্রথমবার ধীর)
   └─ container বানায় → চালু করে → background-এ ছেড়ে দেয়
   └─ volume আগেই থাকলে পুরনো data ফিরে আসে

docker compose down
   └─ container থামায় + মুছে ফেলে
   └─ volume থাকে → পরের `up`-এ data ফেরত

docker compose down -v
   └─ container + volume সব মোছে → পরের `up` = খালি DB
```

মনে রাখো: **container মুছলে data হারায় না, volume মুছলে হারায়।** আমাদের DB data `sqlserver-data` volume-এ, redis data `redis-data` volume-এ।

---

## ৫. সাধারণ সমস্যা ও সমাধান

| সমস্যা | কারণ | সমাধান |
|---|---|---|
| `port is already allocated` | 1433/6379 আগেই কেউ ব্যবহার করছে (local SQL/Redis?) | পুরনোটা বন্ধ করো, বা compose-এ port বদলাও |
| API "cannot connect to DB" | container চালু হয়নি / এখনো healthy না | `docker compose ps`, health `healthy` হওয়া পর্যন্ত অপেক্ষা |
| `docker` command not found | Docker Desktop চালু নেই | Docker Desktop খোলো, whale icon সবুজ হওয়া পর্যন্ত অপেক্ষা |
| data হঠাৎ গায়েব | ভুলে `down -v` দিয়েছ | migration আবার চালাও (`dotnet ef database update`) |
| container বারবার restart | ভুল env/password | `docker compose logs sqlserver` দেখো |

---

## ৬. Docker + Kubernetes কীভাবে শিখবে (আমার সাথে)

**নীতি (md ডকুমেন্টের থিম):** tool মুখস্থ না, concept শেখো। Docker/K8s বদলাবে, কিন্তু "container, image, orchestration" ধারণা থাকবে।

### ধাপে ধাপে path
1. **Docker basics (এখন)** — এই project-এই শিখছ। প্রতি command আমি চালানোর আগে বলব কী হবে। তুমি নিজে `docker compose ps`, `logs`, `exec` চালিয়ে হাত পাকাও।
2. **Dockerfile লেখা (Phase 5)** — আমাদের API-কে নিজে container বানাব। তখন শিখবে image কীভাবে build হয় (`FROM`, `COPY`, `RUN`, `ENTRYPOINT`)।
3. **docker-compose গভীরে (Phase 5)** — multi-container + load balancer (nginx) + network। এটাই তোমার mini distributed system.
4. **Kubernetes (পরে, optional)** — যখন "একটা মেশিনে ২টা container" থেকে "বহু মেশিনে বহু container, auto-heal, auto-scale" দরকার। md-02-এর Raft/etcd মনে আছে? K8s-এর মস্তিষ্ক etcd (Raft)। তাই distributed-systems concept আগে, K8s পরে — তাহলে K8s সহজ লাগবে।

### কীভাবে আমি শেখাব
- প্রতি Docker step-এ: **আগে বলব কী করব ও কেন**, তারপর চালাব, তারপর output ব্যাখ্যা।
- তুমি "এটা কেন?" জিজ্ঞেস করতে পারো যেকোনো সময়।
- এই cheatsheet phase বাড়ার সাথে update হবে (Dockerfile, K8s অংশ যোগ হবে)।

### বাইরের রিসোর্স (concept-first)
- Docker: official "Get Started" (docs.docker.com/get-started) — hands-on।
- Kubernetes: "Kubernetes Basics" interactive tutorial (kubernetes.io/docs/tutorials)।
- বই/ধারণা: distributed-systems ভিত্তি (আমাদের md-02) → তারপর K8s architecture।

> পরামর্শ: এখন K8s-এ ঝাঁপিও না। Docker + compose ভালো করে বুঝলে K8s ৮০% সহজ। premature K8s = md-04-এর "premature complexity" ভুল।
