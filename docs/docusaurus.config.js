// @ts-check
// Docusaurus configuration for docs.flowamaz.io (FUNCTIONAL.md §10.7).

/** @type {import('@docusaurus/types').Config} */
const config = {
  title: 'Flowamaz Docs',
  tagline: 'AI-native workflow orchestration',
  favicon: 'img/flowamaz-logo.svg',

  url: 'https://docs.flowamaz.io',
  baseUrl: '/',

  organizationName: 'flowamaz-io',
  projectName: 'docs',

  onBrokenLinks: 'throw',

  markdown: {
    hooks: {
      onBrokenMarkdownLinks: 'throw',
    },
  },

  i18n: {
    defaultLocale: 'en',
    locales: ['en'],
  },

  presets: [
    [
      '@docusaurus/preset-classic',
      /** @type {import('@docusaurus/preset-classic').Options} */
      ({
        docs: {
          routeBasePath: '/',
          sidebarPath: require.resolve('./sidebars.js'),
          editUrl: 'https://github.com/flowamaz-io/docs/blob/main/docs/',
        },
        blog: false,
        theme: {
          customCss: require.resolve('./src/css/custom.css'),
        },
      }),
    ],
  ],

  themeConfig:
    /** @type {import('@docusaurus/preset-classic').ThemeConfig} */
    ({
      navbar: {
        title: 'Flowamaz Docs',
        logo: {
          alt: 'Flowamaz',
          src: 'img/flowamaz-logo.svg',
        },
        items: [
          {
            type: 'docSidebar',
            sidebarId: 'docsSidebar',
            position: 'left',
            label: 'Documentation',
          },
          {
            href: 'https://app.flowamaz.io',
            label: 'Open App',
            position: 'right',
          },
          {
            href: 'https://github.com/flowamaz-io',
            label: 'GitHub',
            position: 'right',
          },
        ],
      },
      footer: {
        style: 'dark',
        links: [
          {
            title: 'Flowamaz',
            items: [
              { label: 'Website', href: 'https://flowamaz.com' },
              { label: 'App', href: 'https://app.flowamaz.io' },
            ],
          },
          {
            title: 'Community',
            items: [{ label: 'GitHub', href: 'https://github.com/flowamaz-io' }],
          },
          {
            title: 'Contact',
            items: [{ label: 'Security', href: 'mailto:security@flowamaz.io' }],
          },
        ],
        copyright: `Copyright © ${new Date().getFullYear()} Flowamaz.`,
      },
    }),
};

module.exports = config;
