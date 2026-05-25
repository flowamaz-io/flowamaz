// Loads every Phase 1 help article at startup (raw markdown) for the in-app help panel.
// Eager + raw so the bundle is self-contained and search works fully offline (FUNCTIONAL.md §10.1).

const rawModules = import.meta.glob('./articles/**/*.md', {
  query: '?raw',
  import: 'default',
  eager: true,
}) as Record<string, string>;

export interface HelpArticle {
  /** Slug without extension, e.g. "getting-started/cloud-signup". */
  slug: string;
  /** Article title (frontmatter `title`, falling back to the first H1). */
  title: string;
  /** Section path derived from the folder, e.g. "Getting Started". */
  section: string;
  /** Markdown body with the YAML frontmatter stripped. */
  content: string;
  /** Plan badge from frontmatter: "All plans" | "Cloud only" | "Enterprise". */
  plans?: string;
  /** Reading-time estimate from frontmatter, e.g. "3 min read". */
  readingTime?: string;
}

interface ParsedMarkdown {
  data: Record<string, string>;
  body: string;
}

/** Minimal YAML frontmatter parser — handles the flat string keys our articles use. */
function parseFrontmatter(md: string): ParsedMarkdown {
  const match = md.match(/^---\r?\n([\s\S]*?)\r?\n---\r?\n?/);
  if (!match) return { data: {}, body: md };
  const data: Record<string, string> = {};
  for (const line of match[1]!.split(/\r?\n/)) {
    const kv = line.match(/^([A-Za-z0-9_]+):\s*(.*)$/);
    if (kv) data[kv[1]!] = kv[2]!.trim().replace(/^["']|["']$/g, '');
  }
  return { data, body: md.slice(match[0].length) };
}

function titleFromMarkdown(md: string, fallback: string): string {
  const match = md.match(/^#\s+(.+)$/m);
  return match ? match[1]!.trim() : fallback;
}

const SECTION_LABELS: Record<string, string> = {
  'getting-started': 'Getting Started',
  workspaces: 'Organisations & Workspaces',
  billing: 'Plans & Billing',
};

function buildArticles(): Record<string, HelpArticle> {
  const out: Record<string, HelpArticle> = {};
  for (const [path, content] of Object.entries(rawModules)) {
    // path looks like "./articles/getting-started/cloud-signup.md"
    const slug = path.replace('./articles/', '').replace(/\.md$/, '');
    const folder = slug.split('/')[0] ?? '';
    const { data, body } = parseFrontmatter(content);
    out[slug] = {
      slug,
      title: data.title ?? titleFromMarkdown(body, slug),
      section: SECTION_LABELS[folder] ?? folder,
      content: body,
      plans: data.plans,
      readingTime: data.readingTime,
    };
  }
  return out;
}

export const articles: Record<string, HelpArticle> = buildArticles();

export function getArticle(slug: string): HelpArticle | null {
  return articles[slug] ?? null;
}

export function allArticles(): HelpArticle[] {
  return Object.values(articles);
}

/** Navigation tree grouped by section (mirrors FUNCTIONAL.md §10.9 content map). */
export interface HelpTreeSection {
  section: string;
  articles: HelpArticle[];
}

export function helpTree(): HelpTreeSection[] {
  const order = ['Getting Started', 'Organisations & Workspaces', 'Plans & Billing'];
  const grouped = new Map<string, HelpArticle[]>();
  for (const a of allArticles()) {
    const list = grouped.get(a.section) ?? [];
    list.push(a);
    grouped.set(a.section, list);
  }
  const sections: HelpTreeSection[] = [];
  for (const name of order) {
    const list = grouped.get(name);
    if (list) sections.push({ section: name, articles: list.sort((x, y) => x.title.localeCompare(y.title)) });
  }
  // Any unexpected sections appended at the end.
  for (const [name, list] of grouped) {
    if (!order.includes(name)) sections.push({ section: name, articles: list });
  }
  return sections;
}
