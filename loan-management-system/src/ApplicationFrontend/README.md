# Application Frontend (Originations Frontend)

The **pre-funding** customer-facing UI: where a prospective borrower, arriving through a partner channel, applies for a loan, sees the credit decision, and reviews/signs the contract. Talks **only to the Application BFF** — never to a backend service directly.

Once the loan is **funded and boarded** (`LoanFunded` → `LoanBoarded`), the customer graduates from this Application experience to the [Portal Frontend](../PortalFrontend) (post-funding self-service).

## Stack

Vite 6 · React 18 · TypeScript 5.6 · TanStack Query 5 · Axios · Zustand (per [steering `09`](../../../.kiro/steering/09-react-feature-module.md)).

## Structure

```
frontend/
  index.html
  package.json
  vite.config.ts            # port 5174; dev proxies /api -> Application BFF (VITE_BFF_ORIGIN, default :5101)
  src/
    main.tsx                # QueryClientProvider root
    App.tsx
    lib/http.ts             # Axios instance, baseURL = VITE_API_BASE_URL (default /api)
    features/
      application/          # sample feature: types, api hook, status panel
```

## Run

```
cd frontend
npm install
npm run dev      # http://localhost:5174, proxies /api to the Application BFF
npm run build    # tsc -b && vite build
```

The status panel reads the Application BFF's placeholder `GET /api/application/status`. Real application intake, decision display, and signing are a later deep-dive.

See the [root HLD](../../README.md) for the full design.
