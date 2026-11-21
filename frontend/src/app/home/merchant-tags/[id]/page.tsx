import DetailRoute from "@/app/home/detail-route";

export const dynamic = "force-dynamic";

export default async function MerchantTagDetailPage({
  params,
  searchParams,
}: {
  params: Promise<{ id: string }>;
  searchParams?: Promise<Record<string, string | string[] | undefined>>;
}) {
  const { id } = await params;
  return (
    <DetailRoute
      id={id}
      kind="merchant_tag"
      path={`/home/merchant-tags/${id}`}
      searchParams={(await searchParams) ?? {}}
    />
  );
}
