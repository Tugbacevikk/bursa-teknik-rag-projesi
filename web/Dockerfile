# The web front door (PLANNING Faz 6, E2): the Angular app built with Node, served by Caddy,
# which also passes /api to the API and does HTTPS (caddy/Caddyfile).

FROM node:24-alpine AS build
WORKDIR /src

# Packages first, so this layer is reused until package-lock.json changes.
COPY package.json package-lock.json ./
RUN npm ci

COPY . .
RUN npx ng build


FROM caddy:2.10-alpine

# An unprivileged user; Caddy listens on 8080/8443 inside, so it needs no root. /data keeps
# the certificates (a volume in compose), /config what Caddy saves for itself.
RUN addgroup -S web \
    && adduser -S -G web web \
    && mkdir -p /data /config \
    && chown -R web:web /data /config

COPY caddy/Caddyfile /etc/caddy/Caddyfile
COPY caddy/tls /etc/caddy/tls
COPY --from=build /src/dist/btu-asistan-web/browser /srv

USER web
EXPOSE 8080 8443

HEALTHCHECK --interval=15s --timeout=5s --start-period=10s --retries=3 \
    CMD wget --quiet --output-document=/dev/null http://127.0.0.1:8081/ || exit 1
