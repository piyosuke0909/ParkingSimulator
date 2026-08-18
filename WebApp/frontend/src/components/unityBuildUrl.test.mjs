import assert from "node:assert/strict";
import test from "node:test";

import { unityBuildUrl } from "./admin/unityBuildUrl.ts";

test("admin Unity build URL is always viewer-only", () => {
  assert.equal(
    unityBuildUrl({ view: "admin-fit-16x10", embed: "admin" }),
    "/unity-build/index.html?view=admin-fit-16x10&embed=admin&backendMode=viewer"
  );
});

test("caller cannot override viewer-only mode", () => {
  assert.equal(
    unityBuildUrl({ view: "admin-fit-16x10", backendMode: "sender" }),
    "/unity-build/index.html?view=admin-fit-16x10&backendMode=viewer"
  );
});
