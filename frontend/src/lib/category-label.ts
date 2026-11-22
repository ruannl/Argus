type CategoryLabelSource = {
  display_name: string;
  kind: string;
  is_archived?: boolean;
};

/**
 * One label for category controls. The system Uncategorised category would
 * otherwise read "Uncategorised (Uncategorised)".
 */
export function formatCategoryLabel(category: CategoryLabelSource): string {
  const name =
    category.kind === "Uncategorised"
      ? category.display_name
      : `${category.display_name} (${category.kind})`;

  return category.is_archived ? `${name} — archived` : name;
}
