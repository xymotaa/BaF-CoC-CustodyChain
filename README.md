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
| Anexos off-chain | IPFS privado local (`ipfs/kubo`, container Docker) |

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

### 1. Subir o banco de dados e o nó IPFS

Na raiz do projeto:

```bash
docker compose up -d
```

Isso sobe dois containers: `custodychain-mysql` (banco) e
`custodychain-ipfs` (armazenamento de anexos, como o mandado judicial da
T-07). Aguarde ambos ficarem saudáveis:

```bash
docker inspect --format='{{.State.Health.Status}}' custodychain-mysql custodychain-ipfs
```

Repita até aparecer `healthy` nos dois. A API do IPFS fica em
`127.0.0.1:5001` (usada pela aplicação) e o gateway de leitura em
`127.0.0.1:8899` (só para inspecionar arquivos manualmente por CID, se
precisar: `http://127.0.0.1:8899/ipfs/<cid>`).

### 2. Rodar a aplicação

Em `src/CustodyChain.Web`:

```bash
cd src/CustodyChain.Web
dotnet run
```

Não é preciso passar `--urls` nem `ASPNETCORE_ENVIRONMENT` na mão: o
perfil padrão em `Properties/launchSettings.json` já sobe em modo
`Development` (o que ativa migration automática e o seed de dados de
teste) e informa a porta no terminal, na linha:

```
Now listening on: http://localhost:5143
```

**Use a porta que aparecer nessa linha** — é a porta padrão do projeto
(5143), não necessariamente a mesma de sessões anteriores. Se dois
`dotnet run` ficarem abertos ao mesmo tempo (por exemplo um seu e um em
background de uma sessão anterior do Claude), o segundo sobe numa porta
diferente; verifique sempre o que o terminal imprimiu.

Rodar sem `dotnet run` sozinho — passando `--urls` manualmente ou uma
variável de ambiente diferente — também funciona, mas não é necessário
no dia a dia.

### 3. Abrir no navegador

A URL exata é a que apareceu em **"Now listening on"** no terminal —
normalmente **http://localhost:5143/entrar**. Com o perfil padrão o
navegador abre sozinho (`launchBrowser: true`); se não abrir, cole o
endereço manualmente.

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
