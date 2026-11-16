/** Slugify org / workspace names: lowercase, non-alphanumerics → single dash, trimmed. */
export function slugify(input: string): string {
  return input
    .toLowerCase()
    .replace(/[^a-z0-9]/g, '-')
    .replace(/-+/g, '-')
    .replace(/^-|-$/g, '');
}

/** Turns a machine id ("manager-approval", "send_email", "riskReview") into "Manager approval". */
export function humanizeId(id: string): string {
  const words = id
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/[-_.]+/g, ' ')
    .trim()
    .toLowerCase();
  return words.length > 0 ? words.charAt(0).toUpperCase() + words.slice(1) : id;
}
