using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flowamaz.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UpdateOfficialConnectorManifests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "connector_definitions",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000001"),
                columns: new[] { "manifest_json", "version" },
                values: new object[] { "{\"id\":\"http-rest\",\"version\":\"1.1.0\",\"auth\":{\"type\":\"optional\",\"credential_types\":[\"BearerToken\",\"ApiKey\",\"BasicAuth\"]},\"triggers\":[],\"operations\":[{\"id\":\"GET\",\"name\":\"HTTP GET\",\"description\":\"Send an HTTP GET request\",\"requires_credential\":false,\"internal\":false,\"input_schema\":{\"type\":\"object\",\"required\":[\"url\"],\"properties\":{\"url\":{\"type\":\"string\",\"description\":\"Full https URL to call\"},\"headers\":{\"type\":\"object\",\"description\":\"Extra request headers\"},\"body\":{\"type\":\"string\",\"description\":\"Request body (JSON text)\"},\"timeout_seconds\":{\"type\":\"integer\",\"description\":\"Timeout in seconds (default 30, max 120)\"},\"api_key_header\":{\"type\":\"string\",\"description\":\"Header for an ApiKey credential (default X-API-Key)\"}}}},{\"id\":\"POST\",\"name\":\"HTTP POST\",\"description\":\"Send an HTTP POST request\",\"requires_credential\":false,\"internal\":false,\"input_schema\":{\"type\":\"object\",\"required\":[\"url\"],\"properties\":{\"url\":{\"type\":\"string\",\"description\":\"Full https URL to call\"},\"headers\":{\"type\":\"object\",\"description\":\"Extra request headers\"},\"body\":{\"type\":\"string\",\"description\":\"Request body (JSON text)\"},\"timeout_seconds\":{\"type\":\"integer\",\"description\":\"Timeout in seconds (default 30, max 120)\"},\"api_key_header\":{\"type\":\"string\",\"description\":\"Header for an ApiKey credential (default X-API-Key)\"}}}},{\"id\":\"PUT\",\"name\":\"HTTP PUT\",\"description\":\"Send an HTTP PUT request\",\"requires_credential\":false,\"internal\":false,\"input_schema\":{\"type\":\"object\",\"required\":[\"url\"],\"properties\":{\"url\":{\"type\":\"string\",\"description\":\"Full https URL to call\"},\"headers\":{\"type\":\"object\",\"description\":\"Extra request headers\"},\"body\":{\"type\":\"string\",\"description\":\"Request body (JSON text)\"},\"timeout_seconds\":{\"type\":\"integer\",\"description\":\"Timeout in seconds (default 30, max 120)\"},\"api_key_header\":{\"type\":\"string\",\"description\":\"Header for an ApiKey credential (default X-API-Key)\"}}}},{\"id\":\"PATCH\",\"name\":\"HTTP PATCH\",\"description\":\"Send an HTTP PATCH request\",\"requires_credential\":false,\"internal\":false,\"input_schema\":{\"type\":\"object\",\"required\":[\"url\"],\"properties\":{\"url\":{\"type\":\"string\",\"description\":\"Full https URL to call\"},\"headers\":{\"type\":\"object\",\"description\":\"Extra request headers\"},\"body\":{\"type\":\"string\",\"description\":\"Request body (JSON text)\"},\"timeout_seconds\":{\"type\":\"integer\",\"description\":\"Timeout in seconds (default 30, max 120)\"},\"api_key_header\":{\"type\":\"string\",\"description\":\"Header for an ApiKey credential (default X-API-Key)\"}}}},{\"id\":\"DELETE\",\"name\":\"HTTP DELETE\",\"description\":\"Send an HTTP DELETE request\",\"requires_credential\":false,\"internal\":false,\"input_schema\":{\"type\":\"object\",\"required\":[\"url\"],\"properties\":{\"url\":{\"type\":\"string\",\"description\":\"Full https URL to call\"},\"headers\":{\"type\":\"object\",\"description\":\"Extra request headers\"},\"body\":{\"type\":\"string\",\"description\":\"Request body (JSON text)\"},\"timeout_seconds\":{\"type\":\"integer\",\"description\":\"Timeout in seconds (default 30, max 120)\"},\"api_key_header\":{\"type\":\"string\",\"description\":\"Header for an ApiKey credential (default X-API-Key)\"}}}},{\"id\":\"HEAD\",\"name\":\"HTTP HEAD\",\"description\":\"Send an HTTP HEAD request\",\"requires_credential\":false,\"internal\":false,\"input_schema\":{\"type\":\"object\",\"required\":[\"url\"],\"properties\":{\"url\":{\"type\":\"string\",\"description\":\"Full https URL to call\"},\"headers\":{\"type\":\"object\",\"description\":\"Extra request headers\"},\"body\":{\"type\":\"string\",\"description\":\"Request body (JSON text)\"},\"timeout_seconds\":{\"type\":\"integer\",\"description\":\"Timeout in seconds (default 30, max 120)\"},\"api_key_header\":{\"type\":\"string\",\"description\":\"Header for an ApiKey credential (default X-API-Key)\"}}}}]}", "1.1.0" });

            migrationBuilder.UpdateData(
                table: "connector_definitions",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000002"),
                columns: new[] { "manifest_json", "version" },
                values: new object[] { "{\"id\":\"postgresql\",\"version\":\"1.1.0\",\"auth\":{\"type\":\"connection-string\",\"credential_types\":[\"ConnectionString\"]},\"triggers\":[],\"operations\":[{\"id\":\"query\",\"name\":\"Run query\",\"description\":\"Run a SELECT and return rows\",\"requires_credential\":true,\"internal\":false,\"input_schema\":{\"type\":\"object\",\"required\":[\"sql\"],\"properties\":{\"sql\":{\"type\":\"string\",\"description\":\"SQL with @named parameters \\u2014 never concatenate values into the SQL\"},\"parameters\":{\"type\":\"object\",\"description\":\"Parameter values by name\"}}}},{\"id\":\"execute\",\"name\":\"Execute statement\",\"description\":\"Run INSERT/UPDATE/DELETE and return affected rows\",\"requires_credential\":true,\"internal\":false,\"input_schema\":{\"type\":\"object\",\"required\":[\"sql\"],\"properties\":{\"sql\":{\"type\":\"string\",\"description\":\"SQL with @named parameters \\u2014 never concatenate values into the SQL\"},\"parameters\":{\"type\":\"object\",\"description\":\"Parameter values by name\"}}}}]}", "1.1.0" });

            migrationBuilder.UpdateData(
                table: "connector_definitions",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000003"),
                columns: new[] { "manifest_json", "version" },
                values: new object[] { "{\"id\":\"mysql\",\"version\":\"1.1.0\",\"auth\":{\"type\":\"connection-string\",\"credential_types\":[\"ConnectionString\"]},\"triggers\":[],\"operations\":[{\"id\":\"query\",\"name\":\"Run query\",\"description\":\"Run a SELECT and return rows\",\"requires_credential\":true,\"internal\":false,\"input_schema\":{\"type\":\"object\",\"required\":[\"sql\"],\"properties\":{\"sql\":{\"type\":\"string\",\"description\":\"SQL with @named parameters \\u2014 never concatenate values into the SQL\"},\"parameters\":{\"type\":\"object\",\"description\":\"Parameter values by name\"}}}},{\"id\":\"execute\",\"name\":\"Execute statement\",\"description\":\"Run INSERT/UPDATE/DELETE and return affected rows\",\"requires_credential\":true,\"internal\":false,\"input_schema\":{\"type\":\"object\",\"required\":[\"sql\"],\"properties\":{\"sql\":{\"type\":\"string\",\"description\":\"SQL with @named parameters \\u2014 never concatenate values into the SQL\"},\"parameters\":{\"type\":\"object\",\"description\":\"Parameter values by name\"}}}}]}", "1.1.0" });

            migrationBuilder.UpdateData(
                table: "connector_definitions",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000004"),
                columns: new[] { "manifest_json", "version" },
                values: new object[] { "{\"id\":\"slack\",\"version\":\"1.1.0\",\"auth\":{\"type\":\"oauth2-auth-code\",\"credential_types\":[\"OAuth2AuthCode\",\"BearerToken\"]},\"triggers\":[],\"operations\":[{\"id\":\"send-message\",\"name\":\"Send message\",\"description\":\"Post a message to a channel\",\"requires_credential\":true,\"internal\":false,\"input_schema\":{\"type\":\"object\",\"required\":[\"channel\",\"text\"],\"properties\":{\"channel\":{\"type\":\"string\",\"description\":\"Channel name or id, e.g. #approvals\"},\"text\":{\"type\":\"string\",\"description\":\"Message text\"},\"blocks\":{\"type\":\"array\",\"description\":\"Optional Block Kit blocks\"}}}},{\"id\":\"send-dm\",\"name\":\"Send direct message\",\"description\":\"Send a direct message to a user\",\"requires_credential\":true,\"internal\":false,\"input_schema\":{\"type\":\"object\",\"required\":[\"user_id\",\"text\"],\"properties\":{\"user_id\":{\"type\":\"string\",\"description\":\"Slack user id, e.g. U0123\"},\"text\":{\"type\":\"string\",\"description\":\"Message text\"}}}},{\"id\":\"post-approval-message\",\"name\":\"Post approval message\",\"description\":\"Approval card for a human gate\",\"requires_credential\":true,\"internal\":true,\"input_schema\":{\"type\":\"object\",\"required\":[\"channel\",\"gate_decision_id\"],\"properties\":{\"channel\":{\"type\":\"string\",\"description\":\"Channel\"},\"gate_decision_id\":{\"type\":\"string\",\"description\":\"Gate id\"},\"workflow_name\":{\"type\":\"string\",\"description\":\"Workflow\"},\"gate_summary\":{\"type\":\"string\",\"description\":\"Summary\"}}}}]}", "1.1.0" });

            migrationBuilder.UpdateData(
                table: "connector_definitions",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000005"),
                columns: new[] { "manifest_json", "version" },
                values: new object[] { "{\"id\":\"microsoft-teams\",\"version\":\"1.1.0\",\"auth\":{\"type\":\"oauth2-auth-code\",\"credential_types\":[\"OAuth2AuthCode\"]},\"triggers\":[],\"operations\":[{\"id\":\"send-message\",\"name\":\"Send channel message\",\"description\":\"Post a message to a Teams channel (Microsoft Graph)\",\"requires_credential\":true,\"internal\":false,\"input_schema\":{\"type\":\"object\",\"required\":[\"team_id\",\"channel_id\",\"text\"],\"properties\":{\"team_id\":{\"type\":\"string\",\"description\":\"Team id\"},\"channel_id\":{\"type\":\"string\",\"description\":\"Channel id\"},\"text\":{\"type\":\"string\",\"description\":\"Message text (HTML allowed)\"}}}},{\"id\":\"send-webhook-message\",\"name\":\"Send via incoming webhook\",\"description\":\"Post to a channel through an incoming-webhook URL\",\"requires_credential\":false,\"internal\":false,\"input_schema\":{\"type\":\"object\",\"required\":[\"webhook_url\",\"text\"],\"properties\":{\"webhook_url\":{\"type\":\"string\",\"description\":\"Incoming webhook URL\"},\"text\":{\"type\":\"string\",\"description\":\"Message text\"}}}},{\"id\":\"post-adaptive-card\",\"name\":\"Post approval card\",\"description\":\"Adaptive Card for a human gate\",\"requires_credential\":false,\"internal\":true,\"input_schema\":{\"type\":\"object\",\"required\":[\"webhook_url\",\"gate_id\"],\"properties\":{\"webhook_url\":{\"type\":\"string\",\"description\":\"Webhook\"},\"gate_id\":{\"type\":\"string\",\"description\":\"Gate id\"},\"gate_summary\":{\"type\":\"string\",\"description\":\"Summary\"},\"platform_url\":{\"type\":\"string\",\"description\":\"Platform URL\"}}}}]}", "1.1.0" });

            migrationBuilder.UpdateData(
                table: "connector_definitions",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000006"),
                columns: new[] { "manifest_json", "version" },
                values: new object[] { "{\"id\":\"email-smtp\",\"version\":\"1.1.0\",\"auth\":{\"type\":\"platform\",\"credential_types\":[]},\"triggers\":[],\"operations\":[{\"id\":\"send-email\",\"name\":\"Send email\",\"description\":\"Send a transactional email from the platform sender\",\"requires_credential\":false,\"internal\":false,\"input_schema\":{\"type\":\"object\",\"required\":[\"to\",\"subject\",\"body\"],\"properties\":{\"to\":{\"type\":\"string\",\"description\":\"Recipient address\"},\"subject\":{\"type\":\"string\",\"description\":\"Subject\"},\"body\":{\"type\":\"string\",\"description\":\"Body\"},\"is_html\":{\"type\":\"boolean\",\"description\":\"Body is HTML (default false)\"}}}}]}", "1.1.0" });

            migrationBuilder.UpdateData(
                table: "connector_definitions",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000007"),
                columns: new[] { "manifest_json", "version" },
                values: new object[] { "{\"id\":\"webhook-emit\",\"version\":\"1.1.0\",\"auth\":{\"type\":\"hmac\",\"credential_types\":[\"HmacSecret\"]},\"triggers\":[],\"operations\":[{\"id\":\"send-webhook\",\"name\":\"Send webhook\",\"description\":\"POST a JSON payload, HMAC-signed when a credential is set\",\"requires_credential\":false,\"internal\":false,\"input_schema\":{\"type\":\"object\",\"required\":[\"url\",\"payload\"],\"properties\":{\"url\":{\"type\":\"string\",\"description\":\"Destination https URL\"},\"payload\":{\"type\":\"object\",\"description\":\"JSON payload\"}}}}]}", "1.1.0" });

            migrationBuilder.UpdateData(
                table: "connector_definitions",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000008"),
                columns: new[] { "manifest_json", "version" },
                values: new object[] { "{\"id\":\"webhook-receive\",\"version\":\"1.1.0\",\"auth\":{\"type\":\"hmac\",\"credential_types\":[\"HmacSecret\"]},\"triggers\":[\"webhook\"],\"operations\":[]}", "1.1.0" });

            migrationBuilder.UpdateData(
                table: "connector_definitions",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000009"),
                columns: new[] { "manifest_json", "version" },
                values: new object[] { "{\"id\":\"file-system\",\"version\":\"1.1.0\",\"auth\":{\"type\":\"none\",\"credential_types\":[]},\"triggers\":[],\"operations\":[{\"id\":\"read-file\",\"name\":\"Read file\",\"description\":\"Read a text file from the workspace file area\",\"requires_credential\":false,\"internal\":false,\"input_schema\":{\"type\":\"object\",\"required\":[\"path\"],\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"Path relative to the workspace file area\"}}}},{\"id\":\"write-file\",\"name\":\"Write file\",\"description\":\"Write a text file to the workspace file area\",\"requires_credential\":false,\"internal\":false,\"input_schema\":{\"type\":\"object\",\"required\":[\"path\",\"content\"],\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"Path relative to the workspace file area\"},\"content\":{\"type\":\"string\",\"description\":\"Text content\"},\"overwrite\":{\"type\":\"boolean\",\"description\":\"Replace an existing file (default false)\"}}}},{\"id\":\"list-files\",\"name\":\"List files\",\"description\":\"List files in a folder of the workspace file area\",\"requires_credential\":false,\"internal\":false,\"input_schema\":{\"type\":\"object\",\"required\":[],\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"Folder (default: root)\"}}}}]}", "1.1.0" });

            migrationBuilder.UpdateData(
                table: "connector_definitions",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000010"),
                columns: new[] { "manifest_json", "version" },
                values: new object[] { "{\"id\":\"schedule-cron\",\"version\":\"1.1.0\",\"auth\":{\"type\":\"none\",\"credential_types\":[]},\"triggers\":[\"schedule\"],\"operations\":[{\"id\":\"validate-cron\",\"name\":\"Validate cron\",\"description\":\"Check a cron expression and return its next run times\",\"requires_credential\":false,\"internal\":false,\"input_schema\":{\"type\":\"object\",\"required\":[\"expression\"],\"properties\":{\"expression\":{\"type\":\"string\",\"description\":\"Cron expression (5 or 6 fields)\"},\"timezone\":{\"type\":\"string\",\"description\":\"IANA time zone (default UTC)\"}}}}]}", "1.1.0" });

            migrationBuilder.UpdateData(
                table: "connector_definitions",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000011"),
                columns: new[] { "manifest_json", "version" },
                values: new object[] { "{\"id\":\"script-shell\",\"version\":\"1.1.0\",\"auth\":{\"type\":\"none\",\"credential_types\":[]},\"triggers\":[],\"operations\":[{\"id\":\"run-script\",\"name\":\"Run script\",\"description\":\"Run a bash or python script (self-hosted only; disabled on Cloud)\",\"requires_credential\":false,\"internal\":false,\"input_schema\":{\"type\":\"object\",\"required\":[\"interpreter\",\"script\"],\"properties\":{\"interpreter\":{\"type\":\"string\",\"description\":\"bash or python3\"},\"script\":{\"type\":\"string\",\"description\":\"Script text (passed on stdin, never through a shell string)\"},\"args\":{\"type\":\"array\",\"description\":\"Arguments\"},\"timeout_seconds\":{\"type\":\"integer\",\"description\":\"Timeout (default 30, max 300)\"}}}}]}", "1.1.0" });

            migrationBuilder.UpdateData(
                table: "connector_definitions",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000012"),
                columns: new[] { "manifest_json", "version" },
                values: new object[] { "{\"id\":\"github\",\"version\":\"1.1.0\",\"auth\":{\"type\":\"oauth2-auth-code\",\"credential_types\":[\"OAuth2AuthCode\",\"BearerToken\"]},\"triggers\":[],\"operations\":[{\"id\":\"create-issue\",\"name\":\"Create issue\",\"description\":\"Open an issue\",\"requires_credential\":true,\"internal\":false,\"input_schema\":{\"type\":\"object\",\"required\":[\"owner\",\"repo\",\"title\"],\"properties\":{\"owner\":{\"type\":\"string\",\"description\":\"Owner\"},\"repo\":{\"type\":\"string\",\"description\":\"Repository\"},\"title\":{\"type\":\"string\",\"description\":\"Title\"},\"body\":{\"type\":\"string\",\"description\":\"Body\"},\"labels\":{\"type\":\"array\",\"description\":\"Labels\"}}}},{\"id\":\"get-issue\",\"name\":\"Get issue\",\"description\":\"Fetch an issue\",\"requires_credential\":true,\"internal\":false,\"input_schema\":{\"type\":\"object\",\"required\":[\"owner\",\"repo\",\"issue_number\"],\"properties\":{\"owner\":{\"type\":\"string\",\"description\":\"Owner\"},\"repo\":{\"type\":\"string\",\"description\":\"Repository\"},\"issue_number\":{\"type\":\"integer\",\"description\":\"Issue number\"}}}},{\"id\":\"create-pr\",\"name\":\"Create pull request\",\"description\":\"Open a pull request\",\"requires_credential\":true,\"internal\":false,\"input_schema\":{\"type\":\"object\",\"required\":[\"owner\",\"repo\",\"title\",\"head\",\"base\"],\"properties\":{\"owner\":{\"type\":\"string\",\"description\":\"Owner\"},\"repo\":{\"type\":\"string\",\"description\":\"Repository\"},\"title\":{\"type\":\"string\",\"description\":\"Title\"},\"head\":{\"type\":\"string\",\"description\":\"Source branch\"},\"base\":{\"type\":\"string\",\"description\":\"Target branch\"},\"body\":{\"type\":\"string\",\"description\":\"Description\"}}}},{\"id\":\"add-comment\",\"name\":\"Add comment\",\"description\":\"Comment on an issue or pull request\",\"requires_credential\":true,\"internal\":false,\"input_schema\":{\"type\":\"object\",\"required\":[\"owner\",\"repo\",\"issue_number\",\"body\"],\"properties\":{\"owner\":{\"type\":\"string\",\"description\":\"Owner\"},\"repo\":{\"type\":\"string\",\"description\":\"Repository\"},\"issue_number\":{\"type\":\"integer\",\"description\":\"Issue or PR number\"},\"body\":{\"type\":\"string\",\"description\":\"Comment\"}}}}]}", "1.1.0" });

            migrationBuilder.UpdateData(
                table: "connector_definitions",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000013"),
                columns: new[] { "manifest_json", "version" },
                values: new object[] { "{\"id\":\"microsoft-365\",\"version\":\"1.1.0\",\"auth\":{\"type\":\"oauth2-auth-code\",\"credential_types\":[\"OAuth2AuthCode\"]},\"triggers\":[],\"operations\":[{\"id\":\"send-mail\",\"name\":\"Send email\",\"description\":\"Send email from the connected mailbox (Microsoft Graph)\",\"requires_credential\":true,\"internal\":false,\"input_schema\":{\"type\":\"object\",\"required\":[\"to\",\"subject\",\"body\"],\"properties\":{\"to\":{\"type\":\"string\",\"description\":\"Recipients, comma-separated\"},\"subject\":{\"type\":\"string\",\"description\":\"Subject\"},\"body\":{\"type\":\"string\",\"description\":\"Body\"},\"is_html\":{\"type\":\"boolean\",\"description\":\"Body is HTML (default false)\"}}}},{\"id\":\"create-calendar-event\",\"name\":\"Create calendar event\",\"description\":\"Create an event in the connected calendar\",\"requires_credential\":true,\"internal\":false,\"input_schema\":{\"type\":\"object\",\"required\":[\"subject\",\"start\",\"end\"],\"properties\":{\"subject\":{\"type\":\"string\",\"description\":\"Title\"},\"start\":{\"type\":\"string\",\"description\":\"Start, ISO 8601\"},\"end\":{\"type\":\"string\",\"description\":\"End, ISO 8601\"},\"timezone\":{\"type\":\"string\",\"description\":\"IANA or Windows time zone (default UTC)\"},\"attendees\":{\"type\":\"string\",\"description\":\"Attendee emails, comma-separated\"},\"body\":{\"type\":\"string\",\"description\":\"Description\"}}}},{\"id\":\"upload-file\",\"name\":\"Upload file to OneDrive\",\"description\":\"Create or replace a text file in the connected OneDrive\",\"requires_credential\":true,\"internal\":false,\"input_schema\":{\"type\":\"object\",\"required\":[\"path\",\"content\"],\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"OneDrive path, e.g. Reports/summary.txt\"},\"content\":{\"type\":\"string\",\"description\":\"Text content\"}}}}]}", "1.1.0" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "connector_definitions",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000001"),
                columns: new[] { "manifest_json", "version" },
                values: new object[] { "{\"id\":\"http-rest\",\"version\":\"1.0.0\",\"operations\":[]}", "1.0.0" });

            migrationBuilder.UpdateData(
                table: "connector_definitions",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000002"),
                columns: new[] { "manifest_json", "version" },
                values: new object[] { "{\"id\":\"postgresql\",\"version\":\"1.0.0\",\"operations\":[]}", "1.0.0" });

            migrationBuilder.UpdateData(
                table: "connector_definitions",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000003"),
                columns: new[] { "manifest_json", "version" },
                values: new object[] { "{\"id\":\"mysql\",\"version\":\"1.0.0\",\"operations\":[]}", "1.0.0" });

            migrationBuilder.UpdateData(
                table: "connector_definitions",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000004"),
                columns: new[] { "manifest_json", "version" },
                values: new object[] { "{\"id\":\"slack\",\"version\":\"1.0.0\",\"operations\":[]}", "1.0.0" });

            migrationBuilder.UpdateData(
                table: "connector_definitions",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000005"),
                columns: new[] { "manifest_json", "version" },
                values: new object[] { "{\"id\":\"microsoft-teams\",\"version\":\"1.0.0\",\"operations\":[]}", "1.0.0" });

            migrationBuilder.UpdateData(
                table: "connector_definitions",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000006"),
                columns: new[] { "manifest_json", "version" },
                values: new object[] { "{\"id\":\"email-smtp\",\"version\":\"1.0.0\",\"operations\":[]}", "1.0.0" });

            migrationBuilder.UpdateData(
                table: "connector_definitions",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000007"),
                columns: new[] { "manifest_json", "version" },
                values: new object[] { "{\"id\":\"webhook-emit\",\"version\":\"1.0.0\",\"operations\":[]}", "1.0.0" });

            migrationBuilder.UpdateData(
                table: "connector_definitions",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000008"),
                columns: new[] { "manifest_json", "version" },
                values: new object[] { "{\"id\":\"webhook-receive\",\"version\":\"1.0.0\",\"operations\":[]}", "1.0.0" });

            migrationBuilder.UpdateData(
                table: "connector_definitions",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000009"),
                columns: new[] { "manifest_json", "version" },
                values: new object[] { "{\"id\":\"file-system\",\"version\":\"1.0.0\",\"operations\":[]}", "1.0.0" });

            migrationBuilder.UpdateData(
                table: "connector_definitions",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000010"),
                columns: new[] { "manifest_json", "version" },
                values: new object[] { "{\"id\":\"schedule-cron\",\"version\":\"1.0.0\",\"operations\":[]}", "1.0.0" });

            migrationBuilder.UpdateData(
                table: "connector_definitions",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000011"),
                columns: new[] { "manifest_json", "version" },
                values: new object[] { "{\"id\":\"script-shell\",\"version\":\"1.0.0\",\"operations\":[]}", "1.0.0" });

            migrationBuilder.UpdateData(
                table: "connector_definitions",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000012"),
                columns: new[] { "manifest_json", "version" },
                values: new object[] { "{\"id\":\"github\",\"version\":\"1.0.0\",\"operations\":[]}", "1.0.0" });

            migrationBuilder.UpdateData(
                table: "connector_definitions",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000013"),
                columns: new[] { "manifest_json", "version" },
                values: new object[] { "{\"id\":\"microsoft-365\",\"version\":\"1.0.0\",\"operations\":[]}", "1.0.0" });
        }
    }
}
