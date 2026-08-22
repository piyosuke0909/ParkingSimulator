export function unityBuildUrl(parameters: Record<string, string>): string {
  const query = new URLSearchParams(parameters);
  query.delete("apiKey");
  query.set("backendMode", "viewer");
  return `/unity-build/index.html?${query.toString()}`;
}
