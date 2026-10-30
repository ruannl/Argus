import type { DefaultSession } from "next-auth";

declare module "next-auth" {
  interface Session {
    authenticated: true;
    user?: DefaultSession["user"];
  }

  interface User {
    backendToken?: string;
    expiresAt?: string;
  }
}

declare module "next-auth/jwt" {
  interface JWT {
    backendToken?: string;
    expiresAt?: string;
  }
}
