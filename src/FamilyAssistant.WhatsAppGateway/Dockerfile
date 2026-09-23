FROM node:22-bookworm-slim AS build
WORKDIR /app
ENV PUPPETEER_SKIP_DOWNLOAD=true
COPY src/FamilyAssistant.WhatsAppGateway/package*.json ./
RUN npm ci
COPY src/FamilyAssistant.WhatsAppGateway/ ./
RUN npm run build
FROM build AS test
ENTRYPOINT ["npm", "test"]
FROM node:22-bookworm-slim AS runtime
RUN apt-get update && apt-get install -y --no-install-recommends chromium ca-certificates fonts-liberation && rm -rf /var/lib/apt/lists/*
WORKDIR /app
ENV NODE_ENV=production PORT=3000
COPY --from=build /app/dist/src ./dist/src
COPY --from=build /app/package.json ./
COPY --from=build /app/node_modules ./node_modules
RUN mkdir -p /app/.wwebjs_auth && chown -R node:node /app/.wwebjs_auth
USER node
EXPOSE 3000
HEALTHCHECK --interval=15s --timeout=5s --start-period=15s --retries=3 CMD node -e "fetch('http://127.0.0.1:3000/health').then(r=>{if(!r.ok)process.exit(1)}).catch(()=>process.exit(1))"
CMD ["node", "dist/src/server.js"]
