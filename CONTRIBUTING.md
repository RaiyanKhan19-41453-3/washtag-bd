# Contributing

Thanks for taking an interest in WashTag BD.

## Getting set up

```bash
cp .env.example .env
docker compose up --build -d
```

The app is available at http://localhost:8081; the demo login is in the README.

## Making changes

1. Create a branch from `main`.
2. Keep the Clean Architecture dependency rule: `Api → Infrastructure → Application → Domain`.
3. Add or update xUnit tests for behaviour changes:
   ```bash
   dotnet test WashtagBD.sln
   cd frontend && npm run build
   ```
4. If the database schema changes, add an EF Core migration instead of editing the model snapshot:
   ```bash
   dotnet ef migrations add MyChange \
     --project backend/Washtag.Infrastructure \
     --startup-project backend/Washtag.Api \
     --output-dir Data/Migrations
   ```
5. Commit with a short Conventional-Commits-style message, e.g. `fix(api): reject backward status changes`.

## Pull requests

Describe what changed and why, include verification steps, and keep one logical change per commit. CI runs build, tests and both Docker image builds on every PR.
