import type { NextConfig } from "next";

// Security headers applied to every route. CSP notes:
// - `script-src 'unsafe-inline'` is required by Next.js for its hydration
//   bootstrap scripts unless a nonce pipeline is added (proxy/middleware);
//   upgrading to nonces is the documented production hardening step.
// - `connect-src 'self'`: browsers talk only to the Next.js origin; the
//   ASP.NET Core backend is called server-side, never from the browser.
// - `script-src 'unsafe-eval'` is added in development only: React's dev build
//   uses eval() for debugging features such as callstack reconstruction, and
//   Next.js dev tooling relies on it too. Production never needs it.
const isDevelopment = process.env.NODE_ENV === "development";
const scriptSrc = isDevelopment
  ? "script-src 'self' 'unsafe-inline' 'unsafe-eval'"
  : "script-src 'self' 'unsafe-inline'";

const securityHeaders = [
  {
    key: "Content-Security-Policy",
    value: [
      "default-src 'self'",
      scriptSrc,
      "style-src 'self' 'unsafe-inline'",
      "img-src 'self' data:",
      "font-src 'self'",
      "connect-src 'self'",
      "object-src 'none'",
      "base-uri 'self'",
      "form-action 'self'",
      "frame-ancestors 'none'",
    ].join("; "),
  },
  { key: "Strict-Transport-Security", value: "max-age=63072000; includeSubDomains" },
  { key: "X-Content-Type-Options", value: "nosniff" },
  { key: "X-Frame-Options", value: "DENY" },
  { key: "Referrer-Policy", value: "strict-origin-when-cross-origin" },
];

const nextConfig: NextConfig = {
  // Privacy: Next.js captures Server Function invocation arguments in
  // development before the action authenticates or validates them, then
  // prints them on success or failure. Private search text must never reach
  // framework logs, so disable the development invocation lines globally.
  // Incoming-request logging, fetch/browser logging and backend audit events
  // are separate and unchanged. Removing this setting while private Server
  // Actions remain reachable reopens the disclosure.
  logging: {
    serverFunctions: false,
  },
  async headers() {
    return [{ source: "/(.*)", headers: securityHeaders }];
  },
};

export default nextConfig;
