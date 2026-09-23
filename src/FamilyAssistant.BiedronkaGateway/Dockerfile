FROM node:22-bookworm-slim
RUN apt-get update && apt-get install -y --no-install-recommends chromium xvfb x11vnc novnc websockify fonts-liberation curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY src/FamilyAssistant.BiedronkaGateway/package*.json ./
RUN npm ci --omit=dev
COPY src/FamilyAssistant.BiedronkaGateway/*.mjs ./
RUN mkdir /state /imports && chown node:node /state /imports
USER node
EXPOSE 3001 6080
HEALTHCHECK --interval=20s --start-period=60s CMD curl --fail --silent http://127.0.0.1:3001/health || exit 1
CMD ["node", "server.mjs"]
