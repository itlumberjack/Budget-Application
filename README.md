# Family Budget

A personal budget app built with ASP.NET Core MVC (.NET 10) and MySQL.

## Pages

- **Pay & Schedule**: each paycheck's take-home pay and schedule (weekly, every two weeks, twice a month, or monthly on a set day or the last day).
- **Recurring Debts**: bills that repeat every month, each with a title, amount, category and optional due day.
- **Monthly Budgets**: one page per month (`/budget/2026/10`). Creating a month copies in every paycheck that lands in it and every active recurring debt. On the month page you can:
  - add extra money (bonus, overtime) to any paycheck, or change a paycheck's amount or date;
  - add extra expenses to any category section (extra credit card payment, kids pictures);
  - change any bill's amount for just that month or remove it;
  - set each item's status to **Not paid**, **Pending**, or **Complete** (the money has come out of your account). The totals show how much is complete, pending and not paid;
  - click **Add missing items** to pull in paychecks or recurring debts you set up after the month was created. It never overwrites what you already changed.
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
