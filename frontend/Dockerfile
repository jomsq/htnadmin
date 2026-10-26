# syntax=docker/dockerfile:1
# HTN Admin web. Targets:
#   dev   - Vite dev server with hot reload (used by docker-compose.yml)
#   build - production bundle in /app/dist
#   prod  - nginx serving the bundle, SPA fallback, /api proxied to the API (default target)
ARG NODE_IMAGE=node:22-alpine
ARG NGINX_IMAGE=nginx:1.27-alpine

# ---- deps: npm ci, cached on package-lock.json --------------------------------------------
FROM ${NODE_IMAGE} AS deps
WORKDIR /app
RUN chown node:node /app
USER node
COPY --chown=node:node package.json package-lock.json ./
RUN --mount=type=cache,id=htn-npm,target=/home/node/.npm,uid=1000,gid=1000 \
    npm ci --no-audit --no-fund

# ---- dev: Vite dev server --------------------------------------------------------------------
FROM deps AS dev
ENV NODE_ENV=development
COPY --chown=node:node . .
EXPOSE 5173
HEALTHCHECK --interval=15s --timeout=5s --start-period=30s --retries=5 \
  CMD wget -q --spider http://127.0.0.1:5173/ || exit 1
CMD ["npm", "run", "dev", "--", "--host", "0.0.0.0", "--port", "5173"]

# ---- build: production bundle ----------------------------------------------------------------
FROM deps AS build
COPY --chown=node:node . .
RUN npm run build

# ---- prod: nginx -----------------------------------------------------------------------------
FROM ${NGINX_IMAGE} AS prod
# The official image renders /etc/nginx/templates/*.template with envsubst at start-up,
# substituting only variables that are set (so nginx's own $uri etc. are left alone).
ENV API_UPSTREAM=api:8080 \
    NGINX_RESOLVER=127.0.0.11 \
    TRUSTED_PROXY_CIDR=127.0.0.1/32
RUN rm -f /etc/nginx/conf.d/default.conf
COPY nginx.conf /etc/nginx/templates/default.conf.template
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 80
HEALTHCHECK --interval=15s --timeout=5s --start-period=10s --retries=5 \
  CMD wget -q --spider http://127.0.0.1/healthz || exit 1
