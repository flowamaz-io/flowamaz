import axios, { AxiosInstance } from "axios";

// A signed-in session on a self-hosted Flowamaz Community Edition install, for reading workflows out of it.
// Community signs in with email and password (a cookie session); the cookie lives only in memory for this command.

export const CLIENT_HEADER = { "X-Flowamaz-Client": "fmz-cli" };

export interface SourceWorkflow {
  id: string;
  name: string;
  slug: string;
  status: "Draft" | "Published" | "Archived";
  yamlContent: string;
  description: string | null;
}

export interface SourceVersion {
  id: string;
  isProduction: boolean;
  createdAt: string;
  tagName: string | null;
}

export interface SourceCredential {
  connectorId: string;
  name: string;
}

export interface CommunitySource {
  instanceName: string;
  listWorkflows(): Promise<SourceWorkflow[]>;
  listVersions(workflowId: string): Promise<SourceVersion[]>;
  versionYaml(workflowId: string, versionId: string): Promise<string>;
  listCredentials(): Promise<SourceCredential[]>;
}

/** Normalises the address people type ("flowamaz.local:3000") to an origin. */
export function sourceOrigin(url: string): string {
  const withScheme = /^https?:\/\//i.test(url) ? url : "http://" + url;
  const parsed = new URL(withScheme);
  return parsed.origin;
}

function cookieFrom(setCookie: string[] | undefined): string | null {
  const session = (setCookie ?? []).map((c) => c.split(";")[0]).find((c) => c.startsWith("flowamaz_session="));
  return session ?? null;
}

/** Signs in to the Community install at `url`. Throws with what to do when it can't. */
export async function connectCommunity(
  url: string,
  email: string,
  password: string,
  http: AxiosInstance = axios.create()
): Promise<CommunitySource> {
  const origin = sourceOrigin(url);
  const statusResp = await http.get(origin + "/api/community/status", { validateStatus: () => true });
  if (statusResp.status !== 200 || typeof statusResp.data?.setupNeeded !== "boolean") {
    throw new Error(
      origin + " doesn't look like a Flowamaz Community Edition install (no /api/community/status). Check the address in --from."
    );
  }
  if (statusResp.data.setupNeeded) {
    throw new Error(origin + " isn't set up yet, so there's nothing to migrate.");
  }

  const login = await http.post(
    origin + "/api/community/login",
    { email, password },
    { headers: CLIENT_HEADER, validateStatus: () => true }
  );
  if (login.status !== 200) {
    const message = typeof login.data?.message === "string" ? login.data.message : "HTTP " + login.status;
    throw new Error("Couldn't sign in to " + origin + ": " + message);
  }
  const cookie = cookieFrom(login.headers["set-cookie"] as string[] | undefined);
  if (!cookie) throw new Error("Signed in to " + origin + " but got no session back. Check that it's a Community Edition install.");

  const session = axios.create({ baseURL: origin, headers: { Cookie: cookie, ...CLIENT_HEADER } });
  const status = (await session.get("/api/community/status")).data as { workspaceId: string; instanceName: string };
  const ws = "/api/v1/workspaces/" + encodeURIComponent(status.workspaceId);

  return {
    instanceName: status.instanceName,
    async listWorkflows() {
      const all: SourceWorkflow[] = [];
      for (let page = 1; ; page++) {
        const resp = (await session.get(ws + "/workflows", { params: { page, pageSize: 100 } })).data as {
          items: { id: string }[];
          total: number;
        };
        for (const item of resp.items) {
          all.push((await session.get(ws + "/workflows/" + encodeURIComponent(item.id))).data as SourceWorkflow);
        }
        if (page * 100 >= resp.total || resp.items.length === 0) break;
      }
      return all;
    },
    async listVersions(workflowId) {
      return (await session.get(ws + "/workflows/" + encodeURIComponent(workflowId) + "/versions")).data as SourceVersion[];
    },
    async versionYaml(workflowId, versionId) {
      const resp = await session.get(
        ws + "/workflows/" + encodeURIComponent(workflowId) + "/versions/" + encodeURIComponent(versionId)
      );
      return (resp.data as { yamlContent: string }).yamlContent;
    },
    async listCredentials() {
      return (await session.get("/api/community/credentials")).data as SourceCredential[];
    },
  };
}
