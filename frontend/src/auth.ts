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

// Auth.js host trust is explicit and fail-closed: only the literal value "true"
// enables it. When off (the default), Auth.js derives the origin from AUTH_URL
// and rejects requests whose Host header does not match.
function resolveTrustHost(): boolean {
  return process.env[AUTH_TRUST_HOST_NAME] === "true";
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
