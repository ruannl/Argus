import { describe, expect, it } from "vitest";

import nextConfig from "../next.config";

const logging = nextConfig.logging as
  | {
      serverFunctions?: boolean;
      incomingRequests?: { ignore?: RegExp[] };
    }
  | undefined;

const ignore = logging?.incomingRequests?.ignore ?? [];
const ignored = (url: string) => ignore.some((pattern) => pattern.test(url));

describe("development request logging", () => {
  it("keeps Server Function argument logging disabled", () => {
    expect(logging?.serverFunctions).toBe(false);
  });

  it("suppresses every recovery link that carries a one-time token", () => {
    expect(ignored("/reset?token=abc123")).toBe(true);
    expect(ignored("/verify?token=abc123")).toBe(true);
    expect(ignored("http://localhost:3000/reset?token=abc123")).toBe(true);
    expect(ignored("http://localhost:3000/verify?token=abc123")).toBe(true);
  });

  it("keeps ordinary navigation and lookalike paths visible", () => {
    expect(ignored("/home")).toBe(false);
    expect(ignored("/reset-account")).toBe(false);
    expect(ignored("/settings?tab=categories")).toBe(false);
  });
});
