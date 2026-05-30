declare module 'cytoscape-edgehandles' {
  import type { Core } from 'cytoscape';
  interface EdgeHandlesOptions {
    snap?: boolean;
    snapThreshold?: number;
    handleNodes?: string;
    complete?: (sourceNode: import('cytoscape').NodeSingular, targetNode: import('cytoscape').NodeSingular, addedEdge: import('cytoscape').EdgeSingular) => void;
    [key: string]: unknown;
  }
  interface EdgeHandlesInstance {
    enable(): void;
    disable(): void;
    destroy(): void;
  }
  // Registers the plugin; once registered, cy.edgehandles(options) returns the instance handle.
  function edgehandles(cy: Core, options?: EdgeHandlesOptions): EdgeHandlesInstance;
  namespace edgehandles {}
  export = edgehandles;
}

declare module 'cytoscape-dagre' {
  import type { Core } from 'cytoscape';
  function dagre(cy: Core): void;
  export = dagre;
}

declare module 'cytoscape-navigator' {
  import type { Core } from 'cytoscape';
  function navigator(cy: Core): void;
  export = navigator;
}
