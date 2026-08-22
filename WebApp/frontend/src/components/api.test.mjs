import assert from "node:assert/strict";
import test from "node:test";

import { apiErrorMessage } from "./api.ts";

test("FastAPI detail is shown without raw JSON", () => {
  assert.equal(apiErrorMessage('{"detail":"案内可能なエリアがありません。"}', 409), "案内可能なエリアがありません。");
});

test("server failures get a user-facing fallback", () => {
  assert.equal(apiErrorMessage("", 500), "サーバーに接続できません。しばらくしてから再試行してください。");
});

test("server failures never expose raw response bodies", () => {
  assert.equal(
    apiErrorMessage("Traceback: internal database connection details", 500),
    "サーバーに接続できません。しばらくしてから再試行してください。"
  );
  assert.equal(
    apiErrorMessage('{"detail":"internal stack trace"}', 503),
    "サーバーに接続できません。しばらくしてから再試行してください。"
  );
});

test("P3 backend error message is shown from the direct error envelope", () => {
  assert.equal(
    apiErrorMessage('{"error":{"code":"COMMAND_TARGET_UNAVAILABLE","message":"UnityのCommand接続がありません。"}}', 409),
    "UnityのCommand接続がありません。"
  );
});

test("FastAPI dependency error message is shown from detail.error", () => {
  assert.equal(
    apiErrorMessage('{"detail":{"error":{"code":"INVALID_API_KEY","message":"API Keyが無効です。"}}}', 401),
    "API Keyが無効です。"
  );
});
