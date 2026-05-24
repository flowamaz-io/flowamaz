# Security Standards

Security scan checklist for all ClaudeCode Foundation projects.
Security Agent runs at the end of every phase including fix phases.

---

## Scan Scope

Security Agent scans every file created or modified across all prompts in the phase.
Backend code, frontend code, mobile code, configuration files, docker files,
nginx config, migration files, seed files — everything.

Additionally runs Trivy and OWASP ZAP as defined below.

---

## Static Code Scan — Every Phase End

### CRITICAL Findings — Must Fix Before Next Phase

**Secrets and Credentials**
- [ ] Hardcoded passwords anywhere in code
- [ ] Hardcoded API keys or tokens anywhere in code
- [ ] Hardcoded database connection strings in code
- [ ] JWT secrets in appsettings or any committed file
- [ ] AWS credentials in code or committed config files
- [ ] Private keys committed to repository
- [ ] .env files committed (should be in .gitignore)

**Injection Vulnerabilities**
- [ ] Raw SQL string concatenation with user input
- [ ] LINQ expressions built from unvalidated user input
- [ ] Command injection via Process.Start with user input
- [ ] Path traversal in file operations using user input
- [ ] XML injection in any XML processing
- [ ] LDAP injection in any directory queries

**Authentication and Authorisation**
- [ ] Endpoints missing authentication attribute without explicit AllowAnonymous
- [ ] Admin or sensitive operations accessible without role check
- [ ] JWT validation not enforcing expiry
- [ ] JWT signature not validated
- [ ] Refresh token not invalidated on logout
- [ ] Password stored without hashing
- [ ] Weak hashing algorithm (MD5, SHA1) used for passwords

**Data Exposure**
- [ ] Stack traces returned to API client
- [ ] Internal error details exposed in API responses
- [ ] Database entity IDs exposed as sequential integers (use UUIDs)
- [ ] Sensitive fields (password_hash, tokens) returned in API responses
- [ ] PII logged in any log statement

**File Upload (if applicable)**
- [ ] File type not validated server-side
- [ ] No file size limit enforced
- [ ] Files stored in web-accessible path without auth
- [ ] Malicious file execution possible via upload

### WARNING Findings — Must Report

**API Security**
- [ ] Rate limiting missing on authentication endpoints
- [ ] Rate limiting missing on password reset endpoint
- [ ] Rate limiting missing on any public endpoint
- [ ] CORS configured with wildcard origin (*)
- [ ] HTTP methods not restricted
- [ ] Request size not limited

**Session and Token Management**
- [ ] Access token expiry longer than 15 minutes
- [ ] Refresh token expiry longer than 7 days without rotation
- [ ] No token revocation mechanism for logout
- [ ] Sensitive operations not requiring re-authentication

**Input Validation**
- [ ] API endpoint accepting input without validation attributes
- [ ] No maximum length on string inputs
- [ ] No validation on email format
- [ ] File path inputs not sanitised

### INFO Findings

- [ ] HTTP security headers not set (HSTS, X-Frame-Options, CSP etc)
- [ ] Sensitive operations missing audit log entries
- [ ] API versioning not implemented

---

## Trivy Scan — Every Phase End

Trivy scans for known CVEs in dependencies and Docker images.
Run Trivy automatically at every phase end regardless of what was built.

### What Trivy Scans
- Docker images defined in docker-compose.yml
- NuGet packages in backend .csproj files
- npm packages in web package.json
- Flutter pub packages in pubspec.yaml (if mobile layer active)

### Trivy Finding Classification

```
CRITICAL CVE  → report as Critical finding, must fix before next phase
HIGH CVE      → report as High finding, must fix before next phase
MEDIUM CVE    → report as Warning finding, travel forward
LOW CVE       → report as Info finding, travel forward
```

### Trivy Report Format

```
FINDING: CVE in [package name] [version]
FILE: [.csproj / package.json / pubspec.yaml / docker-compose.yml]
SEVERITY: Critical / High / Medium / Low
CVE: [CVE number]
DETAIL: [What the vulnerability is and attack vector]
REMEDIATION: Upgrade [package] to [minimum safe version]
REFERENCE: [CVE link]
```

---

## OWASP ZAP Scan — Conditional

OWASP ZAP performs dynamic API security testing against a running instance.
ZAP only runs if staging URL is declared in CLAUDE.md.
If no staging URL is declared, skip ZAP and note in security report.

### CLAUDE.md Staging URL Declaration
```
Staging URL: https://staging.yourproject.com
```

### What ZAP Tests
- Broken object level authorisation
- Broken authentication flows
- Mass assignment vulnerabilities
- Security misconfiguration in HTTP headers
- Injection via API parameters
- Sensitive data exposure in responses
- Rate limiting effectiveness

### ZAP Finding Format

```
FINDING: [Short description of dynamic vulnerability found]
ENDPOINT: [HTTP method + path e.g. POST /api/users]
SEVERITY: Critical / Warning / Info
DETAIL: [What ZAP found and why it is a risk]
REMEDIATION: [Specific fix required]
REFERENCE: [OWASP category]
```

---

## Security Report Structure

```
## Security Report — Phase [N]

Generated: [timestamp]
Trivy scan: COMPLETE
ZAP scan: COMPLETE / SKIPPED (no staging URL in CLAUDE.md)

### Summary
Static scan — Critical: X | Warning: X | Info: X
Trivy scan — Critical: X | High: X | Medium: X | Low: X
ZAP scan — Critical: X | Warning: X | Info: X (or SKIPPED)

### Critical Findings
[All Critical findings from static, Trivy and ZAP]

### Warning Findings
[All Warning / High findings]

### Info Findings
[All Info / Low / Medium findings]

### Priority Classification
Fix phase required (Critical): X issues
Fix phase required (High/Warning): X issues
Travel forward (Medium/Low/Info): X issues

### Phase Security Assessment
Cleared for next phase: YES / NO
Blockers: [list Critical findings that must resolve before proceeding]
Notes: [patterns or systemic issues observed]
```

---

## What Security Agent Does Not Do

- Does not fix issues — reports only
- Does not modify any files
- Does not exploit vulnerabilities
- Does not access external services beyond staging URL declared in CLAUDE.md
- Does not reduce severity based on assumptions — if uncertain, report higher
- Does not group multiple occurrences — each occurrence is a separate finding
- Does not approve a phase if any Critical findings exist
