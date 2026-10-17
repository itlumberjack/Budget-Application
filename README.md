# Family Budget

A personal budget app built with ASP.NET Core MVC (.NET 10) and MySQL.

## Pages

- **Pay & Schedule**: each paycheck's take-home pay and schedule (weekly, every two weeks, twice a month, or monthly on a set day or the last day). Pay on the last day of the month comes on the last business day: when the 31st is a weekend, payday is the Friday before (a switch on each paycheck turns this off). Holidays aren't counted.
- **Bills & Expenses**: everything that repeats every month, week or two weeks. Each item has a type:
  - **Bill**: utilities, cell phone, insurance, subscriptions.
  - **Living expense**: food, gas and other spending that comes every week or two. New ones start weekly and mandatory.
  - **Loan**: mortgage, car loan, credit card. Only loans have an amount owed, interest rate and payoff priority, and only loans show on Debt Payoff.
- **Monthly Budgets**: one page per month (`/budget/2026/10`), built automatically the first time you open it, laid out like the family budget spreadsheet: one column per payday, with Income at the top of each column, then a section per category (Item | Company | Amount | Total), then what's left over from that paycheck. Paydays a few days apart (Sept 30 and Oct 2) share a column. On the month page you can:
  - click any row to edit it, move it to another payday, or remove it; click **+ add** in a section to add an extra expense (extra credit card payment, kids pictures);
  - click **+ extra** in an Income section to add a bonus or overtime to a paycheck, or click a paycheck to change its amount or date;
  - click the dot at the end of a row to cycle its status: **Not paid** (white), **Pending** (yellow), **Complete** (green, the money has come out of your account);
  - new paychecks and recurring bills are added to this month and later months automatically; **Add missing items** also pulls in anything else missing, and to place any "Not assigned" bills under a payday. It never overwrites what you already changed.

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

## Changing a paycheck, and Resync everything

Saving a paycheck on Pay & Schedule updates its paydays from today on in this month's and later budgets: the new
name and take-home pay (a payday whose amount you change by hand on a month page keeps it), and a new schedule
(paydays that no longer fall on it are removed, missing ones added, and bills on a removed payday move to the
right one). Paydays before today, paydays moved by hand and archived months stay as they were.

*Resync everything* (on Monthly Budgets and each month page) brings this month and every later one up to date with
Pay & Schedule, Bills & Expenses and Debt Payoff: paychecks (amounts, dates, Brad's rule), missing bills, duplicates,
and the extra debt payments. Tick *Also reset unpaid bills* to put unpaid bills back to their amount, due day and
payday from Bills & Expenses as well. Past paydays, pending or complete items and paychecks changed by hand stay.

## Check stubs

Each paycheck on Pay & Schedule (and its edit page) has *Read a check stub*. Pick a PDF or photo of a recent stub
and the AI helper breaks it down: gross pay, earnings lines, before-tax deductions, taxes, after-tax deductions
and take-home pay, with year-to-date amounts. A warning shows if the lines don't add up to the take-home pay.
You can make the stub's take-home the paycheck's amount, and by default its upcoming paydays in months already
built change too (paydays before today keep theirs). The breakdown stays on the paycheck's card; the file
itself isn't stored. Uses the same `Anthropic:ApiKey` as the AI helper.

## Weekly bills, six months ahead, and the archive

- A recurring bill can be **monthly** (with a due day), **weekly** or **every two weeks**. For the last two,
  give any date it's due; each month gets a copy for every week (or every other week) from then on. The
  Bills & Expenses page totals what a typical month costs (a weekly bill counts 52 times a year).
- Budgets are always built through **six months ahead**. This happens when the app starts and whenever you
  open Monthly Budgets or a month, so new months appear on their own.
- When a month is over it's **archived**: it moves to the Archive list at the bottom of Monthly Budgets and
  becomes read-only. Open it and choose **Unarchive to make changes** to edit it again; it then stays open
  until you click **Archive this month**.

## Fixed and flexible bills

Each item on Bills & Expenses has a **Mandatory: stays on its payday** switch. Fixed bills (weekly groceries, say)
stay on the payday they fall under; Even out and the AI helper only ever move flexible ones. Weekly and
every-two-weeks bills start out fixed, monthly bills start out flexible, and a single month's copy can be
switched in its edit dialog. On the month page fixed bills show a pin, and a warning triangle marks any
bill whose payday comes after its due date (for example a bill due the 1st when the first paycheck is the 2nd;
Brad's rule can fix that by moving the end-of-month paycheck forward).

A mandatory item can also say **whose payday** it stays on. Pick a person (say, food from Jessica's weekly
check) and every unpaid copy goes under that person's last paycheck before it's due, in this month and
later ones. Leave it on "Anyone's" to use whichever paycheck comes before the due date. Turning Mandatory
off clears the choice.

Even out and the AI helper never move a bill to a payday after its due date. A monthly bill is added to a
month once; only bills you add on the month page can repeat.

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

## Changing a bill on one month

Tap a bill on a month page to edit it. For a recurring bill, **Also change the ones after this** (on by default)
saves the change to the bill on Bills & Expenses and to its unpaid copies after this one: later the same month
for weekly and every-two-weeks bills, and in every later month. Name, company, amount, category and the
mandatory switch carry over, and so does the due day for a monthly bill, which also moves each copy to the
payday before its new due date. Copies already pending or complete are left as they are. Turn the switch off
to change only this one. On a share of the extra debt payment, the switch makes this month's extra total the
new **Extra each month** on Debt Payoff and spreads it over later months' paydays.

## Plan vs now

Each month keeps its original plan. The plan is saved the first time a bill in that month is marked pending or
complete, or straight away with **Save the plan now** above the paydays. After that, the bottom of every payday
shows the plan and where it stands now side by side (income, spending and left over) plus what's paid, pending and
not paid yet, and what's left in the account once paid and pending bills are out. These update as soon as a status
dot is clicked. A bill whose amount changed shows its planned amount under it, and one added later says "not in
plan". **Make this the plan** replaces the saved plan with the month as it stands. The phone API returns the same
numbers (Plan and Now on each payday, PlannedAmount on each bill) and `POST /api/months/{id}/plan` saves the plan.

## Moving bills and marking them paid

- **Drag a bill** to another payday's column to move it. While dragging, paydays after the bill's due date
  are dimmed and won't take it, and mandatory bills (with a pin) don't drag at all; open one and turn
  Mandatory off to move it. Dragging works with a mouse; on a phone, tap the bill and pick its payday instead.
- **The status dot** cycles Not paid, Pending, Complete. It changes on the spot and saves in the background,
  and the totals update without reloading the page.

## Shifting the first or last payday

On a month page, the first payday column has a left arrow and the last one has a right arrow. The left
arrow moves that payday into the previous month and the right arrow moves it into the next month.
Bills it was covering move to the month's other paydays. A paycheck moved this way shows a
⇄ icon, and Brad's rule leaves it where you put it. Moving it back to its own month clears that.

## Debt payoff

Add a **Loan** on **Bills & Expenses** (or use *Add a loan* on Debt Payoff) with an amount owed and an
interest rate. Its monthly amount is used as the minimum payment. The **Debt Payoff**
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

### Extra payment by payday

**Extra each month** comes out of the payday picked under *Paid from*: one person's paycheck (for weekly or
twice-a-month pay, the 1st, 2nd, 3rd, 4th or last one of the month), or *Spread over the paydays*, where paydays
with more left over give more so every payday ends up with about the same left over. **One-time extras** (a tax
refund, a bonus) can be added as many as you like, each with a date; each comes out of the payday on or before
its date, and it's added to that payday's income (with a *snowball* badge; change it on Debt Payoff) so it doesn't
come out of the paycheck. If the monthly extra is also money on top of your paychecks, turn on *It's money on top of
our paychecks* and it's added to its payday's income too. The month's Income card says how much of the income is
extra for debt. They show on the month page as "Extra: <loan>" lines with a *snowball* badge. In this month, anything
that falls on a payday that has already passed moves to the next one still to come.

Saving the plan, saving amounts owed, changing a loan, or *Add missing items* on a month spreads the unpaid
shares again. Shares marked pending or complete are kept and count toward that month's total. You can change a
share's amount on the month page, or remove it: the month's other paydays make up the difference so the month
still pays the extra set on Debt Payoff, and the changed payday keeps its amount when the extra is spread again.
Turn on *Also change later months* in that dialog to make the month's new total the monthly extra instead.

To add something on a month page, use *add* in a category. *Pick a loan or bill* fills in the item, company, amount,
due day and category from Bills & Expenses (or choose *Something new* and type it). For a loan, turn on *Snowball
payment* and enter how much: it adds an "Extra: <loan>" line on that payday with a *snowball +* badge. It's on top
of the extra from Debt Payoff, so the other shares stay as they are, and deleting it removes it.

The snowball goes to one loan at a time: all the extra goes to the first loan in the strategy's order, and moves
on only once it's paid off (the rest of that month's extra goes to the next loan). A loan with a promo the plan
would otherwise miss moves to the front of the line. The Debt Payoff page lists this month's shares under *Extra by payday*.

Once the plan has a loan paid off, it drops off the budget: no more payments on it in the months after. If its
regular payment is more than what's left in the month it's paid off, that payment is cut to what's left and marked
*last payment*. Its payment then rolls into the snowball, so the next loan gets the extra plus that payment. A loan
with an amount owed of $0 counts as paid off now: it's off this month (a payday that has already passed keeps its
payment) and every later one, and the Debt Payoff page shows how much paid-off loans add to the snowball.

Next to each loan payment and extra share on a month page is what's left on that loan after it ("$250 left",
or "paid off"), counting that month's payments in payday order. This month starts from the amount owed on
Debt Payoff; later months start from the plan's projected balance. Debt Payoff shows the same numbers in
*Extra by payday* and an *End of* column in the payoff order. Past months don't show it.

Loans used to have a weekly or every-two-weeks option. Any loan still on one of those schedules goes back to
one monthly payment when the site starts.

### Promos and reading a statement

A loan can have a promo: a rate (often 0%), an end date, an optional promo balance (blank means all of it),
and whether it is *deferred interest* (miss the date and the interest from the start is charged back). The
payoff plan charges the promo rate on that part, and if the strategy's order wouldn't clear the promo before it
ends, that loan moves to the front of the snowball (soonest end date first). Payments are assumed to come off the regular part first. *Promo
deadlines* on Debt Payoff says the month each promo is cleared, or how much is left at the end, the monthly
amount it would take, and any deferred interest that would be charged. A promo stops counting once its date
passes.

On a loan's page, *Read a statement* takes a PDF or photo (JPG, PNG, WEBP or GIF, up to 20 MB) and the AI helper
reads the amount owed, APR, minimum payment and any promos. You review it next to the loan's current numbers
and pick what to save and which promo to plan around. The file is sent to the AI and not stored. This uses the
same `Anthropic:ApiKey` as the AI helper.

### What If

*What If* on Debt Payoff takes a question in plain words, such as "what if I refinance my HELOC at 7% and roll all
my other debt into it?", and compares the answer with your plan: total interest, debt-free date and what you pay
each month, with the loans before and after. The AI helper only turns the question into changes (new rates,
loans rolled together, a fee or term, more extra, a lump sum); the payoff math is the same as the plan's. When a
loan is rolled in, its payment stays what the old payments added up to unless the question gives a term or
payment. If the payments go down, it also shows what happens if you keep paying what you pay now. Questions are
kept on the What If page and worked out again with your latest balances when you open one. Nothing on your plan
changes.

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

## Signing in

Every page needs someone signed in.

- **First run:** the site opens a setup page to create the first sign-in (an email and a password with at least 8 characters, an uppercase letter, a lowercase letter and a number). The setup page goes away once an account exists, so create yours right after you publish.
- **Adding Jessica:** Settings > Sign-ins. You pick her email and password; she can change the password after she signs in. The same page removes a sign-in (it stops working right away) and changes your own password.
- **Forgot a password:** the other person removes the sign-in and adds it again with a new password. There's no email reset.
- With the in-memory database the sign-ins disappear when the app stops, like everything else.

## Phone app API

The site also answers JSON under `/api` for the phone app. Sign in with the same email and password:

```
POST /api/auth/login     {"email": "...", "password": "..."}   -> accessToken (1 hour) and refreshToken
POST /api/auth/refresh   {"refreshToken": "..."}               -> new tokens
```

Send `Authorization: Bearer <accessToken>` on every other call. The site's sign-in cookie doesn't work for `/api`.

| Call | What it does |
| --- | --- |
| `GET /api/me` | Who is signed in |
| `GET /api/months` | Every month's totals (archived ones are flagged) |
| `GET /api/months/{year}/{month}` | A month's paydays, the bills under each, left over and the split |
| `PUT /api/bills/{id}/status` | `{"status": "Complete"}` (or `Pending`, `NotPaid`) |
| `PUT /api/bills/{id}/payday` | `{"payPeriodId": 12}` moves a bill to another paycheck in its month (`null` for Not assigned) |
| `POST /api/months/{id}/even-out` | Even out's suggestions. Changes nothing |
| `POST /api/months/{id}/moves` | `{"moves": [{"expenseId": 5, "payPeriodId": 12}]}` applies the moves you keep |
| `GET`/`PUT /api/settings` | Buffer and leftover split |
| `GET /api/debt-plan` | Loans, the chosen payoff plan, and every strategy compared |
| `GET /api/categories`, `/api/recurring-bills`, `/api/income` | The lists from the site |

Archived months are read-only: changes come back as `409` with a message. The request and response shapes live in `src/BudgetApp.Core/Contracts`, so the phone app can reference the Core library and use the same classes.

## Project layout

- `src/BudgetApp`: the website (pages, database, AI helper).
- `src/BudgetApp.Core`: the shared library. Models, pay schedules, bill frequencies, Even out and the debt planner live here so a phone app can use the same rules.
- `tests/BudgetApp.Core.Tests`: unit tests for the shared rules. Run them with `dotnet test`.

## Changing the data model

```
dotnet tool restore
Database__Provider=MySql dotnet ef migrations add <Name> -p src/BudgetApp
```

## Notes

- Put the site behind HTTPS before using it over the internet, since passwords and phone app tokens travel with every request.
- A paycheck belongs to the month its payday falls in, so a weekly paycheck shows up four or five times depending on the month.
