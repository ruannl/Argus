import { sortByDisplayName } from "@/lib/display-order";

type Named = { display_name: string };

/**
 * Option lists keep archived tags only while they still classify a
 * transaction. An archived tag with no transactions has nothing to filter or
 * remove, so it stays out of the dropdowns until it is restored in Settings.
 */
export function visibleTags<T extends Named & { is_archived: boolean; transaction_count: number }>(
  tags: readonly T[],
): T[] {
  return sortByDisplayName(tags.filter((tag) => !tag.is_archived || tag.transaction_count > 0));
}
