---
title: Add Your First Connector
updated: 2026-05-26
readingTime: "3 min read"
plans: "All plans"
---

# Add Your First Connector

Connectors let your workflows talk to external services — send a Slack message, query
a database, create a GitHub issue, or call an AI model.

## Browse the Library

1. Click **Library** in the left sidebar.
2. Use the **search bar** or category pills (Finance, Messaging, DevOps, …) to find the
   connector you need.
3. Click a card to see the connector's full description, operations, and auth type.

## Install a connector

1. On the connector card or detail page, click **Install** (or **Install & Connect**).
2. The Credential Setup Wizard opens. Follow the steps for your auth type:

   - **OAuth** — click "Connect with {Service}" and complete the popup flow.
   - **API Key** — paste your key from the service's developer settings.
   - **Basic Auth** — enter your username and password.

3. Give the credential a memorable name (default is "{Service} workspace").
4. Click **Done**. The connector is now installed and ready to use in the canvas.

## Use the connector in a workflow

- Open a workflow in the editor.
- Add an **Action** node.
- In the Node Inspector, select your connector and choose an operation.
- Fill in the input fields. Each field maps to a property in the operation's input schema.

## Manage installed connectors

- Go to **Library → Health** to see the status of every installed credential.
- Expired OAuth tokens show a **Reconnect** button — click it to re-authorise.

---

Was this helpful? 👍 👎

[Edit on GitHub ↗](https://github.com/flowamaz-io/docs/blob/main/docs/connectors/add-your-first-connector.md)
