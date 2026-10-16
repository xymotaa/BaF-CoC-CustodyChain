# Implantação isolada — revogação DID no `custodylab2`

## Escopo

Este procedimento aplica a versão v0.62.0 da aplicação, gateway e chaincode
somente no laboratório `custodylab2`. Não use comandos destinados a
`mychannel` ou `custodylab`; não reinicialize wallets, volumes ou bancos.

O procedimento não executa bootstrap ou migração administrativa de DID. A
atualização de chaincode preserva o estado existente do ledger quando mantém
o nome `custodychain` e o canal `custodylab2`.

## Separação de ambientes

| Ambiente | Gateways | Aplicação | Canal |
|---|---|---|---|
| Desenvolvimento/original | `3000`/`3001` | `5143` | configuração local (frequentemente `mychannel`) |
| Laboratório de implantação | `3100`/`3101` | `5243` | `custodylab2` |

As portas de desenvolvimento em `README.md` e `launchSettings.json` não são
valores a copiar para o laboratório. Nos processos de gateway do laboratório,
defina sempre `CHANNEL_NAME=custodylab2`; o default do código continua sendo
`mychannel` apenas para preservar os demais ambientes.

## Pré-requisitos e backup

1. Confirme os endpoints do laboratório: MySQL `3307`, gateways `3100` e
   `3101`, aplicação `.NET` `5243` e canal `custodylab2`.
2. Registre a versão atual da aplicação, dos gateways e do pacote de
   chaincode. Exporte apenas um backup lógico do schema/dados do MySQL 3307.
3. Preserve wallets, certificados, identidades Org1/Org2, volumes e dados
   existentes; não faça reenrollment nem execute bootstrap administrativo.
4. O administrador do laboratório é `did:legal:admin:teste-002`, ativo na
   `Org1MSP`. Antes da implantação, confirme que sua chave pública é
   `z6Mkhj8q86XL5euRdFSxMY4nf1uBYQnkiAcTWSKDpgybFAdR` e que ele permanece na
   governança de administradores ativos.
5. A migration `20261016000000_AdicionaSagaRevogacaoDid` já foi aplicada ao
   MySQL do laboratório. Não execute `database update`, não gere SQL nem tente
   reaplicá-la manualmente; apenas confirme seu registro em
   `__EFMigrationsHistory` e a existência de `SAGA_REVOGACAO_DID`.

## Guarda obrigatória de canal e lifecycle

Todo terminal usado no lifecycle deve declarar e validar o canal antes de
instalar, aprovar ou comitar qualquer definição:

```bash
export CHANNEL_NAME=custodylab2
[ "$CHANNEL_NAME" = "custodylab2" ] || { echo "Canal inválido" >&2; exit 1; }
```

Ao executar `fabric/network/scripts/deployCC.sh`, passe `"$CHANNEL_NAME"`
explicitamente como primeiro argumento. Não execute o script sem esse
argumento, pois o seu default global é `mychannel`.

Antes de qualquer atualização, consulte em **Org1 e Org2** o package ID
instalado, `queryapproved` e `querycommitted` para `custodychain` no canal
`custodylab2`. Prossiga somente se package ID, versão, sequence e política de
endorsement coincidirem entre as duas organizações. O laboratório já registra
`custodychain` versão `2.0`, sequence `1`, aprovada por Org1MSP e Org2MSP;
reconfirme o estado, sem pressupor que esses valores permaneceram inalterados.

## Ordem de implantação

1. Coloque a aplicação 5243 em manutenção, mantendo MySQL e Fabric ativos.
2. Confirme, sem reaplicar migrations, que a migration da saga e os índices da
   tabela `SAGA_REVOGACAO_DID` já estão presentes no MySQL 3307.
3. Se houver uma nova definição de chaincode a publicar, instale, aprove e
   comite nas duas organizações usando explicitamente
   `--channelID "$CHANNEL_NAME"`. Não execute `BootstrapAdminDid`,
   `MigrarAdminDidLegadoV2` ou qualquer comando de recriação de DID.
4. Antes do commit, verifique que Org1 e Org2 aprovaram exatamente o mesmo
   package ID, versão, sequence e política de endorsement.
5. Inicie os gateways do laboratório com `CHANNEL_NAME=custodylab2`, portas
   `3100`/`3101` e MSPs Org1MSP/Org2MSP correspondentes. Consulte a saúde e o
   canal efetivo; interrompa se algum apontar para `mychannel` ou `custodylab`.
6. Pelo gateway Org1, consulte `GET /v2/governanca/identidades`. Se a resposta
   indicar `GOVERNANCE_NOT_INITIALIZED`, execute **uma vez** o endpoint protegido
   `POST /v2/governanca/identidades/inicializar`. Ele apenas cria a governança a
   partir dos DIDs administrativos v2 ativos já existentes; não execute
   `BootstrapAdminDid` nem a migração legada. Uma nova chamada deve apenas retornar
   o mesmo registro de governança.
7. Só então publique a aplicação 5243 configurada para esses dois gateways.

## Verificações de aceitação

1. Consulte a governança de identidades pelo gateway Org1 e confirme a revisão
   e a lista de administradores ativos antes de qualquer revogação. Para o
   `did:legal:admin:teste-002`, confirme também a presença e a chave pública já
   registrada; não recrie o DID.
2. Tente auto-revogação: ela deve ser rejeitada e auditada, sem criar saga.
3. Tente revogar um administrador quando restar apenas um: o Fabric deve
   rejeitar a transação.
4. Revogue uma identidade de teste por prova: o DID no Fabric deve ficar
   `REVOGADO` e o MySQL deve convergir para `REVOGADO`/`INATIVA`.
5. Simule indisponibilidade temporária do gateway sem apagar dados: a saga deve
   ficar pendente, o interveniente não pode operar e o reconciliador deve
   concluir usando o mesmo `commandId` quando o gateway retornar.
6. Confirme logs encadeados para preparação, confirmação, falha e rejeição.
7. Induza somente em ambiente de teste uma resposta temporária `408`, `425`,
   `429` ou `5xx`: a resposta administrativa deve informar processamento, a
   identidade deve continuar bloqueada e o reconciliador deve reutilizar o mesmo
   `commandId`.

## Rollback seguro

1. Não reverta nem reaplique a migration já presente. Se a verificação de
   schema falhar, interrompa a implantação e investigue sem alterar o banco.
2. Se o chaincode já foi comitado, não reverta dados do ledger. Pare a
   aplicação nova, restaure a versão anterior da aplicação/gateway e preserve
   a tabela e as sagas para investigação.
3. Não execute rollback destrutivo no Fabric, não recrie canal, volume ou
   wallet. Registre transaction IDs, `commandId` e hashes de auditoria antes
   de qualquer recuperação manual.
