import { afterEach, describe, expect, it, vi } from "vitest";
import { deepLinkToAppUrl } from "../../../desktop/src/routing";

// Exercise the real Supabase SDK with the web platform used by browsers and Electron.
vi.mock("react-native-url-polyfill/auto", () => ({}));
vi.mock("@react-native-async-storage/async-storage", () => ({ default: {} }));
vi.mock("react-native", () => ({ Platform: { OS: "web" }, AppState: {} }));

const storageKey = "sb-security-test-auth-token";
const user = (id: string) => ({
  id, aud: "authenticated", role: "authenticated", email: `${id}@example.test`,
  app_metadata: {}, user_metadata: {}, created_at: "2026-01-01T00:00:00Z",
});
const session = (id: string) => ({
  access_token: `${id}-access-token`, refresh_token: `${id}-refresh-token`,
  expires_in: 3600, expires_at: Math.floor(Date.now() / 1000) + 3600,
  token_type: "bearer", user: user(id),
});

function setup(href: string, signedIn: boolean) {
  vi.resetModules();
  vi.stubEnv("EXPO_PUBLIC_SUPABASE_URL", "https://security-test.supabase.co");
  vi.stubEnv("EXPO_PUBLIC_SUPABASE_ANON_KEY", "test-anon-key");
  const stored = new Map<string, string>();
  if (signedIn) stored.set(storageKey, JSON.stringify(session("victim")));
  vi.stubGlobal("localStorage", {
    getItem: (key: string) => stored.get(key) ?? null,
    setItem: (key: string, value: string) => { stored.set(key, value); },
    removeItem: (key: string) => { stored.delete(key); },
  });
  vi.stubGlobal("window", {
    location: new URL(href), addEventListener: vi.fn(), removeEventListener: vi.fn(),
  });
  vi.stubGlobal("document", { visibilityState: "hidden" });
  vi.stubGlobal("BroadcastChannel", undefined);
  const fetchMock = vi.fn(async (input: string | URL | Request) => {
    const url = String(input);
    // Accept the attacker's credentials, so rejection cannot be attributed to invalid tokens.
    if (url.endsWith("/auth/v1/user")) return Response.json(user("attacker"));
    if (url.endsWith("/auth/v1/token?grant_type=password")) return Response.json(session("victim"));
    throw new Error(`Unexpected request: ${url}`);
  });
  vi.stubGlobal("fetch", fetchMock);
  return { stored, fetchMock };
}

afterEach(() => {
  vi.unstubAllGlobals();
  vi.unstubAllEnvs();
});

describe("unsolicited URL credentials", () => {
  for (const desktop of [false, true]) {
    for (const separator of ["#", "?"]) {
      it.each([false, true])(
        `ignores ${desktop ? "desktop deep-link" : "web"} ${separator} tokens (stored session: %s)`,
        async (signedIn) => {
          const tokens = new URLSearchParams({
            access_token: "attacker-access-token", refresh_token: "attacker-refresh-token",
            expires_in: "3600", token_type: "bearer",
          });
          const href = desktop
            ? deepLinkToAppUrl(`hfn://auth/callback${separator}${tokens}`)!
            : `https://app.example.test/${separator}${tokens}`;
          const { stored, fetchMock } = setup(href, signedIn);
          const { supabase } = await import("./supabase");
          try {
            const { data, error } = await supabase.auth.getSession();
            expect(error).toBeNull();
            expect(data.session?.user.id ?? null).toBe(signedIn ? "victim" : null);
            expect(stored.has(storageKey)).toBe(signedIn);
            if (signedIn) {
              expect(JSON.parse(stored.get(storageKey)!).user.id).toBe("victim");
            }
            expect(fetchMock).not.toHaveBeenCalled();
          } finally {
            await supabase.auth.stopAutoRefresh();
          }
        },
      );
    }
  }

  it("still signs in with a password and persists the session", async () => {
    const { stored, fetchMock } = setup("https://app.example.test/sign-in", false);
    const { supabase } = await import("./supabase");
    try {
      const { data, error } = await supabase.auth.signInWithPassword({
        email: "victim@example.test", password: "test-password",
      });
      expect(error).toBeNull();
      expect(data.session?.user.id).toBe("victim");
      expect(JSON.parse(stored.get(storageKey)!).user.id).toBe("victim");
      expect(fetchMock).toHaveBeenCalledTimes(1);
    } finally {
      await supabase.auth.stopAutoRefresh();
    }
  });
});
