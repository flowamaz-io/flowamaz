---
prompt-id: 07-docker-deployment
phase: 01
sequence: 7
roles: [Executor, Verifier, Security]
type: feature
depends-on: [06-frontend-scaffold]
estimated-complexity: Medium
---

# Docker, Nginx & Complete Deployment Configuration

## Context
Backend and frontend built and tested locally. Now containerise everything, wire up
nginx as reverse proxy, configure SSL for local dev, and write complete deployment docs.

## Objective
Dockerfiles for backend and frontend, full docker-compose.yml (5 services), nginx
with security headers and rate limiting, dev SSL setup, and complete documentation.

## Scope

### What to Build

**backend/Dockerfile (multi-stage):**
```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY *.sln .
COPY Flowamaz.Api/*.csproj Flowamaz.Api/
COPY Flowamaz.Core/*.csproj Flowamaz.Core/
COPY Flowamaz.Application/*.csproj Flowamaz.Application/
COPY Flowamaz.Infrastructure/*.csproj Flowamaz.Infrastructure/
RUN dotnet restore
COPY . .
RUN dotnet publish Flowamaz.Api -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
RUN adduser --disabled-password --gecos '' appuser && chown -R appuser /app
USER appuser
COPY --from=build /app/publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s \
  CMD curl -f http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "Flowamaz.Api.dll"]
```

**web/Dockerfile (multi-stage):**
```dockerfile
FROM node:22-alpine AS build
WORKDIR /app
COPY package*.json .
RUN npm ci
COPY . .
RUN npm run build

FROM nginx:alpine AS runtime
COPY --from=build /app/dist /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 3000
```

**web/nginx.conf (SPA routing):**
```nginx
server {
  listen 3000;
  root /usr/share/nginx/html;
  gzip on;
  gzip_types text/plain text/css application/javascript application/json;

  location / {
    try_files $uri $uri/ /index.html;
  }

  location ~* \.(js|css|png|jpg|jpeg|gif|ico|svg|woff2)$ {
    expires 1y;
    add_header Cache-Control "public, immutable";
  }
}
```

**infrastructure/docker-compose.yml (5 services):**
```yaml
version: '3.9'
services:
  proxy:
    image: nginx:alpine
    ports: ["80:80", "443:443"]
    volumes:
      - ./nginx/nginx.conf:/etc/nginx/nginx.conf:ro
      - ./nginx/ssl:/etc/nginx/ssl:ro
    depends_on: [backend, web]
    restart: unless-stopped

  backend:
    build: ../backend
    env_file: ../.env
    depends_on:
      db:
        condition: service_healthy
      redis:
        condition: service_started
    restart: unless-stopped
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/health"]
      interval: 30s
      timeout: 5s
      retries: 3

  web:
    build: ../web
    restart: unless-stopped

  db:
    image: postgres:16-alpine
    env_file: ../.env
    environment:
      POSTGRES_DB: ${DB_NAME}
      POSTGRES_USER: ${DB_USER}
      POSTGRES_PASSWORD: ${DB_PASSWORD}
    volumes:
      - pgdata:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U ${DB_USER} -d ${DB_NAME}"]
      interval: 5s
      timeout: 3s
      retries: 10
    restart: unless-stopped

  redis:
    image: redis:7-alpine
    volumes: [redisdata:/data]
    command: redis-server --appendonly yes
    restart: unless-stopped

volumes:
  pgdata:
  redisdata:
```

**infrastructure/nginx/nginx.conf (full config):**
```nginx
events { worker_connections 1024; }

http {
  # Rate limit zones
  limit_req_zone $binary_remote_addr zone=auth:10m rate=10r/m;
  limit_req_zone $binary_remote_addr zone=register:10m rate=5r/h;
  limit_req_zone $binary_remote_addr zone=api:10m rate=100r/m;

  server {
    listen 80;
    server_name _;
    return 301 https://$host$request_uri;
  }

  server {
    listen 443 ssl;
    server_name _;

    ssl_certificate /etc/nginx/ssl/cert.pem;
    ssl_certificate_key /etc/nginx/ssl/key.pem;
    ssl_protocols TLSv1.2 TLSv1.3;
    ssl_ciphers HIGH:!aNULL:!MD5;

    # Security headers
    add_header X-Frame-Options "SAMEORIGIN" always;
    add_header X-Content-Type-Options "nosniff" always;
    add_header X-XSS-Protection "1; mode=block" always;
    add_header Referrer-Policy "strict-origin-when-cross-origin" always;
    add_header Permissions-Policy "camera=(), microphone=(), geolocation=()" always;
    add_header Strict-Transport-Security "max-age=63072000; includeSubDomains" always;

    # API routes
    location /api/v1/auth/login {
      limit_req zone=auth burst=5 nodelay;
      proxy_pass http://backend:8080;
      proxy_set_header Host $host;
      proxy_set_header X-Real-IP $remote_addr;
      proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
      proxy_set_header X-Forwarded-Proto $scheme;
    }

    location /api/v1/auth/register {
      limit_req zone=register burst=2 nodelay;
      proxy_pass http://backend:8080;
      proxy_set_header Host $host;
      proxy_set_header X-Real-IP $remote_addr;
      proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    }

    location /api/ {
      limit_req zone=api burst=20 nodelay;
      proxy_pass http://backend:8080;
      proxy_set_header Host $host;
      proxy_set_header X-Real-IP $remote_addr;
      proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
      proxy_set_header X-Forwarded-Proto $scheme;
      proxy_read_timeout 60s;
    }

    location /scalar {
      proxy_pass http://backend:8080;
    }

    location /health {
      proxy_pass http://backend:8080;
      access_log off;
    }

    # Frontend (SPA)
    location / {
      proxy_pass http://web:3000;
      proxy_set_header Host $host;
    }
  }
}
```

**Dev SSL setup (infrastructure/nginx/ssl/):**
```bash
# infrastructure/nginx/ssl/generate-dev-cert.sh
#!/bin/bash
openssl req -x509 -nodes -days 365 -newkey rsa:2048 \
  -keyout key.pem -out cert.pem \
  -subj "/C=MY/ST=Kuala Lumpur/L=KL/O=Flowamaz/CN=localhost" \
  -addext "subjectAltName=DNS:localhost,IP:127.0.0.1"
echo "Dev SSL certificate generated: cert.pem + key.pem"
```

infrastructure/nginx/ssl/.gitignore:
```
*.pem
*.crt
*.key
!generate-dev-cert.sh
!README.md
```

**Documentation:**

README.md (project root):
```markdown
# Flowamaz

AI-native workflow orchestration platform.

## Quick Start

### Prerequisites
- Docker + Docker Compose
- Git

### Development (local)
cp .env.example .env
# Edit .env with your values

# Start dependencies only (postgres + redis)
docker compose -f infrastructure/docker-compose.dev.yml up -d

# Generate dev SSL cert
cd infrastructure/nginx/ssl && bash generate-dev-cert.sh && cd ../../..

# Run backend
cd backend && dotnet run --project Flowamaz.Api

# Run frontend
cd web && npm install && npm run dev

### Full stack (Docker)
docker compose -f infrastructure/docker-compose.yml up -d

# App: https://localhost
# API: https://localhost/api
# API docs: https://localhost/scalar
# Health: https://localhost/health

### Run tests
cd backend && dotnet test
cd web && npm run test
```

docs/DEVELOPMENT.md — local dev setup, individual service debugging,
  hot reload, test runner setup, Testcontainers requirements

docs/DEPLOYMENT.md — production deployment checklist:
  - Environment variables reference (all vars from .env.example with descriptions)
  - SSL: replace dev cert with real cert (Let's Encrypt certbot instructions)
  - DNS records required (MX, SPF, DKIM, DMARC for flowamaz.io email)
  - Microsoft 365 email setup for team@flowamaz.io
  - Resend setup for noreply@flowamaz.io
  - First deployment steps (run migrations, verify health)
  - Backup strategy (postgres pg_dump, redis RDB)

docs/SECURITY.md (also committed to GitHub root):
```markdown
# Security Policy

## Reporting Vulnerabilities

Please report security vulnerabilities to security@flowamaz.io.
Do NOT open a public GitHub issue for security findings.

We aim to acknowledge reports within 24 hours and resolve within 90 days
depending on severity.

## Scope
- app.flowamaz.io and *.flowamaz.io
- flowamaz-io/flowamaz (public monorepo)
- flowamaz-io/connectors

## Out of Scope
- Denial of service attacks
- Social engineering
```

### What NOT to Build
- No Kubernetes — not needed yet
- No CI/CD pipeline — Phase 5
- No AWS-specific config — use generic Docker

## Acceptance Criteria
- [ ] docker compose up -d → all 5 services healthy within 60 seconds
- [ ] GET https://localhost/health → 200 {"db":"healthy","redis":"healthy"}
- [ ] GET https://localhost/scalar → 200 Scalar UI renders
- [ ] GET https://localhost → Vue app loads
- [ ] POST https://localhost/api/v1/auth/login → works (with test user from prompt 04 tests)
- [ ] HTTP → HTTPS redirect works (GET http://localhost → 301)
- [ ] Security headers present on all responses (X-Frame-Options, X-Content-Type-Options etc)
- [ ] No secrets in any committed file (git grep for common patterns)

## Output Expected
```
backend/Dockerfile
web/Dockerfile
web/nginx.conf
infrastructure/docker-compose.yml
infrastructure/docker-compose.dev.yml
infrastructure/nginx/nginx.conf
infrastructure/nginx/ssl/generate-dev-cert.sh
infrastructure/nginx/ssl/.gitignore
infrastructure/nginx/ssl/README.md
SECURITY.md
README.md
docs/DEVELOPMENT.md
docs/DEPLOYMENT.md
```
