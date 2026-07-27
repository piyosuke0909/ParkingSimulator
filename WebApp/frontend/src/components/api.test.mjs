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
