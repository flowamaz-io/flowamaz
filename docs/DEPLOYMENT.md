# Deployment

Generic Docker deployment. No Kubernetes / cloud-specific config in Phase 1.

## 1. Environment variables
Copy `.env.example` to `.env` and set real values. Key variables:

| Variable | Description |
|----------|-------------|
| `DB_NAME` / `DB_USER` / `DB_PASSWORD` | Postgres database, user, password (compose seeds the db container from these). |
| `DB_CONNECTION_STRING` | Overridden by compose to `Host=db;...`. For host dev it points at localhost. |
| `REDIS_CONNECTION_STRING` | Overridden by compose to `redis:6379`. |
| `JWT_SECRET` | **Required** (≥ 32 chars). Signs access tokens. The API refuses to start outside Development without it. |
| `JWT_ISSUER` / `JWT_AUDIENCE` | Token issuer/audience. |
| `ANTHROPIC_PLATFORM_KEY` | Platform-managed Anthropic key (only Anthropic is platform-managed). |
| `RESEND_API_KEY` / `EMAIL_FROM_ADDRESS` | Transactional email (noreply@flowamaz.io). For Docker deployments use `Email__ResendApiKey` (double underscore) to override the nested .NET config section. |
| `CORS_ALLOWED_ORIGINS` | Comma-separated allowed origins for the SPA. |

Never commit `.env` or any populated secret.

## 2. TLS certificate
Replace the self-signed dev cert with a real one.

**Let's Encrypt (certbot):**
```bash
sudo certbot certonly --standalone -d app.flowamaz.io
# Copy the issued files where the proxy expects them:
cp /etc/letsencrypt/live/app.flowamaz.io/fullchain.pem infrastructure/nginx/ssl/cert.pem
cp /etc/letsencrypt/live/app.flowamaz.io/privkey.pem  infrastructure/nginx/ssl/key.pem
```
Automate renewal with a certbot cron/systemd timer and reload the proxy after renewal.

## 3. DNS records (flowamaz.io email)
| Type | Host | Purpose |
|------|------|---------|
| MX | flowamaz.io | Microsoft 365 mail routing |
| TXT (SPF) | flowamaz.io | `v=spf1 include:spf.protection.outlook.com include:_spf.resend.com -all` |
| CNAME/TXT (DKIM) | selector._domainkey | DKIM signing for M365 + Resend |
| TXT (DMARC) | _dmarc.flowamaz.io | `v=DMARC1; p=quarantine; rua=mailto:security@flowamaz.io` |

- **Microsoft 365 Business Basic** hosts team@flowamaz.io and shared mailboxes (support@, security@). Add the domain in the M365 admin centre and apply the MX/SPF/DKIM records it provides.
- **Resend** sends transactional mail from noreply@flowamaz.io. Verify the domain in Resend and add its DKIM/SPF entries (merge SPF includes into one record).

## 4. First deployment
```bash
docker compose -f infrastructure/docker-compose.yml build
docker compose -f infrastructure/docker-compose.yml up -d

# Apply migrations against the running db container
docker compose -f infrastructure/docker-compose.yml exec backend \
  sh -c 'echo "run migrations via a one-off SDK container or a migration bundle"'
```
Migrations are not auto-applied on startup. Produce a migration bundle during build
(`dotnet ef migrations bundle`) or run `dotnet ef database update` from an SDK container
pointed at the production connection string, then verify:
```bash
curl -fk https://localhost/health     # {"status":"healthy","dependencies":{"db":"healthy","redis":"healthy"}}
```

## 5. Backups
- **Postgres:** scheduled `pg_dump` (daily full), retained off-host. Restore tested quarterly.
  ```bash
  docker compose -f infrastructure/docker-compose.yml exec db \
    pg_dump -U "$DB_USER" "$DB_NAME" | gzip > backup-$(date +%F).sql.gz
  ```
- **Redis:** AOF (`--appendonly yes`, already set) plus periodic RDB snapshots copied off-host.
  Redis holds cache/rate-limit/session data — it is not the source of truth, but back up the AOF for fast warm restarts.

## 6. Smoke test after deploy
- `GET https://<host>/health` → 200, db + redis healthy
- `GET https://<host>/scalar` → API docs render
- `GET https://<host>/` → SPA loads
- `GET http://<host>/` → 301 to https
- Security headers present (X-Frame-Options, X-Content-Type-Options, HSTS, …)
