export class ApiError extends Error {
  status: number;

  constructor(status: number, message: string) {
    super(message);
    this.name = "ApiError";
    this.status = status;
  }
}

export function apiErrorMessage(body: string, status: number): string {
  if (status >= 500) {
    return "サーバーに接続できません。しばらくしてから再試行してください。";
  }

  if (body) {
    try {
      const parsed = JSON.parse(body) as { detail?: unknown; message?: unknown };
      if (typeof parsed.detail === "string") {
        return parsed.detail;
      }
      if (typeof parsed.message === "string") {
        return parsed.message;
      }
    } catch {
      // Non-JSON response bodies may contain internal server details.
      // Fall through to a status-based user-facing message.
    }
  }

  if (status === 409) {
    return "案内状態が更新されています。最新情報を再読み込みしてください。";
  }
  return `データ取得に失敗しました（${status}）。`;
}

export async function fetchJson<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    ...init,
    headers: {
      "content-type": "application/json",
      ...(init?.headers ?? {})
    },
    cache: "no-store"
  });

  if (!response.ok) {
    const text = await response.text();
    throw new ApiError(response.status, apiErrorMessage(text, response.status));
  }

  return response.json() as Promise<T>;
}
