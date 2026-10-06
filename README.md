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
| Ledger | Hyperledger Fabric + chaincode JavaScript + gateway HTTP Node.js |
| Anexos off-chain | IPFS privado local (`ipfs/kubo`, container Docker) |
| Identidade | DID v2 + wallet local Ed25519 (SQLite cifrado) |

## Pré-requisitos

- .NET 10 SDK
- Node.js 22.5 ou superior
- `libsodium` disponível para a aplicação .NET validar assinaturas Ed25519
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

### 2. Preparar Fabric, gateway e wallet para autenticação

O login não aceita mais uma senha simbólica. A aplicação cria um desafio
descartável, a wallet local o assina com Ed25519 e o .NET valida a assinatura
contra a chave pública do DID v2 armazenado no Fabric.

Defina um segredo de serviço forte, igual no gateway e na aplicação (não o
grave no repositório):

```bash
export GATEWAY_SERVICE_TOKEN='<segredo-local-forte>'
export Ledger__ServiceToken="$GATEWAY_SERVICE_TOKEN"
```

Depois de subir a rede e instalar a versão atual do chaincode, inicie o
gateway em outro terminal:

```bash
cd fabric/gateway
npm start
```

Crie uma única identidade administrativa na wallet. O comando solicita e
confirma a senha sem exibi-la:

```bash
cd wallet
npm run create-admin -- --did did:legal:admin:teste-001
```

O comando imprime somente `did`, `keyId`, algoritmo e chave pública. Em uma
rede nova, use esses três valores públicos para executar uma vez o bootstrap
pelo gateway:

```bash
curl -X POST http://127.0.0.1:3000/v2/bootstrap/admin \
  -H "Authorization: Bearer $GATEWAY_SERVICE_TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"did":"did:legal:admin:teste-001","verificationMethodId":"did:legal:admin:teste-001#auth-1","publicKeyMultibase":"<chave-publica-impressa>"}'
```

Se a rede já possuir o DID administrativo legado v1 ativo (como a base de
desenvolvimento inicial), use a migração única abaixo em vez do bootstrap. Ela
só é aceita pela `Org1MSP`, preserva o DID e vincula a chave pública da wallet:

```bash
curl -X POST http://127.0.0.1:3000/v2/bootstrap/admin/legacy-migration \
  -H "Authorization: Bearer $GATEWAY_SERVICE_TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"did":"did:legal:admin:teste-001","verificationMethodId":"did:legal:admin:teste-001#auth-1","publicKeyMultibase":"<chave-publica-impressa>"}'
```

Por fim, inicie a wallet. Ela escuta somente em `127.0.0.1` e mostra um código
de pareamento novo no terminal:

```bash
cd wallet
npm start
```

O bootstrap é deliberadamente único e aceito apenas pela MSP administrativa
`Org1MSP`. Para outra organização, configure `MSP_ID`, `MSP_PATH` e
`TLS_CERT_PATH` no processo do gateway correspondente.

### 3. Rodar a aplicação

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

### 4. Abrir no navegador

A URL exata é a que apareceu em **"Now listening on"** no terminal —
normalmente **http://localhost:5143/entrar**. Com o perfil padrão o
navegador abre sozinho (`launchBrowser: true`); se não abrir, cole o
endereço manualmente.

A tela de login solicita o DID, a senha da wallet e o código de pareamento.
A senha é enviada pelo navegador apenas ao serviço local da wallet e nunca à
aplicação .NET. Nesta primeira fatia, somente o DID administrativo provisionado
acima possui uma chave pública v2 e pode entrar por prova de posse real.

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

### 5. Encerrar

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
