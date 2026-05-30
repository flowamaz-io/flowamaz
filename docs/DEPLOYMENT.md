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

### 1a. Production required-variable checklist (startup-enforced)

In any non-`Development` environment the API validates the following variables at startup and
**refuses to boot** with an actionable log message if any is missing. Set every one before deploying.

| Variable | Required | Description | How to generate / source |
|----------|----------|-------------|---------------------------|
| `DB_CONNECTION_STRING` | Yes | PostgreSQL connection string. In Docker Compose this is overridden to `Host=db;...`. | `Host=<host>;Port=5432;Database=<db>;Username=<user>;Password=<pwd>` (use your managed Postgres credentials). |
| `JWT_SECRET` | Yes | HMAC secret that signs access/refresh tokens (≥ 32 bytes). Rotating it invalidates all sessions. | `openssl rand -base64 48` |
| `CREDENTIAL_MASTER_KEY` | Yes | Master key for the per-workspace credential vault (AES-GCM, HMAC-derived per workspace). **Rotating it makes existing stored credentials undecryptable** — rotate only with a re-encryption plan. | `openssl rand -base64 32` |
| `GATE_SIGNING_KEY` | Yes | Signs human-gate approval/decline links so they can't be forged. | `openssl rand -hex 32` |
| `Email__ResendApiKey` | Yes | Resend API key for transactional email from `noreply@flowamaz.io`. Use the double-underscore form so it binds to the nested `Email:ResendApiKey` config section. | Resend dashboard → API Keys (value starts `re_`). |
| `Ai__AnthropicPlatformKey` | Yes | Platform-managed Anthropic API key (the only platform-managed AI provider). Powers F1–F7 platform AI functions. Use the double-underscore form to bind `Ai:AnthropicPlatformKey`. | Anthropic console → API Keys (value starts `sk-ant-`). |
| `REDIS_CONNECTION_STRING` | Yes (resolved) | Redis endpoint for task queue, session/semantic cache, and rate limiting. In Compose overridden to `redis:6379`. | `<host>:6379` (add `,password=<pwd>` if your Redis requires auth). |
| `CORS_ALLOWED_ORIGINS` | Recommended | Comma-separated SPA origins. If unset in Production all cross-origin requests are blocked (a warning is logged). | e.g. `https://app.flowamaz.io` |
| `GIT_REPOS_BASE_PATH` | Recommended | Writable directory for workflow Git versioning. Startup probes it for write access and fails if not writable. Defaults to `bin/git-repos`. | Any persistent, writable volume path. |

> The six variables marked Required in the first six rows are the exact set enforced by the startup
> gate (`DB_CONNECTION_STRING`, `JWT_SECRET`, `CREDENTIAL_MASTER_KEY`, `GATE_SIGNING_KEY`,
> `Email__ResendApiKey`, `Ai__AnthropicPlatformKey`). A missing value yields a log line naming the
> variable and how to generate it, then the process exits.

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

---

## CI/CD Secrets (GitHub Actions)

Set under repo Settings → Secrets and variables → Actions:

| Secret | Purpose | Required |
|--------|---------|----------|
| `NPM_TOKEN` | Publish `@flowamaz/cli` to npm on release | Release only |
| `CODECOV_TOKEN` | Upload backend coverage (optional — step is non-blocking) | Optional |
| `GITHUB_TOKEN` | GHCR image push — **auto-provided** by Actions | Automatic |

### Triggering a release

A release runs automatically on merge to `main`. To cut a version, tag the merge commit:

```bash
git tag v0.5.0 && git push origin v0.5.0
```

`release.yml` reads the latest tag via `git describe --tags` and tags the GHCR images with it plus
`latest`. To run a release manually, push to `main` (the workflow has no `workflow_dispatch`; add one
if ad-hoc runs are needed).
