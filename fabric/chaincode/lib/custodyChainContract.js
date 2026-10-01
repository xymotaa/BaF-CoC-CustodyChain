'use strict';

const { Contract } = require('fabric-contract-api');

const PREFIXO_DID = 'DID';
const PREFIXO_CREDENCIAL = 'CRED';
const PREFIXO_HISTORICO = 'HIST';

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

        const chaveCredencial = ctx.stub.createCompositeKey(PREFIXO_CREDENCIAL, [credencialId]);
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
}

module.exports = CustodyChainContract;
