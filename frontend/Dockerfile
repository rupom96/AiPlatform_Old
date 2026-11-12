# Multi-stage — node দিয়ে build, nginx দিয়ে serve। final image-এ কোনো .tsx/.ts নেই।
FROM node:22-alpine AS build
WORKDIR /src

# lockfile আগে → dependency না বদলালে npm ci layer cache থেকে আসে
COPY package.json package-lock.json ./
RUN npm ci

COPY . .

# ⚠️ VITE_API_BASE_URL build-time (baseApi.ts) — bundle-এ বেক হয়ে যায়, runtime env var
# দিয়ে বদলানো যায় না। URL বদলালে image আবার build করতে হবে।
ARG VITE_API_BASE_URL
ARG VITE_GOOGLE_CLIENT_ID=
# build-arg সরাসরি process env হয় না, তাই .env.production লিখে দিই — Vite ওটা নিশ্চিত পড়ে।
# (.dockerignore-এ .env* বাদ, তাই host-এর কোনো .env এখানে আসে না — এটাই একমাত্র উৎস।)
RUN test -n "$VITE_API_BASE_URL" || (echo "VITE_API_BASE_URL build-arg lagbe" && exit 1) \
    && printf 'VITE_API_BASE_URL=%s\nVITE_GOOGLE_CLIENT_ID=%s\n' \
        "$VITE_API_BASE_URL" "$VITE_GOOGLE_CLIENT_ID" > .env.production \
    && npm run build

FROM nginx:alpine AS runtime
COPY --from=build /src/dist /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 80
