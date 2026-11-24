# Arn

## Getting Started

You should have [Node.js](https://nodejs.org/en/) (>= 22) installed.

### Quick start

```bash
# Install dependencies
npm install

# Build anything that needs building
npm run build:mock

# Run mocked webapp
npm run dev:mock
```

That will start the mock server on `http://localhost:5173`.

### Mocked API

The mocked version uses [MSW](https://mswjs.io/) (Mock Service Worker) to intercept API calls in the browser. No real backend needed.

- `npm run build:mock` — compile MSW handlers (one-time)
- `npm run dev:mock` — start Vite dev server with MSW enabled
- `npm run build:server` — build the real backend
- `npm run dev:server` — start the real backend

