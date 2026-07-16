export function UnityView({ unityBuildAvailable }: { unityBuildAvailable: boolean | null }) {
  return (
    <section className="adminPanel">
      <div className="adminPanelHeader">
        <div>
          <h2>Unity WebGL</h2>
          <p>表示用 iframe です。常時 snapshot 送信は start-dev の runner が担当します。</p>
        </div>
        <span className={`statusPill ${unityBuildAvailable ? "live" : "stale"}`}>{unityBuildAvailable ? "buildあり" : "buildなし"}</span>
      </div>
      {unityBuildAvailable ? (
        <iframe src="/unity-build/index.html?view=admin-fit-16x10" title="Unity WebGL" className="adminUnityFrame" />
      ) : (
        <div className="adminEmpty">Unity WebGL build がありません。</div>
      )}
    </section>
  );
}
