# Flowamaz Docs

The source for [docs.flowamaz.io](https://docs.flowamaz.io), built with
[Docusaurus 3](https://docusaurus.io/). This is the single source of truth for Flowamaz
documentation; the in-app help panel renders the same Markdown articles.

> Note: `DEVELOPMENT.md` and `DEPLOYMENT.md` in this folder are project engineering docs
> and are not part of the Docusaurus site (the site only builds from `docs/docs/`).

## Structure

```
docs/
  docusaurus.config.js   site config (title, navbar, footer, edit URL)
  sidebars.js            sidebar / content map
  package.json           Docusaurus 3.x dependencies
  docs/                  the published articles
    getting-started/     6 articles
    workspaces/          5 articles
    billing/             4 articles
  static/img/            logo and assets
```

The 15 articles here mirror `web/src/help/articles/**` exactly.

## Local development

```bash
npm install      # install Docusaurus
npm run start    # dev server with hot reload
npm run build    # production build into build/
```

## Editing

Every article has an "Edit on GitHub" link pointing at
`https://github.com/flowamaz-io/docs/blob/main/docs/`. Community contributions are
welcome — the docs repo is licensed CC BY 4.0.
