# Synthetic finance presentation checks

Run both suites from `frontend/`:

```sh
npm run test:e2e
npm run test:e2e:finance
```

The existing suite checks public pages, unauthenticated redirects, security
headers and theme persistence. The separate finance suite starts a production
Next.js frontend on loopback port 3210 and a synthetic REST fixture on 3211.
Occupied ports fail rather than reusing an unknown server. It uses the real
frontend sign-in, cookies, server-side REST client, server actions and components.
All identities, passwords, tokens and financial records are test-only inventions.
The test servers set their own backend origin, application secret and auth secret
explicitly. Next may also load an ignored local `.env` for unrelated settings, but
the fixture remains the only backend and no customer credentials, database or real
backend is used. The fixture is not a substitute for backend authorization,
financial or persistence tests.

The finance suite checks both themes and 320/375/1440 px screens, all five
Settings sections, transaction list/detail/drawer, Imports upload/reviews/history,
review blockers and statement deletion disclosure. It also exercises representative
loading, empty, partial, success, error, changed-version, confirmation and recovered
bulk states; real keyboard tab/drawer behaviour; Chromium's table accessibility
tree; sampled computed contrast; desktop/touch targets; reduced transparency and
forced colours; and long-value/root-font enlargement/text-spacing reflow.

The workspace shell remains in place while the content region scrolls. New
destinations start at their content top; a new history entry restores its own
position on Back/Forward; local tab/filter `replaceState` updates do not reset
the position. The suite verifies all four destinations as real Next client
transitions, same-route query history entries, clamping when the viewport grows,
and release of finance-only document scrolling on logout. At an unusually short
or enlarged-text viewport, the header itself becomes scrollable and is capped at
60% of the viewport so content retains its own scroll area. Focused targets remain
visible and the document does not scroll. The scroll store contains only numeric
offsets, never search text, account identifiers or financial data.

The dark material follows the accepted illustration: a faint finance-only ambient
gradient, glass shell and summary panels, near-opaque work cards and table headers,
fine upper-edge highlights and rectangular status badges. Reduced-transparency and
forced-colour modes remove the gradient and blur and restore opaque surfaces.

Screenshots and failure traces go into the ignored `test-results/` directory.
Optional `PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH` selects an already-installed browser.
No dependency or browser is installed by these commands. The fixture uses Node's
built-in TypeScript stripping (Node 22.18+). Initial local evidence used Node
26.10.0 and Chromium 1252; other engines, an assistive-technology session, browser
zoom, real backend mutations and low-powered hardware remain separate checks.
