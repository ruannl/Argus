import DetailRoute from "@/app/home/detail-route";

export const dynamic = "force-dynamic";

export default async function ComparisonPage({
  searchParams,
}: {
  searchParams?: Promise<Record<string, string | string[] | undefined>>;
}) {
  return (
    <DetailRoute
      kind="comparison"
      path="/home/comparison"
      searchParams={(await searchParams) ?? {}}
    />
  );
}
