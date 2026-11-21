import DetailRoute from "@/app/home/detail-route";

export const dynamic = "force-dynamic";

export default async function CategoryDetailPage({
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
      kind="category"
      path={`/home/categories/${id}`}
      searchParams={(await searchParams) ?? {}}
    />
  );
}
