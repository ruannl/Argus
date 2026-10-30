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
}));

export const testCookieStore = runtimeMocks.cookieStore;
export const testRedirect = runtimeMocks.redirect;

vi.mock("server-only", () => ({}));

vi.mock("next/headers", () => ({
  cookies: vi.fn(async () => runtimeMocks.cookieStore),
}));

vi.mock("next/navigation", () => ({
  redirect: runtimeMocks.redirect,
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
});

afterAll(() => {
  server.close();
});
