# BaF-CoC / CustodyChain

Sistema web de gestão da cadeia de custódia de evidências periciais, com
registro de integridade em blockchain permissionada. Trabalho de conclusão
de curso de Sistemas de Informação (Unifesspa/FACSI), vinculado à
dissertação de mestrado de Rodrigo Pereira Gomes Oscar (PPGCF/Unifesspa),
que propõe o framework BaF-CoC.

## Stack

| Camada | Tecnologia |
|---|---|
| Aplicação | ASP.NET Core MVC, .NET 10, Razor |
| ORM | Entity Framework Core 9.0.x (provider `Pomelo.EntityFrameworkCore.MySql`) |
| Banco | MySQL 8 (container Docker) |
| Ledger (planejado) | Hyperledger Fabric — hoje substituído por `LedgerFake` em memória |

## Pré-requisitos

- .NET 10 SDK
- Docker + Docker Compose
- Ferramenta `dotnet-ef` instalada globalmente:
  ```
  dotnet tool install -g dotnet-ef
  ```
  Se `dotnet-ef` não for encontrado depois de instalado, use o caminho
  completo do binário: `~/.dotnet/tools/dotnet-ef` (acontece quando
  `~/.dotnet/tools` não está no `PATH` do shell).

## Como testar localmente no navegador

### 1. Subir o banco de dados

Na raiz do projeto:

```bash
docker compose up -d
```

Aguarde o container ficar saudável (leva alguns segundos na primeira vez):

```bash
docker inspect --format='{{.State.Health.Status}}' custodychain-mysql
```

Repita até aparecer `healthy`.

### 2. Rodar a aplicação

Em `src/CustodyChain.Web`:

**bash / zsh:**
```bash
cd src/CustodyChain.Web
ASPNETCORE_ENVIRONMENT=Development dotnet run --urls "http://localhost:5219"
```

**fish:**
```fish
cd src/CustodyChain.Web
env ASPNETCORE_ENVIRONMENT=Development dotnet run --urls "http://localhost:5219"
```

`ASPNETCORE_ENVIRONMENT=Development` é o que ativa, no primeiro start:
- aplicação automática das migrations pendentes (`Database.MigrateAsync()`);
- o seed de dados de teste (ver abaixo).

Sem essa variável a aplicação sobe em modo `Production` e **não** roda
migration nem seed automaticamente.

### 3. Abrir no navegador

**http://localhost:5219/entrar**

A tela de login lista as credenciais DID disponíveis (populadas pelo
seed). Selecione uma, digite qualquer senha não vazia — não há wallet
real implementada ainda, então a senha não é validada nesta fase — e
clique **Entrar**.

Usuários de teste disponíveis (um por perfil):

| Perfil | DID |
|---|---|
| Administrador | `did:legal:admin:teste-001` |
| Central de Custódia | `did:legal:custodian:teste-001` |
| Agente Coletor | `did:legal:delegate:teste-001` |
| Perito | `did:legal:expert:teste-001` |
| Órgão Externo | `did:legal:judge:teste-001` |

Após o login você cai no dashboard (`/`), com sidebar retrátil, navbar
mostrando seu nome/perfil reais e métricas lidas do banco (zeradas até
que existam vestígios cadastrados).

### 4. Encerrar

No terminal onde a aplicação está rodando: `Ctrl+C`.

Para derrubar o banco também:

```bash
docker compose down
```

(os dados persistem no volume Docker `mysql_data` entre reinícios; use
`docker compose down -v` só se quiser apagar tudo e recomeçar do zero).

## Testar a API/banco diretamente (sem navegador)

Consultar os dados populados pelo seed direto no MySQL:

```bash
docker exec custodychain-mysql mysql --default-character-set=utf8mb4 \
  -uroot -proot_dev -e "USE custodychain; SHOW TABLES;"
```

Use sempre `--default-character-set=utf8mb4` no cliente `mysql`, senão
texto acentuado aparece corrompido no terminal (os dados em si estão
armazenados corretamente).

## Migrations

Criar uma nova migration depois de alterar entidades em
`Models/Entities/` ou `Data/CustodyChainDbContext.cs`:

```bash
cd src/CustodyChain.Web
dotnet ef migrations add NomeDaMigration
dotnet ef database update
```

Se `dotnet ef` não for encontrado, substitua por `~/.dotnet/tools/dotnet-ef`.

## Estrutura do repositório

```
src/CustodyChain.Web/     Aplicação ASP.NET Core MVC
docker-compose.yml         MySQL 8 para desenvolvimento local
changelog/                 Histórico de decisões por versão (não versionado no git)
docs/                      Documentação de referência do TCC (não versionado no git)
```

`changelog/` e `docs/` estão no `.gitignore` — são material de apoio
local, não fazem parte do código entregue.
