# Decisões aprovadas e reprovadas — histórico consolidado

_Última atualização: v0.15.0 (T-11 Auditoria e verificador), 2026-09-23_

Registro único de toda pergunta de aprovação feita ao usuário ao longo da
implementação, organizada por seção/versão do `changelog/`. Cobre tanto
decisões de forma (como implementar algo já previsto no documento
técnico) quanto decisões de escopo (se algo fora do documento deveria
existir).

---

## v0.1.0 — Setup inicial do projeto

**Estrutura de solução .NET: projeto único ou múltiplos projetos?**
✅ Aprovado: **projeto único** (Web/Models/Data/Services numa pasta só).

**Escopo desta sessão: criar projeto + subir MySQL + testar migration, ou só o esqueleto?**
✅ Aprovado: **ponta a ponta** — projeto, Docker, DbContext, primeira migration aplicada.

**Escopo do modelo inicial: subconjunto mínimo ou as 16 entidades completas de uma vez?**
✅ Aprovado: **subconjunto mínimo primeiro** (PERFIL, INTERVENIENTE, PROCESSO, TIPO_VESTIGIO, VESTIGIO), para validar o pipeline antes de expandir.

**Versão do EF Core: Pomelo ainda não suporta EF Core 10 — como resolver?**
✅ Aprovado: **fixar EF Core em 9.0.x** dentro do projeto `net10.0`, em vez de trocar de provider ou rebaixar o TargetFramework.

---

## v0.2.0 — Modelo de dados completo (16 entidades)

**Por onde seguir depois do setup: expandir para 16 entidades, layout+T-01, ou rede Fabric?**
✅ Aprovado: **expandir para as 16 entidades** antes de construir telas sobre um modelo parcial.

---

## v0.3.0 — Seed inicial

**Próximo passo: seed, layout+T-01, ou rede Fabric?**
✅ Aprovado: **seed inicial** (perfis, tipos de vestígio, usuários de teste) primeiro — rápido e destrava testar as telas com dados reais.

---

## v0.4.0 — Layout base, T-01 (login) e T-02 (dashboard)

**As skills de design instaladas (frontend-design, design-taste-frontend) são voltadas a landing pages, não a dashboards — como aplicar?**
✅ Aprovado: **aplicar só os princípios transferíveis** (tipografia deliberada, paleta com acento único, WCAG AA, dark mode, formulários com label acima do input) — ignorar heroes/marquees/scroll-hijack, que não se aplicam a um sistema de gestão interno.

**Paleta de cores do sistema**
✅ Aprovado: **slate + azul profundo** (`#2c4a6e`), institucional e sóbrio.

**Tipografia do sistema**
✅ Aprovado: **Source Sans 3 + JetBrains Mono** (texto e hashes/DIDs/códigos), self-hosted.

---

## v0.5.0 — T-03 Cadastro de vestígio (FAV)

**Escopo do formulário: completo (Requisição + Lacre Digital + Identificação + Coleta) ou versão mínima?**
✅ Aprovado: **formulário completo** em uma tela, fiel à descrição da FAV no documento.

**Campos de Requisição (tipo de requisição, FAV anterior, urgência) não existem no dicionário de dados oficial — omitir ou adicionar ao schema?**
✅ Aprovado: **omitir por agora** — não inventar colunas fora da Seção 8.2. Ficou documentado como pendência.

⚠️ **Extensão fora do documento, aprovada retroativamente em `v0.8.1`:** um `Processo` de teste foi adicionado ao seed (não mencionado na Seção 8.5, que só cita "perfis, tipos de vestígio e cinco usuários de teste") — necessário porque, sem processo algum, a T-03 não tinha Número do Caso (NC) para associar.

---

## v0.6.0 — T-05 Movimentações (remessa)

**Escopo da T-05: só criar a remessa, ou criar + aprovar/recusar na mesma tela?**
✅ Aprovado: **só criar a remessa** — a aprovação/recusa fica para a T-04 (Recebimento), que é onde o documento já separa essa responsabilidade.

---

## v0.7.0 — T-04 Recebimento

**Estrutura da tela: lista com ação inline, ou lista + tela de detalhe separada por item?**
✅ Aprovado: **lista + ação inline** (confirmar/recusar direto na linha do item).

---

## v0.8.0 — T-06 Arquivo (entrada em guarda + inventário)

**Nenhum vestígio chega a "Armazenado" ainda — construir a ação de entrada no arquivo antes da tela de inventário, ou só a listagem mostrando todos os estados?**
✅ Aprovado: **construir "Dar entrada no arquivo" + a tela de inventário** — sem a ação de entrada, T-06 não teria nada de fato "guardado" para listar.

⚠️ **Extensão fora do documento, aprovada retroativamente em `v0.8.1`:** coluna `VESTIGIO.hash_sha256` (migration `AdicionaHashVestigio`), não prevista no dicionário de dados oficial (Seção 8.2). Perguntado apenas *como* resolver a lacuna (buscar dentro do `PayloadJson` do `REGISTRO_LEDGER` vs. nova coluna), não *se* deveria existir — motivo da correção do usuário registrada a seguir.

---

## v0.8.1 — Correção de processo: aprovação ANTES de codificar

O usuário identificou que duas extensões fora do documento técnico
(`Processo` de teste no seed, `VESTIGIO.hash_sha256`) foram implementadas
sem que a decisão de *se deveriam existir* fosse submetida à aprovação
antes do código ser escrito — só a forma de implementação foi
perguntada, o que já pressupunha a resposta.

**Regra estabelecida:** toda implementação deve ter base no documento
técnico. Qualquer extensão fora dele exige aprovação explícita ANTES de
codificar, separada de qualquer decisão sobre como implementar. Isso foi
salvo em memória (`feedback_aprovacao_fora_do_documento`) para valer em
todas as sessões futuras.

**Aprovação retroativa das duas extensões já implementadas:**

**`Processo` de teste no seed (introduzido em `v0.5.0`)**
✅ Aprovado, mantido — sem ele T-03 fica impraticável de testar.

**`VESTIGIO.hash_sha256` (introduzido em `v0.8.0`)**
✅ Aprovado, mantido — sem ela, a busca por hash de T-06 (e o futuro verificador independente de T-11) não teriam onde consultar; o hash só existia dentro do texto do `REGISTRO_LEDGER.PayloadJson`.

---

## v0.9.0 — T-09 fatia 1: designação de perícia + rompimento de lacre

**T-09 é grande (rompimento + 4 tipos de OPERACAO_AMOSTRA + reselagem + laudo) — construir tudo de uma vez ou fatiar?**
✅ Aprovado: **fatiar**, começando por rompimento de lacre + exame.

**RN10 exige credencial de permissão vigente para o vestígio específico, mas não existe tela para emiti-la (isso seria parte da T-10) — pular a checagem ou construir a designação primeiro?**
✅ Aprovado: **construir a designação de perícia (+ emissão de credencial de permissão) primeiro** — sem isso, RN10 nunca poderia ser satisfeita de verdade, só fingida.

**Quem deve poder designar uma perícia (escolher perito, emitir credencial de permissão)?**
✅ Aprovado: **Central de Custódia** — coerente com o papel que a T-05 já atribui a esse perfil (remessa para perícia).

---

## v0.10.0 — T-09 fatia 2: emissão de laudo

**Não há entidade própria para "exame" no documento — construir uma tela intermediária de exame mesmo sem correspondência no schema, ou ir direto para a emissão do laudo?**
✅ Aprovado: **direto para emissão do laudo** — não inventar uma tela para um conceito que a especificação não modela como entidade.

---

## v0.11.0 — T-09 fatia 3: operações de amostra

**UNIFICACAO combina múltiplos vestígios de origem, mas o schema de OPERACAO_AMOSTRA só tem um `vestigio_origem_id` por registro — como modelar?**
✅ Aprovado: **um registro de OPERACAO_AMOSTRA por vestígio de origem** (mesmo `vestigio_resultante_id`, `vestigio_origem_id` diferente em cada linha) — o schema já suporta isso sem mudança nenhuma.

**Qual o estado inicial do vestígio resultante criado por fracionamento ou unificação na máquina de estados?**
✅ Aprovado: **`EmPericia`** — nasce já dentro do processamento pericial, sob custódia do mesmo perito, em vez de voltar ao início do ciclo de vida (Coletado).

**Bug real encontrado: o vestígio resultante de UNIFICACAO não copiava nenhum hash (ficava sempre NULL). Como calcular o hash de um item com múltiplas origens, que podem ter hashes diferentes?**
✅ Aprovado: **hash combinado das origens** — SHA-256 sobre a concatenação ordenada dos hashes de todas as origens, para que qualquer alteração numa origem mude o hash do resultante.

---

## v0.12.0 — T-07 Destinação final + IPFS privado (P-01)

**`DESCARTE.autorizacao_anexo_id` é `NOT NULL` — exige um `ANEXO` real (mandado judicial), mas nenhuma tela do sistema fez upload de arquivo até agora. Construir upload real ou simular o anexo?**
✅ Aprovado: **construir upload de arquivo real** — sem isso, RN20 ficaria fingida, não implementada (mesmo problema já evitado com RN10 na T-09).

**P-01 do documento de contexto está em aberto: "binários grandes: IPFS privado ou filesystem local". Qual usar para o upload do mandado judicial?**
✅ Aprovado, após esclarecimento do trade-off (IPFS exige subir um daemon, configurar pinning, tratar integração via HTTP API — trabalho de infraestrutura real, não só a tela): **implementar IPFS agora**, resolvendo P-01 com infraestrutura real em vez de adiar para filesystem local.

**O CID do IPFS (identificador de conteúdo) vai em `ANEXO.caminho_relativo` (campo descrito no documento como "Convenção da Seção 13.4", uma convenção de pastas de filesystem, não de IPFS) ou precisa de coluna nova?**
✅ Aprovado: **reaproveitar `caminho_relativo` para o CID** — sem mudança de schema; o campo comporta qualquer string até 500 caracteres, e a divergência da convenção textual original fica documentada no código.

**Quem deve solicitar o descarte/restituição, já que o magistrado (RN20) pode nem ter conta no sistema?**
✅ Aprovado: **Central de Custódia solicita**, informando o DID do magistrado que autorizou via mandado anexado — RN20 é satisfeita pelo anexo real + DID registrado, não por um login de magistrado.

**Quem deve aprovar (RN16: quem solicita não pode aprovar)?**
✅ Aprovado: **Administrador** — segrega a função de encerramento da cadeia para o perfil administrativo.

---

## v0.13.0 — T-08 Relatórios (PDF e XML)

**O documento não especifica o conteúdo exato do "relatório gerencial" (RF20) — relatório por processo (peça processual) ou relatório gerencial agregado (dashboard expandido)?**
✅ Aprovado: **relatório por processo** — D-07 justifica PDF/XML como "peça processual", que é o formato por-processo, mais útil como documento anexável a um processo judicial.

**Geração de PDF exige biblioteca externa — QuestPDF (nativo .NET) ou renderizar a view Razor via motor de navegador headless?**
✅ Aprovado: **QuestPDF** — sem dependência de motor de navegador externo, licença Community gratuita para o caso do TCC.

**Bug real encontrado: geração de PDF falhava (500) porque a fonte "DejaVu Sans" não existe no ambiente. Usar fonte do sistema operacional disponível (Noto Sans) ou embutir fonte própria no projeto?**
✅ Aprovado: **baixar Source Sans 3 em TTF e embutir no projeto** — garante que o PDF gera igual em qualquer ambiente, sem depender do que está instalado no SO, e mantém consistência visual com a aplicação web.

---

## v0.14.0 — T-10 Gestão de perfis

**RF03 pede "gerar DID e ativá-lo com assinatura do emissor" — formulário único (cadastra e já ativa) ou duas etapas (cadastra pendente, ativa depois)?**
✅ Aprovado: **duas etapas** — mais fiel ao ciclo `GERADO→ATIVO→REVOGADO` do dicionário de dados do que um cadastro que já nasce ativo.

(Correção de bug sem pergunta de aprovação: `AccessDeniedPath` apontava para `/entrar`, fazendo um usuário sem permissão parecer deslogado em vez de ver "acesso negado". Corrigido criando `/acesso-negado` dedicado — decisão técnica direta, não uma divergência do documento.)

---

## v0.15.0 — T-11 Auditoria e verificador independente

**A linha do tempo pode vir de REGISTRO_LEDGER (já preenchido desde a T-03) ou LOG_AUDITORIA (nunca populado em 14 versões anteriores, exigiria instrumentar todos os controllers já testados)?**
✅ Aprovado: **REGISTRO_LEDGER** — é literalmente a linha do tempo do vestígio no ledger, já tem um registro por evento real. `LOG_AUDITORIA` (RF23-25) fica como pendência documentada.

**O verificador (usuário sobe arquivo + hash é comparado) busca o hash por Rótulo de Evidência informado, ou tenta achar batendo contra todos os vestígios sem esse dado?**
✅ Aprovado: **buscar por Rótulo de Evidência** — mais preciso, evita colisão de contexto.

**Bug real encontrado: REGISTRO_LEDGER.PayloadJson usa o tipo `json` do MySQL, que reformata o texto ao armazenar — o hash não pode ser reproduzido a partir do que fica salvo no banco. Documentar como limitação conhecida, ou trocar o tipo de coluna agora (nova migration)?**
✅ Aprovado: **trocar a coluna para `longtext` agora** — resolve a limitação de verdade; nenhum controller precisou mudar, e validado ponta a ponta que o hash recalculado a partir do payload armazenado bate exatamente após a correção.

---

## Resumo rápido

| Decisão | Resultado |
|---|---|
| Estrutura .NET: projeto único | ✅ Aprovado |
| Pipeline ponta a ponta na primeira sessão | ✅ Aprovado |
| Modelo mínimo antes de expandir para 16 entidades | ✅ Aprovado |
| EF Core fixado em 9.0.x | ✅ Aprovado |
| Expandir para 16 entidades antes de telas | ✅ Aprovado |
| Seed inicial antes de layout | ✅ Aprovado |
| Aplicar só princípios transferíveis das skills de design | ✅ Aprovado |
| Paleta slate + azul profundo | ✅ Aprovado |
| Tipografia Source Sans 3 + JetBrains Mono | ✅ Aprovado |
| Formulário T-03 completo em uma tela | ✅ Aprovado |
| Omitir campos de Requisição fora do dicionário | ✅ Aprovado (omissão) |
| T-05 só cria a remessa (sem aprovação/recusa) | ✅ Aprovado |
| T-04 lista + ação inline | ✅ Aprovado |
| Construir entrada no arquivo antes do inventário | ✅ Aprovado |
| Fatiar a T-09 em partes menores | ✅ Aprovado |
| Construir designação de perícia antes do rompimento | ✅ Aprovado |
| Central de Custódia designa perícias | ✅ Aprovado |
| Sem tela de "exame" intermediária | ✅ Aprovado |
| `Processo` de teste no seed (fora do documento) | ✅ Aprovado retroativamente |
| `VESTIGIO.hash_sha256` (fora do documento) | ✅ Aprovado retroativamente |
| Unificação: um registro OPERACAO_AMOSTRA por origem | ✅ Aprovado |
| Estado inicial do vestígio resultante: EmPericia | ✅ Aprovado |
| Hash combinado das origens na unificação | ✅ Aprovado |
| Upload real de arquivo para o mandado judicial (RN20) | ✅ Aprovado |
| Resolver P-01 com IPFS privado agora (não filesystem) | ✅ Aprovado |
| CID do IPFS reaproveita `ANEXO.caminho_relativo` | ✅ Aprovado |
| Central de Custódia solicita destinação final | ✅ Aprovado |
| Administrador aprova destinação final | ✅ Aprovado |
| Relatório por processo (não gerencial agregado) | ✅ Aprovado |
| QuestPDF para geração de PDF | ✅ Aprovado |
| Fontes próprias embutidas em vez de fonte do SO | ✅ Aprovado |
| Cadastro de interveniente em duas etapas (GERADO→ATIVO) | ✅ Aprovado |
| Linha do tempo baseada em REGISTRO_LEDGER (não LOG_AUDITORIA) | ✅ Aprovado |
| Verificador busca hash por Rótulo de Evidência | ✅ Aprovado |
| Trocar PayloadJson de `json` para `longtext` | ✅ Aprovado |

Nenhuma extensão ou proposta foi reprovada até o momento.
