import "@testing-library/jest-dom/vitest";
import { cleanup } from "@testing-library/react";
import { afterAll, afterEach, beforeAll, vi } from "vitest";

import { server } from "./tests/mocks/server";

const runtimeMocks = vi.hoisted(() => ({
  cookieStore: {
    get: vi.fn(),
    set: vi.fn(),
    delete: vi.fn(),
  },
  redirect: vi.fn(),
  router: {
    push: vi.fn(),
    replace: vi.fn(),
    refresh: vi.fn(),
    back: vi.fn(),
    forward: vi.fn(),
    prefetch: vi.fn(),
  },
}));

export const testCookieStore = runtimeMocks.cookieStore;
export const testRedirect = runtimeMocks.redirect;
export const testRouter = runtimeMocks.router;

vi.mock("server-only", () => ({}));

vi.mock("next/headers", () => ({
  cookies: vi.fn(async () => runtimeMocks.cookieStore),
}));

vi.mock("next/navigation", () => ({
  redirect: runtimeMocks.redirect,
  useRouter: () => runtimeMocks.router,
}));

vi.mock("next/server", () => ({
  NextRequest: Request,
  NextResponse: {
    json: (body: unknown, init?: ResponseInit) => Response.json(body, init),
  },
}));

vi.mock("next/font/google", () => ({
  Inter: () => ({ variable: "" }),
}));

beforeAll(() => {
  server.listen({ onUnhandledRequest: "error" });
});

afterEach(() => {
  cleanup();
  server.resetHandlers();
  testCookieStore.get.mockReset();
  testCookieStore.set.mockReset();
  testCookieStore.delete.mockReset();
  testRedirect.mockReset();
  testRouter.push.mockReset();
  testRouter.replace.mockReset();
  testRouter.refresh.mockReset();
  testRouter.back.mockReset();
  testRouter.forward.mockReset();
  testRouter.prefetch.mockReset();
});

afterAll(() => {
  server.close();
});
