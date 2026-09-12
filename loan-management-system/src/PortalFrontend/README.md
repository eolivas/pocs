# Portal Frontend (React)

The **post-funding** borrower-facing self-service UI shell — used once the loan is funded and boarded (`LoanFunded` → `LoanBoarded`), after the customer has graduated from the [Application Frontend](../ApplicationFrontend). Talks **only to the Portal BFF** — never to a backend service directly. Follows the repo's React conventions ([steering `09-react-feature-module.md`](../../../.kiro/steering/09-react-feature-module.md)): feature modules under `frontend/src/features/`, TanStack Query for server state, a shared Axios `http` client, Zustand for client state.

## Stack

Vite 6 · React 18 · TypeScript 5.6 · TanStack Query 5 · Axios · Zustand.

## Structure

```
frontend/
  index.html
  package.json
  vite.config.ts            # dev server proxies /api -> Portal BFF (VITE_BFF_ORIGIN, default :5100)
  src/
    main.tsx                # QueryClientProvider root
    App.tsx
    lib/http.ts             # Axios instance, baseURL = VITE_API_BASE_URL (default /api)
    features/
      dashboard/            # sample feature: types, api hook, component
```

## Run

```
cd frontend
npm install
npm run dev      # http://localhost:5173, proxies /api to the BFF
npm run build    # tsc -b && vite build
```

## Notes

- `npm install` reports a few advisories in transitive dev dependencies (typical for a fresh Vite toolchain). For this POC scaffold that is acceptable; run `npm audit` before any production use.
- The dashboard feature reads the Portal BFF's placeholder `GET /api/portal/dashboard`. Real borrower views are a later deep-dive.

See the [root HLD](../../README.md) for the full design.
