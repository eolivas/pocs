---
inclusion: fileMatch
fileMatchPattern: "**/Dockerfile,**/docker-compose*,**/.github/workflows/**"
---

# Docker & CI/CD Awareness

## Docker Compose Services

Local development uses `docker-compose.yml` with these services:

| Service | Image | Ports | Purpose |
|---------|-------|-------|---------|
| `{solution-name}-api` | Built from `src/{SolutionName}.Api/Dockerfile` | 5000:8080 | .NET 8+ API |
| `postgres` | `postgres:16` | 5432:5432 | PostgreSQL database |
| `rabbitmq` | `rabbitmq:3.13-management` | 5672, 15672 | Message broker |
| `frontend` | Built from `frontend/Dockerfile` | 3000:80 | React SPA (nginx, proxies /api) |
| `otel-collector` | `otel/opentelemetry-collector-contrib:0.96.0` | 4317 | OTLP receiver, forwards to Jaeger + Prometheus |
| `jaeger` | `jaegertracing/all-in-one:1.54` | 16686 | Distributed trace visualization |
| `prometheus` | `prom/prometheus:v2.50.0` | 9090 | Metrics query UI |

### Service Dependencies
- `{solution-name}-api` depends on `postgres` (healthy), `rabbitmq` (healthy), and `otel-collector` (healthy)
- `frontend` depends on `{solution-name}-api`

### Nginx Proxy
The frontend nginx config proxies `/api` requests to `{solution-name}-api:8080`:
```nginx
location /api/ {
    proxy_pass http://{solution-name}-api:8080/api/;
}
```

### Connection Strings (Development)
- PostgreSQL: `Host=postgres;Port=5432;Database={solution_name};Username=postgres;Password=postgres`
- RabbitMQ: `Host=rabbitmq;Username=guest;Password=guest`
- OTLP: Exporter sends to `http://otel-collector:4317`

### Adding a New Service
1. Add service definition to `docker-compose.yml`
2. Add health check
3. Wire dependencies with `depends_on: condition: service_healthy`
4. Expose ports only if needed for local development

## CI/CD Pipeline (`.github/workflows/ci.yml`)

### Backend Pipeline Stages

```
lint-and-test → build-and-push → deploy-staging → deploy-production
```

1. **Lint & Test** (all PRs and main pushes)
 - Restore → Build Release → Test with coverage
 - Publish test results (dorny/test-reporter)
 - Upload coverage to Codecov
 - **Coverage threshold: 80%** (fails CI if below)
 - SonarCloud analysis

2. **EF Migrations Validation** (all PRs)
 - PostgreSQL service container with health check (30s timeout)
 - Install `dotnet-ef` tool
 - `dotnet ef migrations has-pending-model-changes` — fails if model diverged from latest migration
 - `dotnet ef database update` against temp PostgreSQL — fails if migration can't apply cleanly

3. **Build & Push** (main branch only)
 - Docker multi-arch build (amd64 + arm64)
 - Push to Amazon ECR
 - Trivy vulnerability scan (CRITICAL/HIGH fails the build)
 - SARIF upload to GitHub Security

3. **Deploy Staging**
 - ECS Fargate deployment
 - Smoke test: `GET /health` with retries
 - Environment: `staging`

4. **Deploy Production**
 - ECS Fargate deployment
 - Git tag: `release-{short-sha}`
 - Environment: `production` (requires approval)

### Frontend Pipeline (`.github/workflows/frontend-ci.yml`)

```
lint → type-check → test → build → deploy (S3 + CloudFront)
```

- Triggers on pushes to `main` when `frontend/**` changes
- Node 20, npm ci
- Vitest with coverage
- Vite production build
- S3 sync + CloudFront invalidation

## Infrastructure

- **Cloud**: AWS
- **Compute**: ECS Fargate
- **Container Registry**: Amazon ECR
- **Database**: PostgreSQL (RDS in production, container locally)
- **Messaging**: RabbitMQ (Amazon MQ or self-managed in production)
- **Frontend Hosting**: S3 + CloudFront
- **Auth**: OIDC for CI/CD, JWT Bearer for API

## Key Environment Variables

| Variable | Where | Purpose |
|----------|-------|---------|
| `ConnectionStrings__{SolutionName}Db` | API container | PostgreSQL connection |
| `RabbitMq__Host/Username/Password` | API container | Message broker |
| `Jwt__Authority` | API container | OIDC provider URL |
| `Jwt__Audience` | API container | Expected JWT audience |
| `ASPNETCORE_ENVIRONMENT` | API container | Environment name |
| `Cors__AllowedOrigins__0` | API container | Allowed CORS origin |
| `RateLimit__PermitLimit` | API container | Rate limit per window |
| `RateLimit__WindowSeconds` | API container | Rate limit window duration |
| `Outbox__PollingIntervalSeconds` | API container | Outbox poll frequency |
| `VITE_API_BASE_URL` | Frontend build arg | API base URL for frontend |

## When Modifying Infrastructure

- Update `docker-compose.yml` for local dev changes
- Update `src/{SolutionName}.Api/Dockerfile` for API container changes
- Update `frontend/Dockerfile` for frontend container changes
- Update `.github/workflows/ci.yml` for backend CI/CD changes
- Update `.github/workflows/frontend-ci.yml` for frontend CI/CD changes
- New services need: Dockerfile, docker-compose entry, CI workflow updates, ECS task definition updates
