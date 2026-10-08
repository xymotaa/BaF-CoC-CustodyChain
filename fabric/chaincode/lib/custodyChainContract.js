'use strict';

const crypto = require('node:crypto');
const { Contract } = require('fabric-contract-api');

const PREFIXO_DID = 'DID';
const PREFIXO_CREDENCIAL = 'CRED';
const PREFIXO_HISTORICO = 'HIST';
const PREFIXO_GOVERNANCA = 'GOV';
const PREFIXO_OPERACAO_ASSINADA = 'SOP';
const PREFIXO_TRANSFERENCIA_INICIAL = 'TRI';
const PREFIXO_RESPOSTA_REMESSA = 'TRR';
const PREFIXO_GUARDA = 'GUA';
const PREFIXO_DESTINACAO_SOLICITACAO = 'DSR';
const PREFIXO_DESTINACAO_APROVACAO = 'DSA';
const MSP_ADMINISTRADOR = 'Org1MSP';
const OPERACOES_ASSINADAS_SUPORTADAS = new Set([
    'COLETA_REGISTRAR', 'REMESSA_CRIAR', 'REMESSA_RECEBER', 'REMESSA_RECUSAR', 'GUARDA_REGISTRAR', 'DESTINACAO_SOLICITAR', 'DESTINACAO_APROVAR', 'PERICIA_RECEBER', 'LACRE_ROMPER', 'LAUDO_EMITIR', 'AMOSTRA_CONSUMIR', 'AMOSTRA_EXAURIR', 'AMOSTRA_FRACIONAR', 'AMOSTRA_UNIFICAR'
]);

class CustodyChainContract extends Contract {

    _agora(ctx) {
        const timestamp = ctx.stub.getTxTimestamp();
        const milissegundos = (timestamp.seconds.low * 1000) + Math.floor(timestamp.nanos / 1e6);
        return new Date(milissegundos).toISOString();
    }

    async GerarDid(ctx, did, metodoDid) {
        const chave = ctx.stub.createCompositeKey(PREFIXO_DID, [did]);
        const existente = await ctx.stub.getState(chave);
        if (existente && existente.length > 0) {
            throw new Error(`DID já existe: ${did}`);
        }

        const documento = {
            did,
            metodoDid,
            ativo: false,
            didEmissor: null,
            criadoEm: this._agora(ctx)
        };

        await ctx.stub.putState(chave, Buffer.from(JSON.stringify(documento)));
        return JSON.stringify(documento);
    }

    async BootstrapAdminDid(ctx, did, verificationMethodId, publicKeyMultibase) {
        this._garantirOrganizacaoAdministradora(ctx);
        this._validarDocumentoDidV2(did, verificationMethodId, publicKeyMultibase);

        const chaveBootstrap = ctx.stub.createCompositeKey(PREFIXO_GOVERNANCA, ['admin-bootstrap']);
        const bootstrapExistente = await ctx.stub.getState(chaveBootstrap);
        if (bootstrapExistente && bootstrapExistente.length > 0) {
            throw new Error('O bootstrap do DID administrador já foi concluído.');
        }

        const chaveDid = ctx.stub.createCompositeKey(PREFIXO_DID, [did]);
        const didExistente = await ctx.stub.getState(chaveDid);
        if (didExistente && didExistente.length > 0) {
            throw new Error(`DID já existe: ${did}`);
        }

        const agora = this._agora(ctx);
        const documento = {
            id: did,
            did,
            metodoDid: 'did:legal:admin',
            version: 2,
            status: 'ATIVO',
            ativo: true,
            controller: did,
            verificationMethod: [{
                id: verificationMethodId,
                type: 'Multikey',
                controller: did,
                publicKeyMultibase,
                status: 'ACTIVE',
                validFrom: agora
            }],
            authentication: [verificationMethodId],
            assertionMethod: [verificationMethodId],
            capabilityInvocation: [verificationMethodId],
            documentVersion: 1,
            keySequence: 1,
            didEmissor: null,
            criadoEm: agora,
            ativadoEm: agora
        };

        await ctx.stub.putState(chaveDid, Buffer.from(JSON.stringify(documento)));
        await ctx.stub.putState(chaveBootstrap, Buffer.from(JSON.stringify({ did, criadoEm: agora })));
        return JSON.stringify(documento);
    }

    async MigrarAdminDidLegadoParaV2(ctx, did, verificationMethodId, publicKeyMultibase) {
        this._garantirOrganizacaoAdministradora(ctx);
        this._validarDocumentoDidV2(did, verificationMethodId, publicKeyMultibase);

        const chaveBootstrap = ctx.stub.createCompositeKey(PREFIXO_GOVERNANCA, ['admin-bootstrap']);
        const bootstrapExistente = await ctx.stub.getState(chaveBootstrap);
        if (bootstrapExistente && bootstrapExistente.length > 0) {
            throw new Error('O bootstrap ou a migração do DID administrador já foi concluída.');
        }

        const chaveDid = ctx.stub.createCompositeKey(PREFIXO_DID, [did]);
        const bytes = await ctx.stub.getState(chaveDid);
        if (!bytes || bytes.length === 0) {
            throw new Error(`DID legado não encontrado: ${did}`);
        }

        const legado = JSON.parse(bytes.toString());
        if (legado.version === 2 || legado.metodoDid !== 'did:legal:admin' || legado.ativo !== true) {
            throw new Error('Somente um DID administrativo legado ativo pode ser migrado para v2.');
        }

        const agora = this._agora(ctx);
        const documento = {
            id: did,
            did,
            metodoDid: 'did:legal:admin',
            version: 2,
            documentVersion: 1,
            status: 'ATIVO',
            ativo: true,
            controller: did,
            verificationMethod: [{
                id: verificationMethodId,
                type: 'Multikey',
                controller: did,
                publicKeyMultibase,
                status: 'ACTIVE',
                validFrom: legado.ativadoEm || agora
            }],
            authentication: [verificationMethodId],
            assertionMethod: [verificationMethodId],
            capabilityInvocation: [verificationMethodId],
            keySequence: 1,
            didEmissor: null,
            criadoEm: legado.criadoEm || agora,
            ativadoEm: legado.ativadoEm || agora,
            migradoDeVersao: 1,
            migradoEm: agora
        };

        await ctx.stub.putState(chaveDid, Buffer.from(JSON.stringify(documento)));
        await ctx.stub.putState(chaveBootstrap, Buffer.from(JSON.stringify({
            did,
            criadoEm: agora,
            origem: 'MIGRACAO_LEGADA_V1'
        })));
        return JSON.stringify(documento);
    }

    async AtualizarCapacidadeAdminDidV2(ctx) {
        this._garantirOrganizacaoAdministradora(ctx);
        const chaveBootstrap = ctx.stub.createCompositeKey(PREFIXO_GOVERNANCA, ['admin-bootstrap']);
        const bootstrap = await ctx.stub.getState(chaveBootstrap);
        if (!bootstrap || bootstrap.length === 0) {
            throw new Error('O DID administrador ainda não foi inicializado.');
        }

        const { did } = JSON.parse(bootstrap.toString());
        const chaveDid = ctx.stub.createCompositeKey(PREFIXO_DID, [did]);
        const bytes = await ctx.stub.getState(chaveDid);
        if (!bytes || bytes.length === 0) {
            throw new Error('DID administrador não encontrado.');
        }

        const documento = JSON.parse(bytes.toString());
        if (documento.version !== 2 || documento.status !== 'ATIVO') {
            throw new Error('DID administrador v2 ativo é obrigatório para a migração.');
        }

        const chaveAdministrativa = documento.verificationMethod?.[0]?.id;
        if (!chaveAdministrativa) {
            throw new Error('DID administrador não possui método de verificação.');
        }

        const capacidades = new Set(documento.capabilityInvocation || []);
        capacidades.add(chaveAdministrativa);
        documento.capabilityInvocation = [...capacidades];
        documento.documentVersion = Math.max(documento.documentVersion || 1, 2);
        documento.atualizadoEm = this._agora(ctx);
        await ctx.stub.putState(chaveDid, Buffer.from(JSON.stringify(documento)));
        return JSON.stringify(documento);
    }

    async RegistrarDidV2Pendente(ctx, comandoJson, assinatura) {
        this._garantirOrganizacaoAdministradora(ctx);
        const comando = this._lerComando(comandoJson, 'CustodyChainDidRegistration');
        this._validarJanelaComando(ctx, comando);
        this._validarComandoRegistro(comando);
        this._verificarAssinatura(comando, assinatura, comando.publicKeyMultibase);

        const chaveComando = ctx.stub.createCompositeKey(PREFIXO_GOVERNANCA, ['command', comando.commandId]);
        const comandoExistente = await ctx.stub.getState(chaveComando);
        if (comandoExistente && comandoExistente.length > 0) {
            const existente = JSON.parse(comandoExistente.toString());
            if (existente.hash !== this._hashComando(comando)) {
                throw new Error('Conflito de idempotência para o comando de registro DID.');
            }
            return this.ResolverDid(ctx, comando.did);
        }

        const chaveDid = ctx.stub.createCompositeKey(PREFIXO_DID, [comando.did]);
        const existente = await ctx.stub.getState(chaveDid);
        if (existente && existente.length > 0) {
            throw new Error(`DID já existe: ${comando.did}`);
        }

        const agora = this._agora(ctx);
        const documento = {
            id: comando.did,
            did: comando.did,
            metodoDid: comando.metodoDid,
            version: 2,
            documentVersion: 1,
            status: 'PENDENTE',
            ativo: false,
            controller: comando.did,
            verificationMethod: [{
                id: comando.verificationMethodId,
                type: 'Multikey',
                controller: comando.did,
                publicKeyMultibase: comando.publicKeyMultibase,
                status: 'PENDING',
                validFrom: null
            }],
            authentication: [comando.verificationMethodId],
            assertionMethod: [comando.verificationMethodId],
            capabilityInvocation: [comando.verificationMethodId],
            keySequence: 1,
            didEmissor: null,
            criadoEm: agora,
            enrollmentId: comando.enrollmentId
        };

        await ctx.stub.putState(chaveDid, Buffer.from(JSON.stringify(documento)));
        await ctx.stub.putState(chaveComando, Buffer.from(JSON.stringify({
            hash: this._hashComando(comando),
            did: comando.did,
            criadoEm: agora
        })));
        return JSON.stringify(documento);
    }

    async AtivarDidV2(ctx, comandoJson, keyId, assinatura) {
        this._garantirOrganizacaoAdministradora(ctx);
        const comando = this._lerComando(comandoJson, 'CustodyChainDidActivation');
        this._validarJanelaComando(ctx, comando);
        this._validarComandoAtivacao(comando, keyId);

        const chaveBootstrap = ctx.stub.createCompositeKey(PREFIXO_GOVERNANCA, ['admin-bootstrap']);
        const bootstrap = await ctx.stub.getState(chaveBootstrap);
        if (!bootstrap || bootstrap.length === 0 || JSON.parse(bootstrap.toString()).did !== comando.actorDid) {
            throw new Error('Somente o DID administrador de governança pode ativar identidades.');
        }

        const administrador = await this._obterDocumentoDid(ctx, comando.actorDid);
        if (administrador.status !== 'ATIVO' || !administrador.capabilityInvocation?.includes(keyId)) {
            throw new Error('DID administrador não possui capacidade para ativar identidades.');
        }
        const chaveAdministrativa = administrador.verificationMethod.find((metodo) => metodo.id === keyId);
        if (!chaveAdministrativa) {
            throw new Error('Método de verificação administrativo não encontrado.');
        }
        this._verificarAssinatura(comando, assinatura, chaveAdministrativa.publicKeyMultibase);

        const chaveComando = ctx.stub.createCompositeKey(PREFIXO_GOVERNANCA, ['command', comando.commandId]);
        const comandoExistente = await ctx.stub.getState(chaveComando);
        if (comandoExistente && comandoExistente.length > 0) {
            const existente = JSON.parse(comandoExistente.toString());
            if (existente.hash !== this._hashComando(comando)) {
                throw new Error('Conflito de idempotência para o comando de ativação DID.');
            }
            return this.ResolverDid(ctx, comando.subjectDid);
        }

        const chaveDid = ctx.stub.createCompositeKey(PREFIXO_DID, [comando.subjectDid]);
        const bytes = await ctx.stub.getState(chaveDid);
        if (!bytes || bytes.length === 0) {
            throw new Error(`DID não encontrado: ${comando.subjectDid}`);
        }
        const documento = JSON.parse(bytes.toString());
        if (documento.version !== 2 || documento.status !== 'PENDENTE'
            || documento.documentVersion !== comando.expectedDocumentVersion) {
            throw new Error('DID não está pendente na versão esperada para ativação.');
        }

        documento.status = 'ATIVO';
        documento.ativo = true;
        documento.didEmissor = comando.actorDid;
        documento.ativadoEm = this._agora(ctx);
        for (const metodo of documento.verificationMethod || []) {
            if (metodo.id === documento.authentication?.[0]) {
                metodo.status = 'ACTIVE';
                metodo.validFrom = documento.ativadoEm;
            }
        }
        documento.documentVersion += 1;
        await ctx.stub.putState(chaveDid, Buffer.from(JSON.stringify(documento)));
        await ctx.stub.putState(chaveComando, Buffer.from(JSON.stringify({
            hash: this._hashComando(comando),
            did: comando.subjectDid,
            criadoEm: documento.ativadoEm
        })));
        return JSON.stringify(documento);
    }

    async RotacionarChaveDidV2(ctx, comandoJson, assinaturaAtual, assinaturaNova) {
        const comando = this._lerComando(comandoJson, 'CustodyChainDidKeyRotation');
        this._validarComandoRotacao(comando);

        const documento = await this._obterDocumentoDid(ctx, comando.subjectDid);
        const metodoAtual = documento.verificationMethod?.find(
            (metodo) => metodo.id === comando.currentKeyId);
        if (!metodoAtual) {
            throw new Error('Método de verificação atual não encontrado no documento DID.');
        }

        this._verificarAssinatura(comando, assinaturaAtual, metodoAtual.publicKeyMultibase);
        this._verificarAssinatura(
            comando,
            assinaturaNova,
            comando.newVerificationMethod.publicKeyMultibase);

        const chaveComando = ctx.stub.createCompositeKey(PREFIXO_GOVERNANCA, ['command', comando.commandId]);
        const comandoExistente = await ctx.stub.getState(chaveComando);
        const hash = this._hashComando(comando);
        if (comandoExistente && comandoExistente.length > 0) {
            const existente = JSON.parse(comandoExistente.toString());
            if (existente.hash !== hash) {
                throw new Error('Conflito de idempotência para o comando de rotação DID.');
            }
            return this.ResolverDid(ctx, comando.subjectDid);
        }

        this._validarJanelaComando(ctx, comando);
        if (documento.version !== 2 || documento.status !== 'ATIVO' || documento.ativo !== true) {
            throw new Error('Somente um DID v2 ativo pode rotacionar sua chave.');
        }
        if (documento.documentVersion !== comando.expectedDocumentVersion) {
            throw new Error('A versão do documento DID mudou; reinicie a rotação.');
        }
        if (!documento.capabilityInvocation?.includes(comando.currentKeyId)) {
            throw new Error('A chave atual não possui capabilityInvocation para rotacionar o DID.');
        }
        if (documento.verificationMethod.some(
            (metodo) => metodo.id === comando.newVerificationMethod.id)) {
            throw new Error('A nova chave já existe no documento DID.');
        }

        const sequenciaAtual = this._obterSequenciaChave(documento);
        const prefixo = comando.currentKeyId.startsWith(`${comando.subjectDid}#auth-`) ? 'auth' : 'key';
        if (comando.newVerificationMethod.id !== `${comando.subjectDid}#${prefixo}-${sequenciaAtual + 1}`) {
            throw new Error('O identificador da nova chave não segue a sequência esperada.');
        }

        const agora = this._agora(ctx);
        metodoAtual.status = 'RETIRED';
        metodoAtual.validUntil = agora;
        metodoAtual.retirementReason = 'ROTATION';
        documento.verificationMethod.push({
            id: comando.newVerificationMethod.id,
            type: comando.newVerificationMethod.type,
            controller: comando.newVerificationMethod.controller,
            publicKeyMultibase: comando.newVerificationMethod.publicKeyMultibase,
            status: 'ACTIVE',
            validFrom: agora
        });
        documento.authentication = this._substituirCapacidade(
            documento.authentication, comando.currentKeyId, comando.newVerificationMethod.id);
        documento.assertionMethod = this._substituirCapacidade(
            documento.assertionMethod, comando.currentKeyId, comando.newVerificationMethod.id);
        documento.capabilityInvocation = this._substituirCapacidade(
            documento.capabilityInvocation, comando.currentKeyId, comando.newVerificationMethod.id);
        documento.keySequence = sequenciaAtual + 1;
        documento.documentVersion += 1;
        documento.atualizadoEm = agora;
        documento.ultimaRotacaoEm = agora;

        const chaveDid = ctx.stub.createCompositeKey(PREFIXO_DID, [comando.subjectDid]);
        await ctx.stub.putState(chaveDid, Buffer.from(JSON.stringify(documento)));
        await ctx.stub.putState(chaveComando, Buffer.from(JSON.stringify({
            hash,
            did: comando.subjectDid,
            criadoEm: agora,
            tipo: 'ROTACAO_CHAVE'
        })));
        return JSON.stringify(documento);
    }

    async RevogarDidV2(ctx, did) {
        this._garantirOrganizacaoAdministradora(ctx);
        const chave = ctx.stub.createCompositeKey(PREFIXO_DID, [did]);
        const bytes = await ctx.stub.getState(chave);
        if (!bytes || bytes.length === 0) {
            throw new Error(`DID não encontrado: ${did}`);
        }

        const documento = JSON.parse(bytes.toString());
        if (documento.version !== 2) {
            throw new Error('Somente documentos DID v2 podem ser revogados por esta operação.');
        }

        if (documento.status !== 'REVOGADO') {
            documento.status = 'REVOGADO';
            documento.ativo = false;
            documento.revogadoEm = this._agora(ctx);
            await ctx.stub.putState(chave, Buffer.from(JSON.stringify(documento)));
        }

        return JSON.stringify(documento);
    }

    async AtivarDid(ctx, did, didEmissor) {
        const chave = ctx.stub.createCompositeKey(PREFIXO_DID, [did]);
        const bytes = await ctx.stub.getState(chave);
        if (!bytes || bytes.length === 0) {
            throw new Error(`DID não encontrado: ${did}`);
        }

        const documento = JSON.parse(bytes.toString());
        documento.ativo = true;
        documento.didEmissor = didEmissor;
        documento.ativadoEm = this._agora(ctx);

        await ctx.stub.putState(chave, Buffer.from(JSON.stringify(documento)));
        return JSON.stringify(documento);
    }

    async ResolverDid(ctx, did) {
        const chave = ctx.stub.createCompositeKey(PREFIXO_DID, [did]);
        const bytes = await ctx.stub.getState(chave);
        if (!bytes || bytes.length === 0) {
            throw new Error(`DID não encontrado: ${did}`);
        }
        return bytes.toString();
    }

    async EmitirCredencialPermissao(ctx, credencialId, did, didEmissor, perfil) {
        throw new Error('Emissão legada de credencial de permissão desabilitada; use EmitirCredencialPermissaoV2 com VC assinada.');
    }

    async EmitirCredencialPermissaoV2(ctx, credencialJson) {
        const credencial = this._lerCredencialPermissaoV2(credencialJson);
        this._validarCredencialPermissaoV2(credencial);

        const chave = ctx.stub.createCompositeKey(PREFIXO_CREDENCIAL, [credencial.id]);
        const existente = await ctx.stub.getState(chave);
        const documentoEmissor = await this._obterDocumentoDid(ctx, credencial.issuer);
        const documentoTitular = await this._obterDocumentoDid(ctx, credencial.credentialSubject.id);
        const metodo = documentoEmissor.verificationMethod?.find(
            (item) => item.id === credencial.proof.verificationMethod);
        if (!metodo || !documentoEmissor.assertionMethod?.includes(metodo.id)) {
            throw new Error('A chave do emissor não possui capacidade assertionMethod para emitir VC.');
        }

        const { proof, ...credencialSemProva } = credencial;
        this._verificarAssinatura(credencialSemProva, proof.proofValue, metodo.publicKeyMultibase);

        const credentialHashSha256 = this._hashComando(credencialSemProva);
        if (existente && existente.length > 0) {
            const registroExistente = JSON.parse(existente.toString());
            const credencialExistenteSemProva = { ...(registroExistente.verifiableCredential || {}) };
            delete credencialExistenteSemProva.proof;
            const hashExistente = registroExistente.credentialHashSha256
                || this._hashComando(credencialExistenteSemProva);
            if (hashExistente === credentialHashSha256) {
                return credencial.id;
            }
            throw new Error(`Conflito de idempotência para a credencial: ${credencial.id}`);
        }

        if (documentoEmissor.version !== 2 || documentoEmissor.status !== 'ATIVO' || documentoEmissor.ativo !== true) {
            throw new Error('DID emissor não está ativo para emitir VC de permissão.');
        }
        if (documentoTitular.version !== 2 || documentoTitular.status !== 'ATIVO' || documentoTitular.ativo !== true) {
            throw new Error('DID titular não está ativo para receber VC de permissão.');
        }
        this._validarEscopoVcPermissao(credencial.credentialSubject, documentoTitular);

        const agora = this._agora(ctx);
        const registro = {
            credencialId: credencial.id,
            tipo: 'PERMISSAO',
            formato: 'VC_V1',
            did: credencial.credentialSubject.id,
            didEmissor: credencial.issuer,
            perfil: credencial.credentialSubject.perfil,
            processoId: credencial.credentialSubject.authorization?.processoId
                || credencial.credentialSubject.processoId || null,
            assetId: credencial.credentialSubject.authorization?.assetId || null,
            operacoes: credencial.credentialSubject.authorization?.operations || [],
            emitidaEm: credencial.issuanceDate,
            expiraEm: credencial.expirationDate || null,
            vcHashSha256: this._hashComando(credencial),
            credentialHashSha256,
            verifiableCredential: credencial,
            status: 'ATIVA',
            revogada: false,
            registradaEm: agora
        };
        await ctx.stub.putState(chave, Buffer.from(JSON.stringify(registro)));
        return credencial.id;
    }

    async EmitirCredencialCoC(ctx, credencialId, assetId, evento, did, payloadHashSha256) {
        throw new Error('Emissão de CoC descontinuada; use operações assinadas v1.');
    }

    async RegistrarOperacaoAssinadaV1(ctx, operacaoJson) {
        const operacao = this._lerOperacaoAssinadaV1(operacaoJson);
        this._validarOperacaoAssinadaV1(operacao);

        const chave = ctx.stub.createCompositeKey(PREFIXO_OPERACAO_ASSINADA, [operacao.operationId]);
        const existente = await ctx.stub.getState(chave);
        const documentoSignatario = await this._obterDocumentoDid(ctx, operacao.signerDid);
        const metodo = documentoSignatario.verificationMethod?.find((item) => item.id === operacao.keyId);
        if (!metodo) throw new Error('A chave do signatário não existe no documento DID.');

        const { signature, ...operacaoSemAssinatura } = operacao;
        this._verificarAssinatura(operacaoSemAssinatura, signature, metodo.publicKeyMultibase);
        const operationHashSha256 = this._hashComando(operacaoSemAssinatura);

        if (existente && existente.length > 0) {
            const registroExistente = JSON.parse(existente.toString());
            if (registroExistente.operationHashSha256 === operationHashSha256) {
                return operacao.operationId;
            }
            throw new Error(`Conflito de idempotência para a operação: ${operacao.operationId}`);
        }

        if (!documentoSignatario.capabilityInvocation?.includes(metodo.id)) {
            throw new Error('A chave do signatário não possui capabilityInvocation para publicar a operação.');
        }

        this._validarJanelaOperacaoAssinada(ctx, operacao);
        if (documentoSignatario.version !== 2 || documentoSignatario.status !== 'ATIVO'
            || documentoSignatario.ativo !== true) {
            throw new Error('DID do signatário não está ativo para publicar a operação.');
        }
        await this._validarAutorizacaoOperacaoAssinada(ctx, operacao);

        const registro = {
            operationId: operacao.operationId,
            formato: 'SIGNED_OPERATION_V1',
            operation: operacao.operation,
            signerDid: operacao.signerDid,
            keyId: operacao.keyId,
            timestamp: operacao.timestamp,
            expiresAt: operacao.expiresAt,
            nonce: operacao.nonce,
            operationHashSha256,
            signedOperation: operacao,
            registradaEm: this._agora(ctx)
        };
        await ctx.stub.putState(chave, Buffer.from(JSON.stringify(registro)));
        return operacao.operationId;
    }

    async ObterOperacaoAssinadaV1(ctx, operationId) {
        if (!this._identificadorValido(operationId)) {
            throw new Error('Identificador de operação inválido.');
        }

        const chave = ctx.stub.createCompositeKey(PREFIXO_OPERACAO_ASSINADA, [operationId]);
        const bytes = await ctx.stub.getState(chave);
        if (!bytes || bytes.length === 0) {
            throw new Error(`Operação assinada não encontrada: ${operationId}`);
        }
        return bytes.toString();
    }

    async VerificarCredencial(ctx, credencialId) {
        const chave = ctx.stub.createCompositeKey(PREFIXO_CREDENCIAL, [credencialId]);
        const bytes = await ctx.stub.getState(chave);

        if (!bytes || bytes.length === 0) {
            return JSON.stringify({ valido: false, motivo: 'Credencial não encontrada no ledger.' });
        }

        const credencial = JSON.parse(bytes.toString());
        if (credencial.revogada) {
            return JSON.stringify({ valido: false, motivo: 'Credencial revogada.' });
        }

        if (credencial.expiraEm && Date.parse(credencial.expiraEm) <= Date.parse(this._agora(ctx))) {
            return JSON.stringify({ valido: false, motivo: 'Credencial expirada.' });
        }

        if (credencial.tipo === 'PERMISSAO') {
            const didAtivo = await this._didEstaAtivo(ctx, credencial.did);
            if (!didAtivo) {
                return JSON.stringify({ valido: false, motivo: 'DID do titular não está ativo.' });
            }
        }

        return JSON.stringify({ valido: true, motivo: null });
    }

    async ObterCredencial(ctx, credencialId) {
        const chave = ctx.stub.createCompositeKey(PREFIXO_CREDENCIAL, [credencialId]);
        const bytes = await ctx.stub.getState(chave);
        if (!bytes || bytes.length === 0) {
            throw new Error(`Credencial não encontrada: ${credencialId}`);
        }
        return bytes.toString();
    }

    async RevogarCredencial(ctx, credencialId) {
        throw new Error('Revogação legada desabilitada; use RevogarCredencialV2 com prova assinada pelo emissor.');
    }

    async RevogarCredencialV2(ctx, comandoJson, keyId, assinatura) {
        const comando = this._lerComando(comandoJson, 'CustodyChainCredentialRevocation');
        this._validarJanelaComando(ctx, comando);
        this._validarComandoRevogacao(comando, keyId);
        const chave = ctx.stub.createCompositeKey(PREFIXO_CREDENCIAL, [comando.credentialId]);
        const bytes = await ctx.stub.getState(chave);
        if (!bytes || bytes.length === 0) {
            throw new Error(`Credencial não encontrada: ${comando.credentialId}`);
        }

        const credencial = JSON.parse(bytes.toString());
        if (credencial.formato !== 'VC_V1' || credencial.didEmissor !== comando.issuerDid) {
            throw new Error('A credencial não pode ser revogada por este emissor.');
        }

        const emissor = await this._obterDocumentoDid(ctx, comando.issuerDid);
        const metodo = emissor.verificationMethod?.find((item) => item.id === keyId);
        if (emissor.version !== 2 || emissor.status !== 'ATIVO' || emissor.ativo !== true
            || !metodo || !emissor.assertionMethod?.includes(keyId)) {
            throw new Error('A chave do emissor não possui capacidade assertionMethod para revogar VC.');
        }
        this._verificarAssinatura(comando, assinatura, metodo.publicKeyMultibase);

        if (credencial.revogada) {
            return JSON.stringify(credencial);
        }
        credencial.revogada = true;
        credencial.status = 'REVOGADA';
        credencial.revogadaEm = this._agora(ctx);

        await ctx.stub.putState(chave, Buffer.from(JSON.stringify(credencial)));
        return JSON.stringify(credencial);
    }

    async HistoricoRegistro(ctx, assetId) {
        const iterator = await ctx.stub.getStateByPartialCompositeKey(PREFIXO_HISTORICO, [assetId]);
        const eventos = [];

        let resultado = await iterator.next();
        while (!resultado.done) {
            if (resultado.value && resultado.value.value.length > 0) {
                eventos.push(JSON.parse(resultado.value.value.toString('utf8')));
            }
            resultado = await iterator.next();
        }
        await iterator.close();

        eventos.sort((a, b) => new Date(a.ocorridoEm) - new Date(b.ocorridoEm));
        return JSON.stringify(eventos);
    }

    async _didEstaAtivo(ctx, did) {
        const chave = ctx.stub.createCompositeKey(PREFIXO_DID, [did]);
        const bytes = await ctx.stub.getState(chave);
        if (!bytes || bytes.length === 0) {
            return false;
        }
        const documento = JSON.parse(bytes.toString());
        return documento.ativo === true;
    }

    async _garantirDidAtivo(ctx, did) {
        const ativo = await this._didEstaAtivo(ctx, did);
        if (!ativo) {
            throw new Error(`DID emissor não está ativo: ${did}`);
        }
    }

    async _obterDocumentoDid(ctx, did) {
        const chave = ctx.stub.createCompositeKey(PREFIXO_DID, [did]);
        const bytes = await ctx.stub.getState(chave);
        if (!bytes || bytes.length === 0) {
            throw new Error(`DID não encontrado: ${did}`);
        }
        return JSON.parse(bytes.toString());
    }

    _lerComando(comandoJson, tipoEsperado) {
        if (typeof comandoJson !== 'string' || comandoJson.length === 0 || comandoJson.length > 8192) {
            throw new Error('Comando DID inválido.');
        }
        let comando;
        try {
            comando = JSON.parse(comandoJson);
        } catch {
            throw new Error('Comando DID não contém JSON válido.');
        }
        if (!comando || typeof comando !== 'object' || Array.isArray(comando)
            || comando.type !== tipoEsperado || comando.version !== 1) {
            throw new Error('Tipo ou versão do comando DID inválido.');
        }
        return comando;
    }

    _lerCredencialPermissaoV2(credencialJson) {
        if (typeof credencialJson !== 'string' || credencialJson.length === 0 || credencialJson.length > 16384) {
            throw new Error('VC de permissão inválida.');
        }
        try {
            const credencial = JSON.parse(credencialJson);
            if (!credencial || typeof credencial !== 'object' || Array.isArray(credencial)) {
                throw new Error();
            }
            return credencial;
        } catch {
            throw new Error('VC de permissão não contém JSON válido.');
        }
    }

    _lerOperacaoAssinadaV1(operacaoJson) {
        if (typeof operacaoJson !== 'string' || operacaoJson.length === 0 || operacaoJson.length > 32768) {
            throw new Error('Operação assinada inválida.');
        }
        try {
            const operacao = JSON.parse(operacaoJson);
            if (!operacao || typeof operacao !== 'object' || Array.isArray(operacao)) {
                throw new Error();
            }
            return operacao;
        } catch {
            throw new Error('Operação assinada não contém JSON válido.');
        }
    }

    _validarOperacaoAssinadaV1(operacao) {
        if (operacao.type !== 'CustodyChainSignedOperation'
            || operacao.version !== 1
            || !this._identificadorValido(operacao.operationId)
            || !OPERACOES_ASSINADAS_SUPORTADAS.has(operacao.operation)
            || !operacao.payload || typeof operacao.payload !== 'object' || Array.isArray(operacao.payload)
            || !/^did:legal:(admin|custodian|delegate|expert|judge):[a-zA-Z0-9._-]{3,128}$/.test(operacao.signerDid)
            || typeof operacao.keyId !== 'string' || !operacao.keyId.startsWith(`${operacao.signerDid}#`)
            || operacao.algorithm !== 'Ed25519'
            || operacao.canonicalization !== 'custodychain-json-c14n-v1'
            || operacao.audience !== 'custodychain-ledger'
            || typeof operacao.timestamp !== 'string' || typeof operacao.expiresAt !== 'string'
            || !/^[A-Za-z0-9_-]{22,128}$/.test(operacao.nonce)
            || !/^[A-Za-z0-9_-]{86}$/.test(operacao.signature)
            || !this._valoresCanonicosDeOperacao(operacao)) {
            throw new Error('Envelope da operação assinada inválido.');
        }
    }

    _validarJanelaOperacaoAssinada(ctx, operacao) {
        const emitidaEm = Date.parse(operacao.timestamp);
        const expiraEm = Date.parse(operacao.expiresAt);
        const agora = Date.parse(this._agora(ctx));
        if (!Number.isFinite(emitidaEm) || !Number.isFinite(expiraEm)
            || expiraEm <= emitidaEm || expiraEm - emitidaEm > (10 * 60 * 1000)
            || expiraEm < agora || emitidaEm > agora + (2 * 60 * 1000)) {
            throw new Error('Janela temporal da operação assinada inválida ou expirada.');
        }
    }

    async _validarAutorizacaoOperacaoAssinada(ctx, operacao) {
        const payload = operacao.payload;
        if (operacao.operation === 'COLETA_REGISTRAR') {
            await this._validarAutorizacaoColeta(ctx, operacao);
            return;
        }

        if (operacao.operation === 'REMESSA_CRIAR') {
            await this._validarAutorizacaoRemessa(ctx, operacao);
            return;
        }

        if (operacao.operation === 'REMESSA_RECEBER' || operacao.operation === 'REMESSA_RECUSAR') {
            await this._validarRespostaRemessa(ctx, operacao);
            return;
        }

        if (operacao.operation === 'GUARDA_REGISTRAR') {
            await this._validarGuarda(ctx, operacao);
            return;
        }

        if (operacao.operation === 'DESTINACAO_SOLICITAR') {
            await this._validarSolicitacaoDestinacao(ctx, operacao);
            return;
        }

        if (operacao.operation === 'DESTINACAO_APROVAR') {
            await this._validarAprovacaoDestinacao(ctx, operacao);
            return;
        }

        if (operacao.operation === 'AMOSTRA_UNIFICAR') {
            await this._validarAutorizacaoUnificacao(ctx, operacao);
            return;
        }

        if (!this._identificadorValido(payload.credentialId)
            || !/^[1-9][0-9]*$/.test(payload.processoId)
            || !/^[1-9][0-9]*$/.test(payload.assetId)
            || !/^[1-9][0-9]*$/.test(payload.periciaId)) {
            throw new Error(`Payload da operação ${operacao.operation} inválido.`);
        }

        if (operacao.operation === 'LAUDO_EMITIR'
            && (typeof payload.numeroLaudo !== 'string' || !/^LAUDO-[0-9]{4}-[0-9]{6}$/.test(payload.numeroLaudo)
                || typeof payload.hashLaudo !== 'string' || !/^[a-f0-9]{64}$/.test(payload.hashLaudo)
                || typeof payload.hashVestigio !== 'string' || !/^[a-f0-9]{64}$/.test(payload.hashVestigio))) {
            throw new Error('Payload da operação LAUDO_EMITIR inválido.');
        }

        if (operacao.operation === 'LACRE_ROMPER'
            && (!/^[1-9][0-9]*$/.test(payload.lacreId)
                || typeof payload.numeroLacre !== 'string' || !payload.numeroLacre.trim()
                || typeof payload.justificativa !== 'string' || !payload.justificativa.trim())) {
            throw new Error('Payload da operação LACRE_ROMPER inválido.');
        }

        if ((operacao.operation === 'AMOSTRA_CONSUMIR' || operacao.operation === 'AMOSTRA_EXAURIR')
            && ((payload.quantidadeDescrita !== null && typeof payload.quantidadeDescrita !== 'string')
                || typeof payload.justificativa !== 'string' || !payload.justificativa.trim())) {
            throw new Error(`Payload da operação ${operacao.operation} inválido.`);
        }

        if (operacao.operation === 'AMOSTRA_FRACIONAR'
            && ((payload.hashVestigio !== null && !/^[a-f0-9]{64}$/.test(payload.hashVestigio))
                || typeof payload.rotuloEvidenciaResultante !== 'string' || !payload.rotuloEvidenciaResultante.trim()
                || typeof payload.descricaoResultante !== 'string' || !payload.descricaoResultante.trim()
                || (payload.quantidadeDescrita !== null && typeof payload.quantidadeDescrita !== 'string')
                || typeof payload.justificativa !== 'string' || !payload.justificativa.trim())) {
            throw new Error('Payload da operação AMOSTRA_FRACIONAR inválido.');
        }

        const chaveCredencial = ctx.stub.createCompositeKey(PREFIXO_CREDENCIAL, [payload.credentialId]);
        const bytes = await ctx.stub.getState(chaveCredencial);
        if (!bytes || bytes.length === 0) {
            throw new Error('VC de permissão não encontrada para a operação.');
        }

        const credencial = JSON.parse(bytes.toString());
        if (credencial.tipo !== 'PERMISSAO' || credencial.formato !== 'VC_V1'
            || credencial.status !== 'ATIVA' || credencial.revogada
            || credencial.did !== operacao.signerDid || credencial.perfil !== 'PERITO'
            || credencial.processoId !== payload.processoId || credencial.assetId !== payload.assetId
            || !Array.isArray(credencial.operacoes) || !credencial.operacoes.includes(operacao.operation)
            || (credencial.expiraEm && Date.parse(credencial.expiraEm) <= Date.parse(this._agora(ctx)))) {
            throw new Error(`VC de permissão não autoriza a operação: ${operacao.operation}`);
        }
    }

    async _validarAutorizacaoColeta(ctx, operacao) {
        const payload = operacao.payload;
        const textoObrigatorio = (valor) => typeof valor === 'string' && valor.trim();
        const textoOpcional = (valor) => valor === null || typeof valor === 'string';
        if (!this._identificadorValido(payload.credentialId)
            || !this._identificadorValido(payload.assetRef)
            || !/^[1-9][0-9]*$/.test(payload.processoId)
            || !/^[1-9][0-9]*$/.test(payload.tipoVestigioId)
            || !textoObrigatorio(payload.rotuloEvidencia)
            || !textoObrigatorio(payload.rotuloConjunto)
            || !textoObrigatorio(payload.descricao)
            || !textoObrigatorio(payload.numeroLacre)
            || !Number.isFinite(Date.parse(payload.dataHoraColeta))
            || !textoOpcional(payload.numeroEvidencia)
            || !textoOpcional(payload.localColeta)
            || !textoOpcional(payload.metodoColeta)
            || typeof payload.houveIntercorrencia !== 'boolean'
            || !textoOpcional(payload.descricaoIntercorrencia)
            || (payload.houveIntercorrencia && !textoObrigatorio(payload.descricaoIntercorrencia))
            || (!payload.houveIntercorrencia && payload.descricaoIntercorrencia !== null)
            || !this._integridadeDaColetaValida(payload.integrity)) {
            throw new Error('Payload da operação COLETA_REGISTRAR inválido.');
        }

        const chaveCredencial = ctx.stub.createCompositeKey(PREFIXO_CREDENCIAL, [payload.credentialId]);
        const bytes = await ctx.stub.getState(chaveCredencial);
        if (!bytes || bytes.length === 0) {
            throw new Error('VC de permissão não encontrada para a operação.');
        }

        const credencial = JSON.parse(bytes.toString());
        if (credencial.tipo !== 'PERMISSAO' || credencial.formato !== 'VC_V1'
            || credencial.status !== 'ATIVA' || credencial.revogada
            || credencial.did !== operacao.signerDid || credencial.perfil !== 'COLETOR'
            || credencial.processoId !== payload.processoId || credencial.assetId !== null
            || !Array.isArray(credencial.operacoes) || !credencial.operacoes.includes('COLETA_REGISTRAR')
            || (credencial.expiraEm && Date.parse(credencial.expiraEm) <= Date.parse(this._agora(ctx)))) {
            throw new Error('VC de permissão não autoriza a operação: COLETA_REGISTRAR');
        }
    }

    _integridadeDaColetaValida(integridade) {
        if (integridade === null) return true;
        return integridade && typeof integridade === 'object' && !Array.isArray(integridade)
            && integridade.algorithm === 'SHA-256'
            && /^[a-f0-9]{64}$/.test(integridade.contentHashSha256)
            && typeof integridade.contentCid === 'string' && integridade.contentCid.trim()
            && Number.isSafeInteger(integridade.byteLength) && integridade.byteLength > 0
            && typeof integridade.mediaType === 'string' && integridade.mediaType.trim()
            && typeof integridade.fileName === 'string' && integridade.fileName.trim()
            && integridade.mediaType.length <= 127 && integridade.fileName.length <= 255;
    }

    async _validarAutorizacaoRemessa(ctx, operacao) {
        if (operacao.payload.transferType === 'CUSTODIA') {
            await this._validarRemessaCustodia(ctx, operacao);
            return;
        }
        await this._validarTransferenciaInicial(ctx, operacao);
    }

    async _validarTransferenciaInicial(ctx, operacao) {
        const payload = operacao.payload;
        const textoObrigatorio = (valor) => typeof valor === 'string' && valor.trim();
        const textoOpcional = (valor) => valor === null || typeof valor === 'string';
        if (payload.transferType !== 'INICIAL'
            || !this._identificadorValido(payload.assetRef)
            || !/^[1-9][0-9]*$/.test(payload.assetId)
            || !/^[1-9][0-9]*$/.test(payload.processoId)
            || payload.origemDid !== operacao.signerDid
            || !/^did:legal:delegate:[a-zA-Z0-9._-]{3,128}$/.test(payload.origemDid)
            || !/^did:legal:custodian:[a-zA-Z0-9._-]{3,128}$/.test(payload.destinoDid)
            || !Number.isFinite(Date.parse(payload.dataHoraSaida))
            || !textoOpcional(payload.codigoRastreamento)
            || !this._identificadorValido(payload.coletaOperationId)) {
            throw new Error('Payload da operação REMESSA_CRIAR inicial inválido.');
        }

        const chaveTransferencia = ctx.stub.createCompositeKey(PREFIXO_TRANSFERENCIA_INICIAL, [payload.assetRef]);
        const transferenciaExistente = await ctx.stub.getState(chaveTransferencia);
        if (transferenciaExistente && transferenciaExistente.length > 0) {
            throw new Error('A transferência inicial deste ativo já foi registrada.');
        }

        const chaveColeta = ctx.stub.createCompositeKey(PREFIXO_OPERACAO_ASSINADA, [payload.coletaOperationId]);
        const bytesColeta = await ctx.stub.getState(chaveColeta);
        if (!bytesColeta || bytesColeta.length === 0) {
            throw new Error('A coleta assinada não foi encontrada para a transferência inicial.');
        }

        const coleta = JSON.parse(bytesColeta.toString()).signedOperation;
        if (coleta?.operation !== 'COLETA_REGISTRAR'
            || coleta.signerDid !== operacao.signerDid
            || coleta.payload?.assetRef !== payload.assetRef
            || coleta.payload?.processoId !== payload.processoId) {
            throw new Error('A coleta assinada não autoriza esta transferência inicial.');
        }

        const destino = await this._obterDocumentoDid(ctx, payload.destinoDid);
        if (destino.version !== 2 || destino.status !== 'ATIVO' || destino.ativo !== true
            || destino.metodoDid !== 'did:legal:custodian') {
            throw new Error('O DID de custódia inicial não está ativo.');
        }

        await ctx.stub.putState(chaveTransferencia, Buffer.from(JSON.stringify({
            assetRef: payload.assetRef,
            operationId: operacao.operationId,
            coletaOperationId: payload.coletaOperationId,
            origemDid: operacao.signerDid,
            destinoDid: payload.destinoDid,
            registradaEm: this._agora(ctx)
        })));
    }

    async _validarRemessaCustodia(ctx, operacao) {
        const payload = operacao.payload;
        const textoOpcional = (valor) => valor === null || typeof valor === 'string';
        if (payload.transferType !== 'CUSTODIA'
            || !this._identificadorValido(payload.assetRef)
            || !this._identificadorValido(payload.credentialId)
            || !/^[1-9][0-9]*$/.test(payload.assetId)
            || !/^[1-9][0-9]*$/.test(payload.processoId)
            || payload.origemDid !== operacao.signerDid
            || !/^did:legal:custodian:[a-zA-Z0-9._-]{3,128}$/.test(payload.origemDid)
            || !/^did:legal:custodian:[a-zA-Z0-9._-]{3,128}$/.test(payload.destinoDid)
            || !Number.isFinite(Date.parse(payload.dataHoraSaida))
            || !textoOpcional(payload.codigoRastreamento)
            || payload.coletaOperationId !== null) {
            throw new Error('Payload da operação REMESSA_CRIAR de custódia inválido.');
        }

        const chaveCredencial = ctx.stub.createCompositeKey(PREFIXO_CREDENCIAL, [payload.credentialId]);
        const bytesCredencial = await ctx.stub.getState(chaveCredencial);
        if (!bytesCredencial || bytesCredencial.length === 0) {
            throw new Error('VC de custódia não encontrada para a remessa.');
        }

        const credencial = JSON.parse(bytesCredencial.toString());
        if (credencial.tipo !== 'PERMISSAO' || credencial.formato !== 'VC_V1'
            || credencial.status !== 'ATIVA' || credencial.revogada
            || credencial.did !== operacao.signerDid || credencial.perfil !== 'CUSTODIA'
            || credencial.processoId !== payload.processoId || credencial.assetId !== payload.assetId
            || !Array.isArray(credencial.operacoes) || !credencial.operacoes.includes('REMESSA_CRIAR')
            || (credencial.expiraEm && Date.parse(credencial.expiraEm) <= Date.parse(this._agora(ctx)))) {
            throw new Error('VC de custódia não autoriza a operação: REMESSA_CRIAR');
        }

        const destino = await this._obterDocumentoDid(ctx, payload.destinoDid);
        if (destino.version !== 2 || destino.status !== 'ATIVO' || destino.ativo !== true
            || destino.metodoDid !== 'did:legal:custodian') {
            throw new Error('O DID de custódia destinatário não está ativo.');
        }
    }

    async _validarRespostaRemessa(ctx, operacao) {
        const payload = operacao.payload;
        const textoObrigatorio = (valor) => typeof valor === 'string' && valor.trim();
        const textoOpcional = (valor) => valor === null || typeof valor === 'string';
        if (!this._identificadorValido(payload.credentialId)
            || !this._identificadorValido(payload.remessaOperationId)
            || !this._identificadorValido(payload.assetRef)
            || !/^[1-9][0-9]*$/.test(payload.assetId)
            || !/^[1-9][0-9]*$/.test(payload.processoId)
            || !/^did:legal:custodian:[a-zA-Z0-9._-]{3,128}$/.test(payload.origemDid)
            || payload.destinoDid !== operacao.signerDid
            || !/^did:legal:custodian:[a-zA-Z0-9._-]{3,128}$/.test(payload.destinoDid)) {
            throw new Error(`Payload da operação ${operacao.operation} inválido.`);
        }

        if (operacao.operation === 'REMESSA_RECEBER'
            && (!textoOpcional(payload.numeroLacreEsperado)
                || !textoObrigatorio(payload.numeroLacreConferido)
                || typeof payload.lacreConfere !== 'boolean'
                || payload.lacreConfere !== (payload.numeroLacreEsperado !== null
                    && payload.numeroLacreEsperado === payload.numeroLacreConferido))) {
            throw new Error('Conferência de lacre da operação REMESSA_RECEBER inválida.');
        }
        if (operacao.operation === 'REMESSA_RECUSAR' && !textoObrigatorio(payload.motivoRecusa)) {
            throw new Error('Motivo da operação REMESSA_RECUSAR inválido.');
        }

        const chaveResposta = ctx.stub.createCompositeKey(PREFIXO_RESPOSTA_REMESSA, [payload.remessaOperationId]);
        const respostaExistente = await ctx.stub.getState(chaveResposta);
        if (respostaExistente && respostaExistente.length > 0) {
            throw new Error('Esta remessa já possui recebimento ou recusa registrada.');
        }

        const chaveRemessa = ctx.stub.createCompositeKey(PREFIXO_OPERACAO_ASSINADA, [payload.remessaOperationId]);
        const bytesRemessa = await ctx.stub.getState(chaveRemessa);
        if (!bytesRemessa || bytesRemessa.length === 0) {
            throw new Error('A remessa assinada não foi encontrada.');
        }
        const remessa = JSON.parse(bytesRemessa.toString()).signedOperation;
        if (remessa?.operation !== 'REMESSA_CRIAR'
            || remessa.payload?.assetRef !== payload.assetRef
            || remessa.payload?.assetId !== payload.assetId
            || remessa.payload?.processoId !== payload.processoId
            || remessa.payload?.origemDid !== payload.origemDid
            || remessa.payload?.destinoDid !== operacao.signerDid) {
            throw new Error('A remessa assinada não autoriza esta resposta.');
        }

        await this._validarCredencialCustodia(ctx, operacao, payload);
        await ctx.stub.putState(chaveResposta, Buffer.from(JSON.stringify({
            remessaOperationId: payload.remessaOperationId,
            operationId: operacao.operationId,
            operation: operacao.operation,
            respondidaEm: this._agora(ctx)
        })));
    }

    async _validarCredencialCustodia(ctx, operacao, payload) {
        const chaveCredencial = ctx.stub.createCompositeKey(PREFIXO_CREDENCIAL, [payload.credentialId]);
        const bytesCredencial = await ctx.stub.getState(chaveCredencial);
        if (!bytesCredencial || bytesCredencial.length === 0) {
            throw new Error('VC de custódia não encontrada para a operação.');
        }
        const credencial = JSON.parse(bytesCredencial.toString());
        if (credencial.tipo !== 'PERMISSAO' || credencial.formato !== 'VC_V1'
            || credencial.status !== 'ATIVA' || credencial.revogada
            || credencial.did !== operacao.signerDid || credencial.perfil !== 'CUSTODIA'
            || credencial.processoId !== payload.processoId || credencial.assetId !== payload.assetId
            || !Array.isArray(credencial.operacoes) || !credencial.operacoes.includes(operacao.operation)
            || (credencial.expiraEm && Date.parse(credencial.expiraEm) <= Date.parse(this._agora(ctx)))) {
            throw new Error(`VC de custódia não autoriza a operação: ${operacao.operation}`);
        }
    }

    async _validarGuarda(ctx, operacao) {
        const payload = operacao.payload;
        const textoObrigatorio = (valor) => typeof valor === 'string' && valor.trim();
        const textoOpcional = (valor) => valor === null || typeof valor === 'string';
        if (!this._identificadorValido(payload.credentialId)
            || !this._identificadorValido(payload.recebimentoOperationId)
            || !this._identificadorValido(payload.assetRef)
            || !/^[1-9][0-9]*$/.test(payload.assetId)
            || !/^[1-9][0-9]*$/.test(payload.processoId)
            || !textoObrigatorio(payload.central)
            || !textoOpcional(payload.posicao)
            || (payload.prazoGuardaAte !== null && (!/^\d{4}-\d{2}-\d{2}$/.test(payload.prazoGuardaAte)
                || !Number.isFinite(Date.parse(`${payload.prazoGuardaAte}T00:00:00.000Z`))))) {
            throw new Error('Payload da operação GUARDA_REGISTRAR inválido.');
        }

        const chaveGuarda = ctx.stub.createCompositeKey(PREFIXO_GUARDA, [payload.recebimentoOperationId]);
        const guardaExistente = await ctx.stub.getState(chaveGuarda);
        if (guardaExistente && guardaExistente.length > 0) {
            throw new Error('Este recebimento já possui guarda registrada.');
        }

        const chaveRecebimento = ctx.stub.createCompositeKey(PREFIXO_OPERACAO_ASSINADA, [payload.recebimentoOperationId]);
        const bytesRecebimento = await ctx.stub.getState(chaveRecebimento);
        if (!bytesRecebimento || bytesRecebimento.length === 0) {
            throw new Error('O recebimento assinado não foi encontrado para a guarda.');
        }
        const recebimento = JSON.parse(bytesRecebimento.toString()).signedOperation;
        if (recebimento?.operation !== 'REMESSA_RECEBER'
            || recebimento.signerDid !== operacao.signerDid
            || recebimento.payload?.assetRef !== payload.assetRef
            || recebimento.payload?.assetId !== payload.assetId
            || recebimento.payload?.processoId !== payload.processoId) {
            throw new Error('O recebimento assinado não autoriza esta guarda.');
        }

        await this._validarCredencialCustodia(ctx, operacao, payload);
        await ctx.stub.putState(chaveGuarda, Buffer.from(JSON.stringify({
            recebimentoOperationId: payload.recebimentoOperationId,
            operationId: operacao.operationId,
            guardadaEm: this._agora(ctx)
        })));
    }

    async _validarSolicitacaoDestinacao(ctx, operacao) {
        const payload = operacao.payload;
        const textoObrigatorio = (valor) => typeof valor === 'string' && valor.trim();
        const textoOpcional = (valor) => valor === null || typeof valor === 'string';
        if (!this._identificadorValido(payload.credentialId)
            || !this._identificadorValido(payload.guardaOperationId)
            || !this._identificadorValido(payload.assetRef)
            || !/^[1-9][0-9]*$/.test(payload.assetId)
            || !/^[1-9][0-9]*$/.test(payload.processoId)
            || !['DESCARTE', 'RESTITUICAO'].includes(payload.tipo)
            || !/^did:legal:judge:[a-zA-Z0-9._-]{3,128}$/.test(payload.didMagistrado)
            || !textoObrigatorio(payload.autorizacaoCid)
            || typeof payload.autorizacaoHashSha256 !== 'string' || !/^[a-f0-9]{64}$/.test(payload.autorizacaoHashSha256)
            || !textoObrigatorio(payload.autorizacaoNomeArquivo)
            || !Number.isSafeInteger(payload.autorizacaoTamanhoBytes) || payload.autorizacaoTamanhoBytes <= 0
            || !textoOpcional(payload.observacao)) {
            throw new Error('Payload da operação DESTINACAO_SOLICITAR inválido.');
        }

        const chaveDestinacao = ctx.stub.createCompositeKey(PREFIXO_DESTINACAO_SOLICITACAO, [payload.assetRef]);
        const solicitacaoExistente = await ctx.stub.getState(chaveDestinacao);
        if (solicitacaoExistente && solicitacaoExistente.length > 0) {
            throw new Error('Este ativo já possui solicitação de destinação final registrada.');
        }

        const chaveGuarda = ctx.stub.createCompositeKey(PREFIXO_OPERACAO_ASSINADA, [payload.guardaOperationId]);
        const bytesGuarda = await ctx.stub.getState(chaveGuarda);
        if (!bytesGuarda || bytesGuarda.length === 0) {
            throw new Error('A guarda assinada não foi encontrada para a destinação final.');
        }
        const guarda = JSON.parse(bytesGuarda.toString()).signedOperation;
        if (guarda?.operation !== 'GUARDA_REGISTRAR'
            || guarda.signerDid !== operacao.signerDid
            || guarda.payload?.assetRef !== payload.assetRef
            || guarda.payload?.assetId !== payload.assetId
            || guarda.payload?.processoId !== payload.processoId) {
            throw new Error('A guarda assinada não autoriza esta solicitação de destinação final.');
        }

        await this._validarCredencialCustodia(ctx, operacao, payload);
        await ctx.stub.putState(chaveDestinacao, Buffer.from(JSON.stringify({
            assetRef: payload.assetRef,
            operationId: operacao.operationId,
            solicitanteDid: operacao.signerDid,
            solicitadaEm: this._agora(ctx)
        })));
    }

    async _validarAprovacaoDestinacao(ctx, operacao) {
        const payload = operacao.payload;
        if (!this._identificadorValido(payload.destinacaoOperationId)
            || !this._identificadorValido(payload.assetRef)
            || !/^[1-9][0-9]*$/.test(payload.assetId)
            || !/^[1-9][0-9]*$/.test(payload.processoId)
            || !['DESCARTE', 'RESTITUICAO'].includes(payload.tipo)
            || typeof payload.autorizacaoCid !== 'string' || !payload.autorizacaoCid.trim()
            || typeof payload.autorizacaoHashSha256 !== 'string' || !/^[a-f0-9]{64}$/.test(payload.autorizacaoHashSha256)) {
            throw new Error('Payload da operação DESTINACAO_APROVAR inválido.');
        }

        this._garantirOrganizacaoAdministradora(ctx);
        const aprovador = await this._obterDocumentoDid(ctx, operacao.signerDid);
        if (aprovador.metodoDid !== 'did:legal:admin') {
            throw new Error('Somente DID administrativo pode aprovar destinação final.');
        }

        const chaveAprovacao = ctx.stub.createCompositeKey(PREFIXO_DESTINACAO_APROVACAO, [payload.destinacaoOperationId]);
        const aprovacaoExistente = await ctx.stub.getState(chaveAprovacao);
        if (aprovacaoExistente && aprovacaoExistente.length > 0) {
            throw new Error('Esta solicitação de destinação final já foi aprovada.');
        }

        const chaveSolicitacao = ctx.stub.createCompositeKey(PREFIXO_OPERACAO_ASSINADA, [payload.destinacaoOperationId]);
        const bytesSolicitacao = await ctx.stub.getState(chaveSolicitacao);
        if (!bytesSolicitacao || bytesSolicitacao.length === 0) {
            throw new Error('A solicitação assinada de destinação final não foi encontrada.');
        }
        const solicitacao = JSON.parse(bytesSolicitacao.toString()).signedOperation;
        if (solicitacao?.operation !== 'DESTINACAO_SOLICITAR'
            || solicitacao.signerDid === operacao.signerDid
            || solicitacao.payload?.assetRef !== payload.assetRef
            || solicitacao.payload?.assetId !== payload.assetId
            || solicitacao.payload?.processoId !== payload.processoId
            || solicitacao.payload?.tipo !== payload.tipo
            || solicitacao.payload?.autorizacaoCid !== payload.autorizacaoCid
            || solicitacao.payload?.autorizacaoHashSha256 !== payload.autorizacaoHashSha256) {
            throw new Error('A solicitação assinada não autoriza esta aprovação de destinação final.');
        }

        await ctx.stub.putState(chaveAprovacao, Buffer.from(JSON.stringify({
            destinacaoOperationId: payload.destinacaoOperationId,
            operationId: operacao.operationId,
            aprovadorDid: operacao.signerDid,
            aprovadaEm: this._agora(ctx)
        })));
    }

    async _validarAutorizacaoUnificacao(ctx, operacao) {
        const payload = operacao.payload;
        if (!/^[1-9][0-9]*$/.test(payload.periciaId)
            || !Array.isArray(payload.origens) || payload.origens.length < 2 || payload.origens.length > 100
            || typeof payload.rotuloEvidenciaResultante !== 'string' || !payload.rotuloEvidenciaResultante.trim()
            || typeof payload.descricaoResultante !== 'string' || !payload.descricaoResultante.trim()
            || typeof payload.justificativa !== 'string' || !payload.justificativa.trim()) {
            throw new Error('Payload da operação AMOSTRA_UNIFICAR inválido.');
        }

        const assetIds = new Set();
        const processoIds = new Set();
        for (const origem of payload.origens) {
            if (!origem || typeof origem !== 'object' || Array.isArray(origem)
                || !this._identificadorValido(origem.credentialId)
                || !/^[1-9][0-9]*$/.test(origem.processoId)
                || !/^[1-9][0-9]*$/.test(origem.assetId)
                || (origem.hashVestigio !== null && !/^[a-f0-9]{64}$/.test(origem.hashVestigio))
                || assetIds.has(origem.assetId)) {
                throw new Error('Origem da operação AMOSTRA_UNIFICAR inválida.');
            }
            assetIds.add(origem.assetId);
            processoIds.add(origem.processoId);
            await this._validarCredencialDaOperacao(ctx, operacao, origem);
        }

        if (processoIds.size !== 1) {
            throw new Error('As origens da operação AMOSTRA_UNIFICAR pertencem a processos diferentes.');
        }
    }

    async _validarCredencialDaOperacao(ctx, operacao, escopo) {
        const chaveCredencial = ctx.stub.createCompositeKey(PREFIXO_CREDENCIAL, [escopo.credentialId]);
        const bytes = await ctx.stub.getState(chaveCredencial);
        if (!bytes || bytes.length === 0) {
            throw new Error('VC de permissão não encontrada para a operação.');
        }

        const credencial = JSON.parse(bytes.toString());
        if (credencial.tipo !== 'PERMISSAO' || credencial.formato !== 'VC_V1'
            || credencial.status !== 'ATIVA' || credencial.revogada
            || credencial.did !== operacao.signerDid || credencial.perfil !== 'PERITO'
            || credencial.processoId !== escopo.processoId || credencial.assetId !== escopo.assetId
            || !Array.isArray(credencial.operacoes) || !credencial.operacoes.includes(operacao.operation)
            || (credencial.expiraEm && Date.parse(credencial.expiraEm) <= Date.parse(this._agora(ctx)))) {
            throw new Error(`VC de permissão não autoriza a operação: ${operacao.operation}`);
        }
    }

    _valoresCanonicosDeOperacao(valor) {
        if (valor === null || typeof valor === 'string' || typeof valor === 'boolean') return true;
        if (typeof valor === 'number') return Number.isSafeInteger(valor);
        if (Array.isArray(valor)) return valor.every((item) => this._valoresCanonicosDeOperacao(item));
        if (typeof valor === 'object') return Object.values(valor)
            .every((item) => this._valoresCanonicosDeOperacao(item));
        return false;
    }

    _validarCredencialPermissaoV2(credencial) {
        const subject = credencial.credentialSubject;
        const status = credencial.credentialStatus;
        const proof = credencial.proof;
        const authorization = subject?.authorization;
        const operacoesPermitidas = new Set([
            'PERICIA_RECEBER',
            'LACRE_ROMPER',
            'LAUDO_EMITIR',
            'AMOSTRA_FRACIONAR',
            'AMOSTRA_UNIFICAR',
            'AMOSTRA_CONSUMIR',
            'AMOSTRA_EXAURIR',
            'COLETA_REGISTRAR',
            'REMESSA_CRIAR',
            'REMESSA_RECEBER',
            'REMESSA_RECUSAR',
            'GUARDA_REGISTRAR',
            'DESTINACAO_SOLICITAR'
        ]);
        const escopoInvalido = authorization !== undefined && (!authorization
            || typeof authorization !== 'object' || Array.isArray(authorization)
            || !/^[1-9][0-9]*$/.test(authorization.processoId)
            || (authorization.assetId !== undefined && !/^[1-9][0-9]*$/.test(authorization.assetId))
            || !Array.isArray(authorization.operations) || authorization.operations.length === 0
            || new Set(authorization.operations).size !== authorization.operations.length
            || authorization.operations.some((operacao) => !operacoesPermitidas.has(operacao)));
        if (!this._identificadorValido(credencial.id)
            || !Array.isArray(credencial['@context'])
            || !credencial['@context'].includes('https://www.w3.org/2018/credentials/v1')
            || !Array.isArray(credencial.type)
            || !credencial.type.includes('VerifiableCredential')
            || !credencial.type.includes('CustodyChainPermissionCredential')
            || !/^did:legal:admin:[a-zA-Z0-9._-]{3,128}$/.test(credencial.issuer)
            || !Number.isFinite(Date.parse(credencial.issuanceDate))
            || (credencial.expirationDate && (!Number.isFinite(Date.parse(credencial.expirationDate))
                || Date.parse(credencial.expirationDate) <= Date.parse(credencial.issuanceDate)))
            || !subject || typeof subject !== 'object'
            || !/^did:legal:(admin|custodian|delegate|expert|judge):[a-zA-Z0-9._-]{3,128}$/.test(subject.id)
            || typeof subject.perfil !== 'string' || !/^[A-Z_]{3,20}$/.test(subject.perfil)
            || escopoInvalido
            || !status || status.id !== `${credencial.id}#status` || status.type !== 'CustodyChainLedgerStatusV1'
            || !proof || proof.type !== 'CustodyChainEd25519Signature2026'
            || proof.proofPurpose !== 'assertionMethod'
            || proof.canonicalization !== 'custodychain-json-c14n-v1'
            || typeof proof.verificationMethod !== 'string' || !proof.verificationMethod.startsWith(`${credencial.issuer}#`)
            || !Number.isFinite(Date.parse(proof.created))
            || typeof proof.proofValue !== 'string') {
            throw new Error('Envelope da VC de permissão inválido.');
        }
    }

    _validarEscopoVcPermissao(subject, documentoTitular) {
        const authorization = subject.authorization;
        if (!authorization) return;

        const operacoes = authorization.operations;
        const perfilValido = (perfil, metodoDid, operacoesEsperadas, exigeAtivo) =>
            subject.perfil === perfil
            && documentoTitular.metodoDid === metodoDid
            && operacoes.every((operacao) => operacoesEsperadas.includes(operacao))
            && (exigeAtivo ? /^[1-9][0-9]*$/.test(authorization.assetId) : authorization.assetId === undefined);

        const perito = perfilValido('PERITO', 'did:legal:expert', [
            'PERICIA_RECEBER', 'LACRE_ROMPER', 'LAUDO_EMITIR', 'AMOSTRA_FRACIONAR',
            'AMOSTRA_UNIFICAR', 'AMOSTRA_CONSUMIR', 'AMOSTRA_EXAURIR'
        ], true);
        const coletor = perfilValido('COLETOR', 'did:legal:delegate', ['COLETA_REGISTRAR'], false);
        const custodia = perfilValido('CUSTODIA', 'did:legal:custodian', [
            'REMESSA_CRIAR', 'REMESSA_RECEBER', 'REMESSA_RECUSAR', 'GUARDA_REGISTRAR', 'DESTINACAO_SOLICITAR'
        ], true);
        if (!perito && !coletor && !custodia) {
            throw new Error('Perfil, DID ou escopo da VC de permissão não é autorizado.');
        }
    }

    _validarJanelaComando(ctx, comando) {
        if (!this._identificadorValido(comando.commandId)
            || comando.audience !== 'custodychain-ledger'
            || typeof comando.issuedAt !== 'string' || typeof comando.expiresAt !== 'string') {
            throw new Error('Metadados do comando DID inválidos.');
        }
        const emitidoEm = Date.parse(comando.issuedAt);
        const expiraEm = Date.parse(comando.expiresAt);
        const agora = Date.parse(this._agora(ctx));
        if (!Number.isFinite(emitidoEm) || !Number.isFinite(expiraEm)
            || expiraEm <= emitidoEm || expiraEm < agora || emitidoEm > agora + (2 * 60 * 1000)) {
            throw new Error('Janela temporal do comando DID inválida ou expirada.');
        }
    }

    _validarComandoRegistro(comando) {
        if (!/^did:legal:(admin|custodian|delegate|expert|judge):[a-zA-Z0-9._-]{3,128}$/.test(comando.did)
            || comando.metodoDid !== comando.did.split(':').slice(0, 3).join(':')
            || comando.verificationMethodId !== `${comando.did}#key-1`
            || !this._identificadorValido(comando.enrollmentId)
            || !/^z[1-9A-HJ-NP-Za-km-z]{40,64}$/.test(comando.publicKeyMultibase)) {
            throw new Error('Conteúdo do comando de registro DID inválido.');
        }
    }

    _validarComandoAtivacao(comando, keyId) {
        if (!/^did:legal:admin:[a-zA-Z0-9._-]{3,128}$/.test(comando.actorDid)
            || !/^did:legal:(admin|custodian|delegate|expert|judge):[a-zA-Z0-9._-]{3,128}$/.test(comando.subjectDid)
            || typeof keyId !== 'string' || !keyId.startsWith(`${comando.actorDid}#`)
            || !Number.isInteger(comando.expectedDocumentVersion) || comando.expectedDocumentVersion < 1) {
            throw new Error('Conteúdo do comando de ativação DID inválido.');
        }
    }

    _validarComandoRotacao(comando) {
        const novoMetodo = comando.newVerificationMethod;
        if (!/^did:legal:(admin|custodian|delegate|expert|judge):[a-zA-Z0-9._-]{3,128}$/.test(comando.subjectDid)
            || !Number.isInteger(comando.expectedDocumentVersion) || comando.expectedDocumentVersion < 1
            || typeof comando.currentKeyId !== 'string'
            || !comando.currentKeyId.startsWith(`${comando.subjectDid}#`)
            || !novoMetodo || typeof novoMetodo !== 'object' || Array.isArray(novoMetodo)
            || typeof novoMetodo.id !== 'string' || !novoMetodo.id.startsWith(`${comando.subjectDid}#`)
            || novoMetodo.type !== 'Multikey' || novoMetodo.controller !== comando.subjectDid
            || !this._chavePublicaMultibaseValida(novoMetodo.publicKeyMultibase)
            || comando.algorithm !== 'Ed25519'
            || comando.canonicalization !== 'custodychain-json-c14n-v1'
            || typeof comando.nonce !== 'string' || !/^[A-Za-z0-9_-]{22,128}$/.test(comando.nonce)) {
            throw new Error('Conteúdo do comando de rotação DID inválido.');
        }
    }

    _validarComandoRevogacao(comando, keyId) {
        if (!this._identificadorValido(comando.credentialId)
            || !/^did:legal:admin:[a-zA-Z0-9._-]{3,128}$/.test(comando.issuerDid)
            || typeof keyId !== 'string' || !keyId.startsWith(`${comando.issuerDid}#`)) {
            throw new Error('Conteúdo do comando de revogação de VC inválido.');
        }
    }

    _verificarAssinatura(comando, assinaturaBase64Url, publicKeyMultibase) {
        if (typeof assinaturaBase64Url !== 'string' || !/^[A-Za-z0-9_-]{80,128}$/.test(assinaturaBase64Url)) {
            throw new Error('Assinatura DID inválida.');
        }
        const chaveMulticodec = decodificarMultibaseEd25519(publicKeyMultibase);
        const chaveSpki = Buffer.concat([
            Buffer.from('302a300506032b6570032100', 'hex'),
            chaveMulticodec.subarray(2)
        ]);
        const chavePublica = crypto.createPublicKey({ key: chaveSpki, format: 'der', type: 'spki' });
        const assinatura = Buffer.from(assinaturaBase64Url, 'base64url');
        if (assinatura.length !== 64 || !crypto.verify(null, Buffer.from(canonicalizarJson(comando)), chavePublica, assinatura)) {
            throw new Error('A assinatura do comando DID é inválida.');
        }
    }

    _hashComando(comando) {
        return crypto.createHash('sha256').update(canonicalizarJson(comando)).digest('hex');
    }

    _identificadorValido(valor) {
        return typeof valor === 'string' && /^urn:uuid:[0-9a-fA-F-]{36}$/.test(valor);
    }

    _obterSequenciaChave(documento) {
        if (Number.isInteger(documento.keySequence) && documento.keySequence > 0) {
            return documento.keySequence;
        }
        return Math.max(1, ...(documento.verificationMethod || []).map((metodo) => {
            const resultado = /#(?:key|auth)-(\d+)$/.exec(metodo.id);
            return resultado ? Number(resultado[1]) : 0;
        }));
    }

    _substituirCapacidade(capacidades, chaveAtual, chaveNova) {
        const resultado = (capacidades || []).filter((chave) => chave !== chaveAtual);
        if ((capacidades || []).includes(chaveAtual)) resultado.push(chaveNova);
        return resultado;
    }

    _chavePublicaMultibaseValida(publicKeyMultibase) {
        return typeof publicKeyMultibase === 'string'
            && /^z[1-9A-HJ-NP-Za-km-z]{40,64}$/.test(publicKeyMultibase);
    }

    _garantirOrganizacaoAdministradora(ctx) {
        const mspId = ctx.clientIdentity.getMSPID();
        if (mspId !== MSP_ADMINISTRADOR) {
            throw new Error(`MSP não autorizado para governança de identidade: ${mspId}`);
        }
    }

    _validarDocumentoDidV2(did, verificationMethodId, publicKeyMultibase) {
        if (!/^did:legal:admin:[a-zA-Z0-9._-]{3,128}$/.test(did)) {
            throw new Error('DID administrador inválido.');
        }
        if (verificationMethodId !== `${did}#auth-1`) {
            throw new Error('Identificador do método de verificação inválido.');
        }
        if (!this._chavePublicaMultibaseValida(publicKeyMultibase)) {
            throw new Error('Chave pública Multikey inválida.');
        }
    }
}

function canonicalizarJson(valor) {
    if (valor === null || typeof valor === 'string' || typeof valor === 'boolean') {
        return JSON.stringify(valor);
    }
    if (typeof valor === 'number') {
        if (!Number.isFinite(valor)) {
            throw new Error('Número não finito não pode integrar comando DID.');
        }
        return JSON.stringify(valor);
    }
    if (Array.isArray(valor)) {
        return `[${valor.map(canonicalizarJson).join(',')}]`;
    }
    if (typeof valor === 'object') {
        return `{${Object.keys(valor).sort().map((chave) =>
            `${JSON.stringify(chave)}:${canonicalizarJson(valor[chave])}`).join(',')}}`;
    }
    throw new Error('Tipo inválido no comando DID.');
}

function decodificarMultibaseEd25519(valor) {
    if (typeof valor !== 'string' || !valor.startsWith('z')) {
        throw new Error('Chave pública Multikey inválida.');
    }
    const alfabeto = '123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz';
    const bytes = [0];
    for (const caractere of valor.slice(1)) {
        let transporte = alfabeto.indexOf(caractere);
        if (transporte < 0) {
            throw new Error('Chave pública Multikey inválida.');
        }
        for (let indice = 0; indice < bytes.length; indice += 1) {
            transporte += bytes[indice] * 58;
            bytes[indice] = transporte & 0xff;
            transporte >>= 8;
        }
        while (transporte > 0) {
            bytes.push(transporte & 0xff);
            transporte >>= 8;
        }
    }
    const resultado = Buffer.from(bytes.reverse());
    if (resultado.length !== 34 || resultado[0] !== 0xed || resultado[1] !== 0x01) {
        throw new Error('Chave pública Ed25519 Multikey inválida.');
    }
    return resultado;
}

module.exports = CustodyChainContract;
