FROM node:22-alpine
WORKDIR /app
COPY package*.json ./
RUN npm ci
COPY tsconfig.json ./
COPY src ./src
COPY README.md ./README.md
COPY .env.example ./.env.example
RUN npm run build
EXPOSE 3000
CMD ["sh", "-c", "npm run migrate && npm run start"]
