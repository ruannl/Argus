import DetailRoute from "@/app/home/detail-route";

export const dynamic = "force-dynamic";

export default async function UntaggedDetailPage({
  searchParams,
}: {
  searchParams?: Promise<Record<string, string | string[] | undefined>>;
}) {
  return (
    <DetailRoute kind="untagged" path="/home/untagged" searchParams={(await searchParams) ?? {}} />
  );
}
