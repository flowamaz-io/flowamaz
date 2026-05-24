# Tech Stack Standards

All projects built under ClaudeCode Foundation follow these standards by default.
Deviations must be explicitly declared in the project CLAUDE.md.

---

## Backend — .NET

- Framework: Latest stable .NET (no preview or RC releases)
- Packages: Latest stable NuGet packages only — pre-release versions strictly prohibited
- ORM: Entity Framework Core — code-first approach
- Loading: Lazy loading enabled by default
- API Wrapping: AutoWrapper applied to all API responses without exception
- API Documentation: Scalar.AspNetCore — registered in Program.cs, accessible at /scalar
- Logging: Serilog — see serilog-standard.md for full rules
- Architecture: Clean architecture — Controllers, Services, Repositories, DTOs
- Validation: FluentValidation on all request DTOs
- Error Handling: Global exception middleware — no unhandled exceptions reach the client

### .NET Non-Negotiables
- No hardcoded connection strings, secrets or keys anywhere in code
- All configuration via environment variables or AWS Secrets Manager
- All endpoints protected by auth unless explicitly declared public
- RBAC enforced at service layer not just controller layer
- Async/await throughout — no blocking calls
- Cancellation tokens on all async operations
- Scalar /scalar endpoint always public — no auth required
- OpenAPI spec generated and wired to Scalar with project title and version

### .NET Testing Stack
- Unit tests: xUnit + Moq — fast isolated tests, no containers required
- Integration tests: xUnit + Testcontainers — real PostgreSQL, no mocking the database
- Coverage: Coverlet — minimum 80% on service layer, enforced in CI
- Load tests: k6 — critical API endpoints, runs at phase end

---

## Web — Vue 3

- Language: TypeScript strict mode — no any types
- Framework: Vue 3 Composition API only — no Options API
- Styling: Tailwind CSS v4 only
- Custom CSS: Zero style blocks in Vue components — hard rule, no exceptions
- Inline styles: Avoided unless Tailwind physically cannot achieve the result
- Icons: Allowed via icon libraries (Heroicons, Lucide)
- Component libraries: Tailwind-based libraries allowed (shadcn-vue, Headless UI)
- State: Pinia for global state
- Router: Vue Router 4
- HTTP: Axios with interceptors for auth token injection and error handling
- Forms: VeeValidate + Zod for schema validation

### Vue Non-Negotiables
- No style blocks under any circumstances
- All API calls go through a centralised API service layer
- No direct fetch or axios calls in components
- Environment variables via import.meta.env only
- All routes protected by auth guard unless declared public
- Loading, empty and error states designed for every data-fetching component

### Vue Testing Stack
- Unit + component tests: Vitest + Vue Test Utils — native to Vite, near-zero config
- API mocking: MSW (Mock Service Worker) — intercepts at network level, no live backend needed
- E2E: Playwright — critical user journeys, login, core flow, logout

---

## Mobile — Flutter

- Version: Latest stable Flutter SDK
- Language: Dart with strict null safety
- State: Riverpod or Bloc (declare in project CLAUDE.md)
- HTTP: Dio with interceptors
- Navigation: GoRouter
- Storage: flutter_secure_storage for sensitive data

### Flutter Non-Negotiables
- No hardcoded strings — all via localisation or constants file
- No platform-specific code without proper platform checks
- Null safety enforced throughout

### Flutter Testing Stack
- Unit + widget tests: flutter_test — bundled in SDK, zero setup
- Mocking: Mocktail — no code generation required, simpler than Mockito
- Integration tests: integration_test — official package, real device or emulator flows

---

## Database — PostgreSQL

- Version: Latest stable PostgreSQL
- Migrations: EF Core code-first migrations only — no manual SQL schema changes
- Naming: snake_case for all tables and columns
- Indexes: Every foreign key column indexed by default
- Soft deletes: is_deleted + deleted_at columns on all major entities
- Audit fields: created_at, updated_at, created_by, updated_by on all major entities
- No raw SQL unless EF Core genuinely cannot express the query

---

## Authentication and Authorisation — RBAC

- Approach: Database-stored roles and permissions
- Pattern: See rbac-pattern.md for table structure and middleware approach
- JWT: Access token short-lived (15 min), refresh token longer-lived (7 days)
- Roles and screen/function access: Defined per project in CLAUDE.md
- Password policy: Minimum 12 characters, uppercase, lowercase, number, special character
- Password hashing: BCrypt minimum cost factor 12

---

## Infrastructure — Docker + AWS

### Docker Compose Default Services
- backend: .NET API container
- web: Vue app served via Nginx container
- db: PostgreSQL container
- proxy: Nginx reverse proxy container
- Flutter: Builds separately, not containerised

### Hosting
- AWS Lightsail
- Ubuntu Latest LTS
- See docker-template/ for compose and nginx defaults

### Environment
- All secrets via environment variables
- .env files never committed — .gitignore enforced
- .env.example committed with all keys listed but no values

---

## Security Scanning

- Trivy: Docker image + NuGet + npm CVE scanning — runs at every phase end
- OWASP ZAP: Dynamic API security scan — runs at phase end if staging URL declared in CLAUDE.md
- Static analysis: Security Agent code review — runs at every phase end

---

## Testing Coverage Requirements

| Layer | Tool | Minimum Coverage |
|-------|------|-----------------|
| .NET service layer | Coverlet | 80% |
| Vue components | Vitest | 80% |
| Flutter business logic | flutter_test | 80% |

Coverage below threshold fails the phase. Testing Agent enforces this.

---

## Code Quality Non-Negotiables Across All Layers

- No commented-out code committed
- No TODO comments left unresolved at phase end
- No magic numbers — all constants named and centralised
- No console.log or Debug.WriteLine left in committed code
- All public methods have XML documentation comments (backend)
- All public Vue composables documented with JSDoc
