# Understanding your ROI Dashboard

The ROI Dashboard shows the business value of your automations — how much time they save and how
much cost they avoid — in plain, deterministic numbers. There is no AI and no estimation: every
figure comes from baselines you set multiplied by the runs your workflows actually completed.

## Configuring a workflow's baseline

Open **Analytics**, find the workflow in the table, and click **Configure**. You set three numbers:

- **Manual process time** — how long the task took a person before automation (minutes per run).
- **Manual cost per run** — the fully-loaded cost of one manual run (salary, overhead).
- **Automation cost per run** — connector costs + AI costs + platform cost for one automated run.

Only **Workspace Admins** can edit these, because they are financial figures.

## How the numbers are calculated

For each workflow, over the selected period:

- **Time saved** = manual process time × successful runs
- **Cost avoided** = (manual cost − automation cost) × successful runs
- **ROI %** = cost avoided ÷ total automation cost × 100

The workspace summary sums these across every configured workflow. A workflow with no baseline shows
runs but a dash for value — configure it to include it in the totals.

## Date ranges and export

Switch between **This month**, **Last 3 months**, and **Last 12 months** at the top. Use **Export
CSV** to download the per-workflow table for finance reporting.

## Why a workflow shows no value

- It has no ROI baseline configured (click **Configure**).
- It had no *successful* runs in the period — only successful runs count toward saved time and cost.
