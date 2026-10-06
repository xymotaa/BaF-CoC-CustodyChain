'use strict';

const crypto = require('node:crypto');
const { Contract } = require('fabric-contract-api');

const PREFIXO_DID = 'DID';
const PREFIXO_CREDENCIAL = 'CRED';
const PREFIXO_HISTORICO = 'HIST';
const PREFIXO_GOVERNANCA = 'GOV';
const MSP_ADMINISTRADOR = 'Org1MSP';

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
                publicKeyMultibase
            }],
            authentication: [verificationMethodId],
            assertionMethod: [verificationMethodId],
            capabilityInvocation: [verificationMethodId],
            documentVersion: 1,
            didEmissor: null,
            criadoEm: agora,
            ativadoEm: agora
        };

        await ctx.stub.putState(chaveDid, Buffer.from(JSON.stringify(documento)));
        await ctx.stub.putState(chaveBootstrap, Buffer.from(JSON.stringify({ did, criadoEm: agora })));
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
                publicKeyMultibase: comando.publicKeyMultibase
            }],
            authentication: [comando.verificationMethodId],
            assertionMethod: [comando.verificationMethodId],
            capabilityInvocation: [comando.verificationMethodId],
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
        documento.documentVersion += 1;
        await ctx.stub.putState(chaveDid, Buffer.from(JSON.stringify(documento)));
        await ctx.stub.putState(chaveComando, Buffer.from(JSON.stringify({
            hash: this._hashComando(comando),
            did: comando.subjectDid,
            criadoEm: documento.ativadoEm
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
        await this._garantirDidAtivo(ctx, didEmissor);

        const credencial = {
            credencialId,
            tipo: 'PERMISSAO',
            did,
            didEmissor,
            perfil,
            emitidaEm: this._agora(ctx),
            revogada: false
        };

        const chave = ctx.stub.createCompositeKey(PREFIXO_CREDENCIAL, [credencialId]);
        await ctx.stub.putState(chave, Buffer.from(JSON.stringify(credencial)));
        return credencialId;
    }

    async EmitirCredencialCoC(ctx, credencialId, assetId, evento, did, payloadHashSha256) {
        const chaveCredencial = ctx.stub.createCompositeKey(PREFIXO_CREDENCIAL, [credencialId]);
        const credencialExistente = await ctx.stub.getState(chaveCredencial);

        if (credencialExistente && credencialExistente.length > 0) {
            const existente = JSON.parse(credencialExistente.toString());
            const mesmaOperacao = existente.tipo === 'COC'
                && existente.assetId === assetId
                && existente.evento === evento
                && existente.did === did
                && existente.payloadHashSha256 === payloadHashSha256;

            if (!mesmaOperacao) {
                throw new Error(`Conflito de idempotência para a credencial: ${credencialId}`);
            }

            return credencialId;
        }

        const credencial = {
            credencialId,
            tipo: 'COC',
            assetId,
            evento,
            did,
            payloadHashSha256,
            emitidaEm: this._agora(ctx),
            revogada: false
        };

        await ctx.stub.putState(chaveCredencial, Buffer.from(JSON.stringify(credencial)));

        const ocorridoEm = this._agora(ctx);
        const chaveHistorico = ctx.stub.createCompositeKey(PREFIXO_HISTORICO, [assetId, ctx.stub.getTxID()]);
        const registroHistorico = {
            assetId,
            evento,
            ocorridoEm,
            didResponsavel: did,
            credencialId
        };
        await ctx.stub.putState(chaveHistorico, Buffer.from(JSON.stringify(registroHistorico)));

        return credencialId;
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
        const chave = ctx.stub.createCompositeKey(PREFIXO_CREDENCIAL, [credencialId]);
        const bytes = await ctx.stub.getState(chave);
        if (!bytes || bytes.length === 0) {
            throw new Error(`Credencial não encontrada: ${credencialId}`);
        }

        const credencial = JSON.parse(bytes.toString());
        credencial.revogada = true;
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
        if (!/^z[1-9A-HJ-NP-Za-km-z]{40,64}$/.test(publicKeyMultibase)) {
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
