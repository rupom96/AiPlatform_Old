# Docker — Quick Command Card (বাংলা)

> শুধু কমান্ড + এক লাইনে কাজ। ধারণা/analogy/troubleshooting → [docker-cheatsheet.md](docker-cheatsheet.md)।
> সব command project root (`D:\Saiful\AiPlatform`, যেখানে `docker-compose.yml`) থেকে চালাও।

---

## দৈনন্দিন (compose)

| Command | কাজ |
|---|---|
| `docker compose up -d` | backing services (sqlserver + redis + qdrant) background-এ চালু |
| `docker compose --profile full up -d --build` | full stack — API×2 + nginx LB সহ build+চালু → `http://localhost:8080` |
| `docker compose ps` | কোন container চলছে + health দেখাও |
| `docker compose stop` | সব থামাও (container + data থাকবে) |
| `docker compose start` | থামানো container আবার চালাও |
| `docker compose restart redis` | এক service restart |
| `docker compose down` | থামাও + container মুছো (⚠️ volume/data থাকবে) |
| `docker compose down -v` | + volume/data মুছো (fresh DB — ⚠️ সব হারাবে) |
| `docker compose logs -f api1` | এক service-এর live log (Ctrl+C বেরোতে) |
| `docker compose pull` | image-গুলোর নতুন version নামাও |

---

## দেখা / inspect

| Command | কাজ |
|---|---|
| `docker ps` | চলমান সব container (সব project) |
| `docker ps -a` | বন্ধগুলোসহ সব container |
| `docker stats` | live CPU / RAM usage |
| `docker images` | নামানো সব image |
| `docker volume ls` | সব volume (persistent data) |
| `docker logs -f aiplatform-redis` | নাম দিয়ে এক container-এর log |
| `docker inspect --format '{{.State.Health.Status}}' aiplatform-sqlserver` | এক container healthy কিনা |

---

## container-এর ভেতরে ঢোকা

| Command | কাজ |
|---|---|
| `docker exec -it aiplatform-redis redis-cli` | Redis CLI-তে ঢুকে command চালাও |
| `docker exec -it aiplatform-redis redis-cli KEYS '*'` | Redis-এর সব key দেখো (dev only) |
| `docker exec -it aiplatform-sqlserver bash` | SQL Server container-এর shell-এ ঢোকো |

### SQL Server query (এক শটে)
```bash
docker exec -it aiplatform-sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P "Your_strong_Pass123!" -C -d AiPlatform \
  -Q "SELECT name FROM sys.tables"
```
`-C` = certificate trust · `-d` = কোন database · `-Q` = query চালিয়ে বেরিয়ে যাও

---

## পরিষ্কার (⚠️ সাবধান)

| Command | কাজ |
|---|---|
| `docker compose down -v` | এই project-এর container + volume/data মুছো (fresh start) |
| `docker system prune` | unused container/network/dangling image মুছে জায়গা খালি |
| `docker system prune -a --volumes` | ⚠️ unused সব image + volume-ও মুছো (aggressive) |
| `docker volume rm aiplatform_sqlserver-data` | নির্দিষ্ট এক volume মুছো |

> নিয়ম: **container মুছলে data থাকে, volume মুছলে data হারায়।** ইচ্ছাকৃত reset ছাড়া `-v` দিও না।

---

## verify (LB round-robin কাজ করছে?)
```bash
curl -i http://localhost:8080/api/v1/ping   # বারবার hit → X-Instance header api1/api2 বদলাবে
```
