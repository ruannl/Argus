import { NextResponse } from "next/server";

import { auth } from "@/auth";

export default auth((request) => {
  if (!request.auth?.user) {
    return NextResponse.redirect(new URL("/login", request.url));
  }

  return NextResponse.next();
});

export const config = {
  matcher: ["/home", "/home/:path*", "/admin", "/admin/:path*"],
};
