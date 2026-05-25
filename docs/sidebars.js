// @ts-check
// Sidebar for the Flowamaz docs site. Mirrors the in-app help content map.

/** @type {import('@docusaurus/plugin-content-docs').SidebarsConfig} */
const sidebars = {
  docsSidebar: [
    {
      type: 'category',
      label: 'Getting Started',
      items: [
        'getting-started/what-is-flowamaz',
        'getting-started/install-community-edition',
        'getting-started/cloud-signup',
        'getting-started/plans-comparison',
        'getting-started/migrate-community-to-cloud',
        'getting-started/key-concepts-glossary',
      ],
    },
    {
      type: 'category',
      label: 'Organisations & Workspaces',
      items: [
        'workspaces/what-is-a-workspace',
        'workspaces/invite-team-members',
        'workspaces/roles-and-permissions',
        'workspaces/api-keys',
        'workspaces/environments',
      ],
    },
    {
      type: 'category',
      label: 'Plans & Billing',
      items: [
        'billing/community-vs-cloud',
        'billing/upgrade-guide',
        'billing/understanding-your-invoice',
        'billing/enterprise-self-hosted',
      ],
    },
  ],
};

module.exports = sidebars;
