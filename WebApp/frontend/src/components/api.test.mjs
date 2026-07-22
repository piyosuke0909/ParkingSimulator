import assert from "node:assert/strict";
import test from "node:test";

import { apiErrorMessage } from "./api.ts";

test("FastAPI detail is shown without raw JSON", () => {
  assert.equal(apiErrorMessage('{"detail":"案内可能なエリアがありません。"}', 409), "案内可能なエリアがありません。");
});

test("server failures get a user-facing fallback", () => {
  assert.equal(apiErrorMessage("", 500), "サーバーに接続できません。しばらくしてから再試行してください。");
});
