# Family Budget

A personal budget app built with ASP.NET Core MVC (.NET 10) and MySQL.

## Pages

- **Pay & Schedule**: each paycheck's take-home pay and schedule (weekly, every two weeks, twice a month, or monthly on a set day or the last day).
- **Recurring Debts**: bills that repeat every month, each with a title, amount, category and optional due day.
- **Monthly Budgets**: one page per month (`/budget/2026/10`), built automatically the first time you open it, laid out like the family budget spreadsheet: one column per payday, with Income at the top of each column, then a section per category (Item | Company | Amount | Total), then what's left over from that paycheck. Paydays a few days apart (Sept 30 and Oct 2) share a column. On the month page you can:
  - click any row to edit it, move it to another payday, or remove it; click **+ add** in a section to add an extra expense (extra credit card payment, kids pictures);
  - click **+ extra** in an Income section to add a bonus or overtime to a paycheck, or click a paycheck to change its amount or date;
  - click the dot at the end of a row to cycle its status: **Not paid** (white), **Pending** (yellow), **Complete** (green, the money has come out of your account);
  - new paychecks and recurring debts are added to this month and later months automatically; **Add missing items** also pulls in anything else missing, and to place any "Not assigned" bills under a payday. It never overwrites what you already changed.

  New bills go under the last paycheck on or before their due day, or the month's first paycheck if they have no due day.
- **Brad's rule** (top of Pay & Schedule, two switches): *End-of-month paychecks pay next month's bills* puts monthly pay that lands on the last day of the month into the next month's budget. *Weekly payday closest to the 1st starts the month* puts the last weekly payday of a month into the next month when it's at least as close to the 1st as the next payday is (October 30 vs. November 6 means October 30 goes to November; on a tie the earlier payday wins). Switching a rule moves paychecks in this month and later months (and last month's end-of-month check) to where the rule puts them, bills included.
- **Moving a paycheck to another month**: click a paycheck on the month page and choose **Move to November** (or back). Use it for a one-off, like an October 31 check that pays November's bills. The unpaid bills due before the next payday move under it, and the bills it covered in the old month go to that month's other paydays. For a paycheck that *always* covers the next month, use the option below instead.
- **Pay & Schedule** has a "Budget this paycheck in the following month" option for end-of-month pay that covers next month's bills, so a Sept 30 paycheck appears in October.
- **Categories**: the sections on the month page. Ten are created for you.

## Run it

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download), then from the repo root:

```
dotnet run --project src/BudgetApp
```

### Testing mode (default): in-memory database

Out of the box `appsettings.json` has `"Database": { "Provider": "InMemory" }`, so no MySQL is needed. Everything is kept in memory and **is wiped when the app stops**, which makes it good for trying things out.

### Switching to MySQL

1. Install MySQL 8 and create a database and user:
   ```sql
   CREATE DATABASE budget;
   CREATE USER 'budget'@'localhost' IDENTIFIED BY 'pick-a-password';
   GRANT ALL ON budget.* TO 'budget'@'localhost';
   ```
2. In `src/BudgetApp/appsettings.json`, set `Database:Provider` to `MySql` and put the password in `ConnectionStrings:Budget` (or use the environment variables `Database__Provider` and `ConnectionStrings__Budget`).
3. Create the tables, either way works:
   - Start the app (`dotnet run --project src/BudgetApp`). With the provider set to `MySql` it creates or upgrades the tables on startup.
   - Or, from the repo root, run `dotnet tool restore` and then `dotnet ef database update -p src/BudgetApp`. The `dotnet ef` commands always use MySQL and the `Budget` connection string, even while `Database:Provider` is still `InMemory`.

When the app starts, its log says which database it is using ("Using MySQL database" or "Using in-memory database").
If it says in-memory when you expected MySQL, check that `appsettings.json` has only one `"Database"` section and that
`appsettings.Development.json` and the `Database__Provider` environment variable don't set it back.

Data entered in testing mode does not carry over to MySQL.

## Weekly bills, six months ahead, and the archive

- A recurring bill can be **monthly** (with a due day), **weekly** or **every two weeks**. For the last two,
  give any date it's due; each month gets a copy for every week (or every other week) from then on. The
  Recurring Debts page totals what a typical month costs (a weekly bill counts 52 times a year).
- Budgets are always built through **six months ahead**. This happens when the app starts and whenever you
  open Monthly Budgets or a month, so new months appear on their own.
- When a month is over it's **archived**: it moves to the Archive list at the bottom of Monthly Budgets and
  becomes read-only. Open it and choose **Unarchive to make changes** to edit it again; it then stays open
  until you click **Archive this month**.

## Settings: buffer and leftover split

The gear button at the top opens **Settings**. Each payday's left over is shared in two steps: a buffer
(20% to start) is held back as padding, and the rest is split between the two of you (50/50 to start).
Both percentages and both names can be changed there. Under every payday on the month page you'll see the
buffer and each person's share, and the **Left over** tile adds them up for the month. A payday that's
short (left over below zero) has nothing to split.

## Even out

**Even out** on a month page suggests moving unpaid bills between paydays so each payday has about the
same amount left over. A bill only moves to a payday on or before its due day (or to the first payday),
so nothing is paid late. Like the AI helper, it only suggests: uncheck any move you don't want and click
**Apply selected**.

## Shifting the first or last payday

On a month page, the first payday column has a left arrow and the last one has a right arrow. The left
arrow moves that payday into the previous month and the right arrow moves it into the next month.
Bills it was covering move to the month's other paydays. A paycheck moved this way shows a
⇄ icon, and Brad's rule leaves it where you put it. Moving it back to its own month clears that.

## Debt payoff

Give a bill on **Recurring Debts** an amount owed and an interest rate (under *Loan or credit card
details*) and it becomes a loan. Its monthly amount is used as the minimum payment. The **Debt Payoff**
page then shows:

- Totals: amount owed, minimum payments, average rate (weighted by balance) and this month's interest.
- A payoff plan modeled on the Vertex42 debt reduction calculator. Pick a strategy (avalanche,
  snowball, highest balance first, order entered, custom priority, or minimums only), an extra amount
  each month and an optional one-time payment. It shows the debt-free date, the total interest, and how
  much interest and time it saves compared with paying only the minimums.
- A chart of the total owed over time, the payoff order with each debt's payoff month, all strategies
  side by side, and a month-by-month schedule.
- A quick form for updating balances from your statements.

Each month, every debt is charged interest (balance × APR ÷ 12) and paid its minimum. The rest of the
monthly payment (the snowball) goes to the first unpaid debt in the strategy's order. When a debt is
paid off, its minimum is added to the snowball.

## AI helper (optional)

The **Suggest arrangement** button on a month page asks Claude to look at that month's paydays and
unpaid bills and suggest moving bills between paydays so no paycheck runs short and bills are paid
before they're due. You see each suggested move with its reason and the left over per payday before
and after, uncheck any you don't want, and click **Apply selected**. Nothing changes until you do.

It needs your own Anthropic API key (from console.anthropic.com). Keep it out of the repo by storing
it as a user secret or an environment variable:

```bash
dotnet user-secrets set "Anthropic:ApiKey" "<your key>" --project src/BudgetApp
# or
export Anthropic__ApiKey="<your key>"
```

`Anthropic:Model` in appsettings.json picks the model (default `claude-opus-5-5`). Each suggestion
sends the month's paycheck amounts, bill names and amounts to Anthropic's API.

## Changing the data model

```
dotnet tool restore
Database__Provider=MySql dotnet ef migrations add <Name> -p src/BudgetApp
```

## Notes

- There is no login. It's meant to run on your own computer or home network; add ASP.NET Core Identity before exposing it to the internet.
- A paycheck belongs to the month its payday falls in, so a weekly paycheck shows up four or five times depending on the month.
