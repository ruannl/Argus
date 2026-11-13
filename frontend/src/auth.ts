import "server-only";

import NextAuth, { type NextAuthConfig } from "next-auth";
import Credentials from "next-auth/providers/credentials";

import { PRODUCT_SLUG } from "@/lib/brand";

const AUTH_SECRET_NAME = "ARGUS_AUTH_SECRET";
const AUTH_TRUST_HOST_NAME = "ARGUS_AUTH_TRUST_HOST";
const SESSION_MAX_AGE_SECONDS = 12 * 60 * 60;
const MAX_BACKEND_TOKEN_BYTES = 1024;

function requiredAuthSecret(): string {
  const secret = process.env[AUTH_SECRET_NAME];
  if (!secret || new TextEncoder().encode(secret).byteLength < 32) {
    throw new Error(`${AUTH_SECRET_NAME} must be set to at least 32 bytes`);
  }
  return secret;
}

// Auth.js host trust is explicit. Only the literal value "true" trusts the
// request Host header; an explicit "false" (or a misspelt value) fails closed,
// rejecting every host. When the variable is unset, trust is left to Auth.js,
// which enables it only because the public URL below is pinned.
function resolveTrustHost(): boolean | undefined {
  const value = process.env[AUTH_TRUST_HOST_NAME];
  if (value === undefined || value === "") {
    return undefined;
  }

  return value === "true";
}

// Auth.js derives its public origin from AUTH_URL and rewrites every incoming
// request URL to that origin, so a spoofed Host header cannot redirect a
// callback or set a cookie for another host. The kit has one public origin in
// ARGUS_FRONTEND_URL; pin AUTH_URL to it unless an operator set one explicitly.
const publicFrontendOrigin = process.env.ARGUS_FRONTEND_URL;
if (publicFrontendOrigin && !process.env.AUTH_URL && !process.env.NEXTAUTH_URL) {
  process.env.AUTH_URL = publicFrontendOrigin;
}

function validBackendToken(value: unknown): value is string {
  return (
    typeof value === "string" &&
    value.length > 0 &&
    new TextEncoder().encode(value).byteLength <= MAX_BACKEND_TOKEN_BYTES
  );
}

export const authConfig = {
  secret: requiredAuthSecret(),
  trustHost: resolveTrustHost(),
  session: {
    strategy: "jwt",
    maxAge: SESSION_MAX_AGE_SECONDS,
  },
  cookies: {
    sessionToken: {
      name: `${PRODUCT_SLUG}-authjs.session-token`,
      options: {
        httpOnly: true,
        sameSite: "lax",
        path: "/",
        secure: process.env.NODE_ENV === "production",
      },
    },
  },
  pages: {
    signIn: "/login",
  },
  providers: [
    Credentials({
      credentials: {
        backendToken: { label: "Backend session", type: "text" },
        expiresAt: { label: "Session expiry", type: "text" },
      },
      async authorize(credentials) {
        if (!validBackendToken(credentials?.backendToken)) {
          return null;
        }

        return {
          id: "backend-session",
          backendToken: credentials.backendToken,
          expiresAt: typeof credentials.expiresAt === "string" ? credentials.expiresAt : "",
        };
      },
    }),
  ],
  callbacks: {
    async jwt({ user, token }) {
      if (!user) {
        return token;
      }

      return {
        backendToken: user.backendToken,
        expiresAt: user.expiresAt,
      };
    },
    async session({ session }) {
      return {
        expires: session.expires,
        authenticated: true,
      };
    },
  },
} satisfies NextAuthConfig;

export const { handlers, auth, signIn, signOut } = NextAuth(authConfig);
