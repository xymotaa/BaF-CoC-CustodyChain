# Arquitetura aprovada — SignedOperationV1, outbox e autorização distribuída

## Estado deste registro

Esta é a arquitetura aprovada após a correção das suposições sobre wallet,
VC e outbox. A fatia de designação pericial descrita em
`v0.36.0-designacao-pericia-vc-escopada.md` está implementada. O contrato
`SignedOperationV1` e a migração da outbox abaixo estão implementados na
fatia 2. O tracer de negócio que emitirá a primeira operação, `LAUDO_EMITIR`,
continua planejado para a fatia 3; portanto esta fatia não simula uma decisão
de autorização de domínio.

## SignedOperationV1

Toda operação de domínio que exigir consentimento criptográfico preservará
um envelope completo com:

- `type: "CustodyChainSignedOperation"` e `version: 1`;
- `operationId` global e estável (`urn:uuid`);
- `operation`, usando um nome fechado pela política do chaincode;
- `payload`, com IDs e dados específicos da operação;
- `signerDid`, `keyId` e `algorithm: "Ed25519"`;
- `timestamp`, `expiresAt`, `nonce` e `audience: "custodychain-ledger"`;
- `signature`, Base64URL sem padding.

A wallet assina a canonicalização `custodychain-json-c14n-v1` do envelope sem
`signature`: chaves de objetos em ordem lexical, arrays na ordem original,
UTF-8 e representação JSON determinística. O mesmo vetor canônico será testado
em wallet, gateway e chaincode.

## Proteção contra replay

- `operationId` é a chave de idempotência no banco e no ledger;
- `nonce` é imprevisível e vinculado ao desafio emitido para a operação;
- `timestamp/expiresAt` formam uma janela curta, validada no gateway e chaincode;
- repetir ID e hash canônico iguais retorna o resultado existente;
- repetir o ID com conteúdo diferente é conflito;
- o chaincode verifica assinatura e política antes de aceitar a repetição.

## Fronteiras de confiança

- **Aplicação .NET:** monta o comando, aplica regras locais, associa usuário ao
  DID, persiste a outbox e projeta o resultado. Não possui chaves privadas.
- **Wallet:** gera e custodia chaves, exibe o comando para consentimento e
  assina somente após desbloqueio local. Senha e chave não saem do dispositivo.
- **Outbox/worker:** transporta bytes imutáveis da `SignedOperation`; não recria,
  completa ou assina comandos.
- **Gateway:** autentica o serviço .NET, valida forma, canonicalização,
  assinatura e DID, escolhe a identidade Fabric da organização e encaminha.
- **Identidade Fabric:** prova qual organização submeteu a transação; não
  substitui a assinatura DID do ator.
- **Chaincode:** é a fronteira autoritativa para replay, estado do DID/VC,
  escopo, papel, organização e transição de estado.

## Contrato da outbox

A migração preservará a `SignedOperationV1` inteira, sua versão, hash
canônico, `operationId`, agregado, tentativa e diagnóstico. Estados previstos:
`PENDENTE`, `PROCESSANDO`, `PUBLICADO` e `FALHA`. O worker usará retry com
backoff e lease; payload, assinatura e chave de idempotência nunca serão
alterados entre tentativas.

Se o Fabric confirmar e o update local falhar, a operação continua recuperável:
o retry recebe do ledger o mesmo resultado idempotente e conclui a projeção.
Falha permanente fica em `FALHA` com diagnóstico e exige reprocessamento
explícito; não se cria uma segunda operação silenciosamente.

## Correção da fatia 3 — confirmação síncrona da autorização

`SignedOperationV1` não será publicada pela outbox quando representar uma
autorização humana com janela curta. O prazo de até cinco minutos vale para a
primeira aceitação no gateway/Fabric; a aplicação .NET envia a operação já
assinada de modo síncrono antes de concluir o laudo e a perícia localmente.

Após receber o mesmo `operationId` do ledger, a transação MySQL grava o laudo,
a mudança de estado e o envelope inteiro em `RegistroLedger` como `ANCORADO`.
O worker não republica esse registro: ele permanece evidência local da
autorização já confirmada. Se Fabric rejeitar ou estiver indisponível, nenhuma
mudança de domínio é persistida e o usuário deve tentar novamente.

O chaincode verifica a política `LAUDO_EMITIR` somente para uma operação nova:
DID `PERITO` ativo, chave `capabilityInvocation`, VC não revogada/não expirada,
titular, processo, vestígio e escopo da operação. Para recuperar o caso em que
o Fabric confirmou e a transação MySQL falhou, ele procura primeiro o mesmo
`operationId` e hash canônico; se coincidir, devolve o resultado já confirmado
mesmo depois da expiração. Outro conteúdo com o mesmo identificador é conflito.

Esta decisão mantém a expiração como proteção contra uma nova aceitação tardia,
sem tornar o retry de um commit já confirmado impossível. Uma autorização
confirmada sem conclusão local pode ficar órfã se o navegador for fechado após
falha do MySQL; ela não produz efeito de domínio no Fabric e uma nova assinatura
é necessária para reiniciar o fluxo. Uma recuperação durável de rascunhos fica
fora desta fatia.

## Política distribuída

As operações periciais exigem DID `PERITO`, VC vigente, escopo do mesmo
processo/vestígio e permissão nominal. Coleta exige `COLETOR`; movimentações,
guarda e solicitação de destinação exigem `CUSTODIA`; emissão/revogação de VC
exige `ADMIN`. A destinação preserva segregação: `DESTINACAO_SOLICITAR`
referencia guarda confirmada e VC de custódia; `DESTINACAO_APROVAR` é assinada
por DID administrativo distinto do solicitante.

O .NET controla sessão, UX e pré-condições. O gateway valida transporte e
prova. O chaincode decide a autorização distribuída. A identidade Fabric deve
corresponder à organização autorizada para a operação.

## Revogação

O Fabric é a fonte de verdade. Revogação assinada pelo emissor bloqueia novas
operações, mas não apaga nem invalida retroativamente eventos já confirmados.
A projeção MySQL só muda após confirmação e deve ser reconciliável quando a
resposta do ledger e o commit local divergirem.

## Ordem das próximas fatias

- [x] Fatia 1 — designação de perícia com VC assinada e escopada;
- [x] Fatia 2 — `SignedOperationV1`, persistência integral na outbox e vetores
  canônicos compartilhados;
- [x] Fatia 3 — tracer síncrono de autorização distribuída para `LAUDO_EMITIR`;
- [x] Fatia 4a — `PERICIA_RECEBER` e `LACRE_ROMPER` com autorização síncrona;
- [x] Fatia 4b — `AMOSTRA_CONSUMIR` e `AMOSTRA_EXAURIR`;
- [x] Fatia 4c — `AMOSTRA_FRACIONAR`;
- [x] Fatia 4d — `AMOSTRA_UNIFICAR`, com VC válida por origem;
- [x] Fatia 5a — emissão de VC por escopo de processo/ativo e política por perfil;
- [x] Fatia 5b — `COLETA_REGISTRAR`;
- [x] Fatia 5b.1 — `assetRef` imutável vinculado à coleta assinada;
- [x] Fatia 5c.1 — `REMESSA_CRIAR` inicial, assinada pelo coletor e vinculada à coleta;
- [x] Fatia 5c.2 — `REMESSA_CRIAR` ordinária por `CUSTODIA`;
- [x] Fatia 5d — `REMESSA_RECEBER` e `REMESSA_RECUSAR`;
- [x] Fatia 5e — `GUARDA_REGISTRAR`;
- [x] Fatia 6a — `DESTINACAO_SOLICITAR` assinada pelo custodiante, vinculada à guarda;
- [x] Fatia 6b — `DESTINACAO_APROVAR` assinada por administrador distinto;
- [ ] Fatia 7 — remoção completa dos contratos CoC legados;
- [x] Fatia 8a — rotação de chave com dupla prova e histórico criptográfico;
- [x] Fatia 8b — identidade Fabric por organização;
- [x] Fatia 8c — recuperação administrativa de chave;
- [ ] Fatia 8d — migração operacional e endurecimento.
