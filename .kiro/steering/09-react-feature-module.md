---
inclusion: fileMatch
fileMatchPattern: "frontend/**"
---

# React Feature Module

All frontend features live in `frontend/src/features/{feature-name}/`. Follow this structure for new features.

## Feature Directory Structure

```
frontend/src/features/{feature-name}/
├── api/
│   └── index.ts         # TanStack Query hooks (useQuery, useMutation)
├── components/
│   ├── index.ts         # Barrel export for components
│   └── {Component}.tsx  # Feature-specific React components
├── hooks/
│   └── index.ts         # Feature-specific custom hooks
├── types.ts             # TypeScript interfaces and types
└── index.ts             # Feature barrel export
```

## Barrel Exports

Every directory needs an `index.ts` barrel file:

```typescript
// features/{name}/index.ts
export * from './types';
// Re-export components/hooks as needed for external use
```

## Types (`types.ts`)

Define all feature-specific interfaces:

```typescript
export interface {Entity}LineDto {
  productId: string;
  quantity: number;
  unitPrice: number;
  currency: string;
}

export interface {Entity}Dto {
  id: string;
  customerId: string;
  status: string;
  lines: {Entity}LineDto[];
  total: { amount: number; currency: string };
}

export interface Place{Entity}Request {
  customerId: string;
  lines: { productId: string; quantity: number; unitPrice: number; currency: string }[];
}
```

## API Layer (`api/index.ts`) — TanStack Query Hooks

```typescript
import { useQuery, useMutation } from '@tanstack/react-query';
import http from '../../../lib/http';
import type { {Entity}Dto, Place{Entity}Request } from '../types';

export function use{Entity}(id: string) {
  return useQuery<{Entity}Dto>({
    queryKey: ['{entities}', id],
    queryFn: async () => {
      const response = await http.get<{Entity}Dto>(`/{entities}/${id}`);
      return response.data;
    },
  });
}

export function usePlace{Entity}() {
  return useMutation<{Entity}Dto, Error, Place{Entity}Request>({
    mutationFn: async (request: Place{Entity}Request) => {
      const response = await http.post<{Entity}Dto>('/{entities}', request);
      return response.data;
    },
  });
}
```

Rules:
- Import `http` from `../../../lib/http` (Axios instance with auth interceptor)
- Use `useQuery` for reads, `useMutation` for writes
- Query keys: `['{resource}', id]` for single items, `['{resource}']` for lists
- Type parameters: `useQuery<TData>`, `useMutation<TData, TError, TVariables>`

## Components

```tsx
import { type FormEvent, useState } from 'react';
import { usePlace{Entity} } from '../api';
import type { Place{Entity}Request } from '../types';

export function Place{Entity}Form() {
  const mutation = usePlace{Entity}();
  // ... state and handlers

  return (
    <form onSubmit={handleSubmit}>
      {/* Use htmlFor on labels, id on inputs for accessibility */}
      <label htmlFor="customerId">Customer ID</label>
      <input id="customerId" type="text" required />
      {mutation.isError && <div role="alert">{mutation.error.message}</div>}
      <button type="submit" disabled={mutation.isPending}>Submit</button>
    </form>
  );
}
```

Rules:
- Named exports (no default exports)
- PascalCase component file names
- Use `mutation.isPending` / `mutation.isError` for loading/error states
- Accessibility: `htmlFor`, `role="alert"`, semantic HTML

## Zustand Stores

For feature-local state (not server state), use Zustand:

```typescript
import { create } from 'zustand';

interface CartState {
  items: CartItem[];
  addItem: (item: CartItem) => void;
  removeItem: (productId: string) => void;
  clear: () => void;
}

export const useCartStore = create<CartState>((set) => ({
  items: [],
  addItem: (item) => set((state) => ({ items: [...state.items, item] })),
  removeItem: (productId) =>
    set((state) => ({ items: state.items.filter((i) => i.productId !== productId) })),
  clear: () => set({ items: [] }),
}));
```

Rules:
- Place stores in `hooks/` or feature root
- Naming: `use{Feature}Store`
- Use `create<TState>` with typed interface
- Global stores (auth, theme) live in `src/lib/`

## Shared Libraries (`frontend/src/lib/`)

- `http.ts` — Axios instance with base URL from `VITE_API_BASE_URL` (default `/api`), auth token interceptor, 401 redirect to `/login`, network error detection
- `auth-store.ts` — Global Zustand store for JWT token
- `index.ts` — Barrel export

## Shared Components (`frontend/src/shared/`)

- `ErrorBoundary` — Class component at app root catching unhandled rendering errors. Displays fallback with `role="alert"` and reload button.
- `LoadingIndicator` — Renders a spinner with `aria-busy="true"` on the containing element for accessibility.

## Error Handling Patterns

### ProblemDetails Parsing (`useApiError` hook)

```typescript
import { useApiError } from '@/shared/hooks';

function MyForm() {
  const { error, fieldErrors, parseError } = useApiError();

  const handleSubmit = async () => {
    try { await api.post(...); }
    catch (e) { parseError(e); }
  };

  return (
    <>
      {error && <div role="alert">{error}</div>}
      {fieldErrors.quantity && <span>{fieldErrors.quantity}</span>}
    </>
  );
}
```

Rules:
- Parse `detail` field (or `title` fallback) for general error display
- Parse `errors` dictionary for per-field validation messages
- Network errors (no response): display "Unable to reach the server"
- HTTP 401: clear auth store, redirect to `/login`

### Environment Configuration

The API base URL is configurable per environment:
- `VITE_API_BASE_URL` env var at build time (via Vite)
- Default: `/api` (relative path for same-origin via nginx proxy)
- Frontend Dockerfile accepts `ARG VITE_API_BASE_URL` for Docker builds

## Tech Stack Reference

| Concern | Library | Version |
|---------|---------|---------|
| HTTP | Axios | ^1.x |
| Server state | @tanstack/react-query | ^5.x |
| Client state | Zustand | ^5.x |
| Build | Vite | ^6.x |
| Testing | Vitest + @testing-library/react | — |
| Type checking | TypeScript | ~5.6 |
