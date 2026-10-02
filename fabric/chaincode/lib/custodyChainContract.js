'use strict';

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
            didEmissor: null,
            criadoEm: agora,
            ativadoEm: agora
        };

        await ctx.stub.putState(chaveDid, Buffer.from(JSON.stringify(documento)));
        await ctx.stub.putState(chaveBootstrap, Buffer.from(JSON.stringify({ did, criadoEm: agora })));
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

module.exports = CustodyChainContract;
