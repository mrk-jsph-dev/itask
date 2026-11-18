# iTask API

ASP.NET Core 10 (LTS) minimal APIs in a **vertical slice** layout.

```
src/ITask.Api/
  Features/<Area>/<UseCase>/   one folder per use case: Request, Validator, Endpoint, handler
  Domain/                      entities and enums shared by slices (Board, BoardMember, BoardRole)
  Common/                      cross-cutting: auth (IBoardAccess), persistence, validation, security
  Infrastructure/              outside services: Ai (OpenAI in Foundry), Integrations, Demo seeding
tests/ITask.Api.Tests/         integration tests through the real HTTP pipeline
```

Adding a use case: copy `Features/Boards/CreateBoard/`, rename, and implement `IEndpoint`.
It is discovered automatically. Board-scoped slices must check `IBoardAccess`; a
non-member gets 404.

## Run it

```sh
dotnet run --project src/ITask.Api      # http://localhost:5080, SQLite file, demo seeding on
dotnet test
```

Development uses SQLite so it works anywhere (laptop, Codespaces, cloud sandboxes)
with zero setup. For SQL Server parity run `docker compose up sql` at the repo root
and set `Database__Provider=SqlServer` plus `ConnectionStrings__Default`.

Demo accounts need a password that is never committed:

```sh
dotnet user-secrets --project src/ITask.Api set "Demo:Password" "<pick one>"
```

Migrations (SQL Server, added in the Boards PR):

```sh
dotnet tool restore
dotnet ef migrations add <Name> --project src/ITask.Api
```
