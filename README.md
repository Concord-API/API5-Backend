# API5-Backend

API de leitura do Ratio: ASP.NET Core sobre .NET 10, Clean Architecture. A documentação
completa está na wiki `API-5-docs`.

## Pré-requisitos

.NET SDK 10 e Docker Desktop (os testes de integração sobem um `postgres:16` via Testcontainers).

## Comandos

```bash
cd Ratio
dotnet restore
dotnet build
dotnet test
dotnet run --project Ratio.Api
```

`dotnet test --filter "FullyQualifiedName~NomeDoTeste"` roda um recorte. Sem Docker, os testes de
`Ratio.Infrastructure.Tests` falham; os demais rodam normalmente.

## Configuração

Por variável de ambiente, nunca em `appsettings.json` versionado:

| Variável | Para quê |
|---|---|
| `ConnectionStrings__Ratio` | Postgres com o schema `dw` publicado (usuário somente leitura). A API não inicia sem ela |
| `Cors__AllowedOrigins__0` | origem do frontend em dev; em Development já vem `http://localhost:5173` |

```powershell
$env:ConnectionStrings__Ratio = "Host=<host>;Port=5433;Database=ratio;Username=ratio_api;Password=<senha>"
```

Em produção (e em qualquer máquina que não use variável de ambiente), copie
`Ratio/Ratio.Api/appsettings.Production.example.json` para `appsettings.Production.json` ao lado do
executável e preencha os valores. O arquivo real não é versionado nem entra no pacote.

A API escuta em `http://127.0.0.1:5000` (atrás do NGINX em produção), roda como serviço Windows
e grava log em `logs/ratio-.log`. Detalhes de banco e ambiente: wiki, página *Ambiente local*.

## Testes (TDD)

| Projeto | O que cobre |
|---|---|
| `Ratio.Application.Tests` | Domain e casos de uso (xUnit + Moq) |
| `Ratio.Infrastructure.Tests` | repositórios contra Postgres real (Testcontainers) |
| `Ratio.Api.Tests` | HTTP em memória com `WebApplicationFactory` (`ApiFactory`) |

Asserção é o `Assert` do xUnit; sem biblioteca extra. Versões de pacote em `Ratio/Directory.Packages.props`.

## Pendências

- Repositórios de leitura e casos de uso (nenhum ainda).
- Contrato da API além de `/health` e `/health/ready`.
- Pacote de versão completo (API + frontend + `nginx.conf` + dump do `dw`): o CI hoje só publica a API `win-x64`.
