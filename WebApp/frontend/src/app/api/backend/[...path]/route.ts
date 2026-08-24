import { NextRequest } from "next/server";

const backendUrl = process.env.BACKEND_URL ?? "http://localhost:8000";
const backendApiKey = process.env.BACKEND_API_KEY ?? process.env.SMARTPARKING_LOCAL_API_KEY ?? "";

type RouteContext = {
  params: Promise<{ path: string[] }> | { path: string[] };
};

async function resolvePath(context: RouteContext) {
  const params = await context.params;
  return params.path.join("/");
}

async function proxy(request: NextRequest, context: RouteContext) {
  const path = await resolvePath(context);
  const incomingUrl = new URL(request.url);
  const targetUrl = `${backendUrl.replace(/\/$/, "")}/api/${path}${incomingUrl.search}`;
  const body = request.method === "GET" || request.method === "HEAD" ? undefined : await request.text();

  const response = await fetch(targetUrl, {
    method: request.method,
    headers: {
      "content-type": request.headers.get("content-type") ?? "application/json",
      ...(backendApiKey ? { "X-API-Key": backendApiKey } : {}),
      ...(request.headers.get("Idempotency-Key")
        ? { "Idempotency-Key": request.headers.get("Idempotency-Key") as string }
        : {})
    },
    body,
    cache: "no-store"
  });

  const responseBody = await response.text();
  return new Response(responseBody, {
    status: response.status,
    headers: {
      "content-type": response.headers.get("content-type") ?? "application/json"
    }
  });
}

export async function GET(request: NextRequest, context: RouteContext) {
  return proxy(request, context);
}

export async function POST(request: NextRequest, context: RouteContext) {
  return proxy(request, context);
}
