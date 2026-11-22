import ClearSession from "@/components/clear-session";

/** Fail-closed workspace fallback when the backend session is no longer valid. */
export default function WorkspaceExpired() {
  return (
    <main className="finance-workspace min-h-screen px-5 pt-5 pb-24 sm:px-8 lg:px-12">
      <div className="mx-auto max-w-3xl rounded-[2rem] border border-line bg-surface p-6 card-shadow sm:p-10">
        <ClearSession />
      </div>
    </main>
  );
}
