# Arquitetura aprovada — SignedOperationV1, outbox e autorização distribuída

## Estado deste registro

Esta é a arquitetura aprovada após a correção das suposições sobre wallet,
VC e outbox. A fatia de designação pericial descrita em
`v0.36.0-designacao-pericia-vc-escopada.md` está implementada. O contrato
`SignedOperationV1` e a migração da outbox abaixo permanecem planejados; este
documento não os apresenta como código entregue.

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

## Política distribuída

As operações periciais exigem DID `PERITO`, VC vigente, escopo do mesmo
processo/vestígio e permissão nominal. Coleta exige `COLETOR`; movimentações e
guarda exigem `CUSTODIA`; emissão/revogação de VC exige `ADMIN`. Destinação
final manterá a segregação solicitante/aprovador definida pelo domínio.

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
- [ ] Fatia 2 — `SignedOperationV1`, persistência integral na outbox e vetores
  canônicos compartilhados;
- [ ] Fatia 3 — tracer de autorização distribuída para `LAUDO_EMITIR`;
- [ ] Fatia 4 — demais operações periciais;
- [ ] Fatia 5 — coleta, remessa, recebimento e guarda;
- [ ] Fatia 6 — destinação final com segregação de funções;
- [ ] Fatia 7 — remoção completa dos contratos CoC legados;
- [ ] Fatia 8 — rotação/recuperação de chaves e identidade Fabric por organização.
