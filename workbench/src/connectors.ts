/** Connector manifests, as the step settings on the canvas read them (operations, how a connector signs in). */

/** Credential types a person can paste (OAuth connectors sign in through the provider instead). */
export type ManualAuthType = 'ApiKey' | 'BearerToken' | 'BasicAuth' | 'ConnectionString' | 'HmacSecret';

/** Operation entry in a connector manifest. */
export interface ManifestOperation {
  id: string;
  name?: string;
  description?: string;
  requires_credential?: boolean;
  internal?: boolean;
  input_schema?: {
    required?: string[];
    properties?: Record<string, { description?: string; type?: string }>;
  };
}

export interface ConnectorManifest {
  auth?: { type?: string; credential_types?: string[] };
  operations?: ManifestOperation[];
  triggers?: string[];
}

export function parseManifest(connector: { manifestJson: string } | undefined): ConnectorManifest {
  if (!connector) return {};
  try {
    return JSON.parse(connector.manifestJson) as ConnectorManifest;
  } catch {
    return {};
  }
}

/** OAuth connectors are connected through the provider sign-in, not a pasted value. */
export function isOAuthConnector(manifest: ConnectorManifest): boolean {
  const type = manifest.auth?.type?.toLowerCase() ?? '';
  return type.startsWith('oauth') || (manifest.auth?.credential_types ?? []).includes('OAuth2AuthCode');
}

/** Credential types the user can paste for this connector (OAuth excluded). */
export function manualCredentialTypes(manifest: ConnectorManifest): ManualAuthType[] {
  const manual: ManualAuthType[] = ['ApiKey', 'BearerToken', 'BasicAuth', 'ConnectionString', 'HmacSecret'];
  return (manifest.auth?.credential_types ?? []).filter((t): t is ManualAuthType =>
    (manual as string[]).includes(t),
  );
}
