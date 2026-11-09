type Named = { display_name: string };

/**
 * Sorts categories, tags, and similar named records alphabetically by display
 * name. Returns a copy so backend order is never mutated.
 */
export function sortByDisplayName<T extends Named>(items: readonly T[]): T[] {
  return [...items].sort((left, right) =>
    left.display_name.localeCompare(right.display_name, "en", { sensitivity: "base" }),
  );
}
