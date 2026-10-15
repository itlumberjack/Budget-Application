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
3. Start the app. The tables are created automatically on first start.

Data entered in testing mode does not carry over to MySQL.

## Changing the data model

```
dotnet tool restore
Database__Provider=MySql dotnet ef migrations add <Name> -p src/BudgetApp
```

## Notes

- There is no login. It's meant to run on your own computer or home network; add ASP.NET Core Identity before exposing it to the internet.
- A paycheck belongs to the month its payday falls in, so a weekly paycheck shows up four or five times depending on the month.
