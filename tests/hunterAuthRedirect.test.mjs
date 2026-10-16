import test from "node:test";
import assert from "node:assert/strict";
import { hunterAuthRedirectUrl, hunterAuthReturnError } from "../lib/hunterAuthRedirect.ts";

test("production callbacks never inherit localhost or Vercel preview origins", () => {
  assert.equal(hunterAuthRedirectUrl("/license"), "https://projectprime.gg/license");
  assert.equal(hunterAuthRedirectUrl("/maps/account"), "https://projectprime.gg/maps/account");
});
test("local callbacks require an explicit selection and destination allowlist", () => {
  assert.equal(hunterAuthRedirectUrl("/license", "http://localhost:3000"),
    "http://localhost:3000/license");
  assert.equal(hunterAuthRedirectUrl("/maps/account", "http://127.0.0.1:3000"),
    "http://127.0.0.1:3000/maps/account");
  assert.throws(() => hunterAuthRedirectUrl("/license", "http://projectprime.gg"));
  assert.throws(() => hunterAuthRedirectUrl("/license", "https://projectprime.gg/other"));
  assert.throws(() => hunterAuthRedirectUrl("/license", "https://user:pass@projectprime.gg"));
  assert.throws(() => hunterAuthRedirectUrl("//evil.example", "https://projectprime.gg"));
});
test("link errors do not echo upstream fragments or tokens", () => {
  assert.equal(hunterAuthReturnError(""), null);
  assert.equal(hunterAuthReturnError("#access_token=secret"), null);
  assert.match(hunterAuthReturnError("#error=access_denied&error_code=otp_expired") || "", /expired/);
  const message = hunterAuthReturnError("#error=access_denied&error_description=secret") || "";
  assert.match(message, /verification/);
  assert.equal(message.includes("secret"), false);
});
