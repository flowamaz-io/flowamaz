# RBAC Pattern

Base authentication and authorisation pattern for all ClaudeCode Foundation projects.
Roles and screen/function access are defined per project in CLAUDE.md.

---

## Core Approach

- Roles stored in database — not hardcoded in code
- Permissions defined as screen and function access
- JWT for stateless authentication
- Middleware enforces auth at API gateway level
- Service layer enforces RBAC — not just controller level

---

## Base Table Structure

```sql
-- Users
users
  id              uuid primary key
  email           varchar(255) unique not null
  password_hash   varchar(255) not null
  is_active       boolean default true
  created_at      timestamptz default now()
  updated_at      timestamptz default now()
  created_by      uuid references users(id)
  updated_by      uuid references users(id)
  is_deleted      boolean default false
  deleted_at      timestamptz

-- Roles
roles
  id              uuid primary key
  name            varchar(100) unique not null
  description     varchar(255)
  is_active       boolean default true
  created_at      timestamptz default now()
  updated_at      timestamptz default now()

-- User Roles (many to many)
user_roles
  id              uuid primary key
  user_id         uuid references users(id)
  role_id         uuid references roles(id)
  assigned_at     timestamptz default now()
  assigned_by     uuid references users(id)

-- Permissions
permissions
  id              uuid primary key
  name            varchar(100) unique not null
  description     varchar(255)
  module          varchar(100)

-- Role Permissions (many to many)
role_permissions
  id              uuid primary key
  role_id         uuid references roles(id)
  permission_id   uuid references permissions(id)
```

---

## JWT Configuration

```json
{
  "JwtSettings": {
    "AccessTokenExpiryMinutes": 15,
    "RefreshTokenExpiryDays": 7,
    "Issuer": "project-name-api",
    "Audience": "project-name-client"
  }
}
```

- Secret via environment variable only — never in appsettings
- Access token payload includes: userId, email, roles, permissions
- Refresh token stored in database with expiry and revocation support

---

## Middleware Pattern

```csharp
// Applied globally in Program.cs
app.UseAuthentication();
app.UseAuthorization();

// Controller level — require authenticated user
[Authorize]

// Specific permission check
[Authorize(Policy = "PermissionName")]

// Public endpoint — explicitly declared
[AllowAnonymous]
```

---

## Service Layer Enforcement

RBAC is enforced at service layer in addition to controller.
A user reaching a service method they are not authorised for throws UnauthorisedException.
This prevents bypasses via internal service-to-service calls.

```csharp
public async Task<Result> DeleteUserAsync(Guid userId, ClaimsPrincipal currentUser)
{
    _logger.LogInformation("Entering {FunctionName}", nameof(DeleteUserAsync));

    if (!currentUser.HasPermission("users.delete"))
        throw new UnauthorisedException("Insufficient permissions to delete users");

    // proceed with operation
}
```

---

## Project-Specific Configuration

Each project defines in CLAUDE.md:

```
RBAC Roles:
  - RoleName: Description of role
  - RoleName: Description of role

RBAC Permissions per Role:
  RoleName:
    - module.action (e.g. users.read, users.create, reports.export)
    - module.action
  RoleName:
    - module.action
```

Executor reads this from CLAUDE.md and seeds the database via EF Core data seeding.

---

## Password Policy

- Minimum 12 characters
- Must contain uppercase, lowercase, number, special character
- BCrypt hashing — minimum cost factor 12
- Password reset via time-limited token (1 hour expiry)
- Account lockout after 5 failed attempts — 15 minute lockout

---

## Verifier RBAC Checklist

- [ ] All endpoints have explicit Authorize or AllowAnonymous attribute
- [ ] Service layer enforces permission check on sensitive operations
- [ ] JWT secret sourced from environment variable only
- [ ] Refresh token stored in database with revocation support
- [ ] Password hashed with BCrypt cost factor minimum 12
- [ ] Account lockout implemented on auth endpoint
- [ ] RBAC roles and permissions seeded from CLAUDE.md definition
