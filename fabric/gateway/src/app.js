'use strict';

const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const grpc = require('@grpc/grpc-js');
const { connect, signers, hash } = require('@hyperledger/fabric-gateway');
const express = require('express');
const cors = require('cors');

const CHANNEL_NAME = process.env.CHANNEL_NAME || 'mychannel';
const CHAINCODE_NAME = process.env.CHAINCODE_NAME || 'custodychain';
const MSP_ID = process.env.MSP_ID || 'Org1MSP';
const PEER_ENDPOINT = process.env.PEER_ENDPOINT || 'localhost:7051';
const PEER_HOST_ALIAS = process.env.PEER_HOST_ALIAS || 'peer0.org1.example.com';
const PORT = process.env.PORT || 3000;
const SERVICE_TOKEN = process.env.GATEWAY_SERVICE_TOKEN || '';
const LEGACY_IDENTITY_WRITES_ENABLED = process.env.ENABLE_LEGACY_IDENTITY_WRITES === 'true';
const ALLOWED_ORIGINS = (process.env.ALLOWED_ORIGINS || 'http://localhost:5143')
    .split(',')
    .map((origin) => origin.trim())
    .filter(Boolean);

const CRYPTO_PATH = process.env.CRYPTO_PATH
    || path.resolve(__dirname, '..', '..', 'network', 'organizations', 'peerOrganizations', 'org1.example.com');
const TLS_CERT_PATH = process.env.TLS_CERT_PATH
    || path.resolve(CRYPTO_PATH, 'peers', 'peer0.org1.example.com', 'tls', 'ca.crt');
const MSP_PATH = process.env.MSP_PATH
    || path.resolve(CRYPTO_PATH, 'users', 'User1@org1.example.com', 'msp');

let gateway;
let client;

function lerPrimeiroArquivo(diretorio) {
    const [arquivo] = fs.readdirSync(diretorio);
    return fs.readFileSync(path.resolve(diretorio, arquivo));
}

async function novaConexaoGrpc() {
    const tlsRootCert = fs.readFileSync(TLS_CERT_PATH);
    const credenciaisTls = grpc.credentials.createSsl(tlsRootCert);
    return new grpc.Client(PEER_ENDPOINT, credenciaisTls, {
        'grpc.ssl_target_name_override': PEER_HOST_ALIAS
    });
}

function novaIdentidade() {
    const certPath = path.resolve(MSP_PATH, 'signcerts');
    const credentials = lerPrimeiroArquivo(certPath);
    return { mspId: MSP_ID, credentials };
}

function novoAssinante() {
    const keyPath = path.resolve(MSP_PATH, 'keystore');
    const chavePrivadaPem = lerPrimeiroArquivo(keyPath);
    const chavePrivada = crypto.createPrivateKey(chavePrivadaPem);
    return signers.newPrivateKeySigner(chavePrivada);
}

async function inicializarGateway() {
    client = await novaConexaoGrpc();
    gateway = connect({
        client,
        identity: novaIdentidade(),
        signer: novoAssinante(),
        hash: hash.sha256,
        evaluateOptions: () => ({ deadline: Date.now() + 5000 }),
        endorseOptions: () => ({ deadline: Date.now() + 15000 }),
        submitOptions: () => ({ deadline: Date.now() + 5000 }),
        commitStatusOptions: () => ({ deadline: Date.now() + 60000 })
    });
}

function obterContrato() {
    const network = gateway.getNetwork(CHANNEL_NAME);
    return network.getContract(CHAINCODE_NAME);
}

function tratarErro(res, erro) {
    const detalhes = erro && erro.details;
    const mensagem = Array.isArray(detalhes)
        ? detalhes.map((detalhe) => detalhe.message || String(detalhe)).join(' ')
        : detalhes || (erro && erro.message) || 'Erro desconhecido no gateway.';
    const status = mensagem.includes('não encontrado') ? 404
        : mensagem.includes('já existe') || mensagem.includes('já foi concluído') || mensagem.includes('já foi concluída') ? 409
            : mensagem.includes('inválid') || mensagem.includes('não autorizado') ? 400
                : 500;
    res.status(status).json({ error: mensagem });
}

function autenticarServico(req, res, next) {
    const authorization = req.get('authorization') || '';
    const recebido = authorization.startsWith('Bearer ') ? authorization.slice(7) : '';
    const esperado = Buffer.from(SERVICE_TOKEN);
    const informado = Buffer.from(recebido);
    if (!SERVICE_TOKEN || esperado.length !== informado.length
        || !crypto.timingSafeEqual(esperado, informado)) {
        return res.status(401).json({ error: 'Credencial de serviço inválida.' });
    }
    return next();
}

function corpoObrigatorio(req, campos) {
    for (const campo of campos) {
        if (typeof req.body[campo] !== 'string' || !req.body[campo].trim()) {
            throw new Error(`Campo obrigatório inválido: ${campo}`);
        }
    }
}

function validarComandoDid(command, tipoEsperado) {
    if (!command || typeof command !== 'object' || Array.isArray(command)
        || command.type !== tipoEsperado || command.version !== 1
        || typeof command.commandId !== 'string' || typeof command.issuedAt !== 'string'
        || typeof command.expiresAt !== 'string' || command.audience !== 'custodychain-ledger') {
        throw new Error('Contrato do comando DID inválido.');
    }
}

function verificarProvaDid(command, signature, publicKeyMultibase) {
    if (typeof publicKeyMultibase !== 'string' || typeof signature !== 'string') {
        throw new Error('Prova DID inválida.');
    }
    const multicodec = decodificarMultibaseEd25519(publicKeyMultibase);
    const chave = crypto.createPublicKey({
        key: Buffer.concat([Buffer.from('302a300506032b6570032100', 'hex'), multicodec.subarray(2)]),
        format: 'der', type: 'spki'
    });
    const assinatura = Buffer.from(signature, 'base64url');
    if (assinatura.length !== 64 || !crypto.verify(null, Buffer.from(canonicalizarJson(command)), chave, assinatura)) {
        throw new Error('Assinatura DID inválida.');
    }
}

function verificarVcPermissao(credential, publicKeyMultibase) {
    if (!credential || typeof credential !== 'object' || Array.isArray(credential)
        || credential.proof?.type !== 'CustodyChainEd25519Signature2026'
        || credential.proof.proofPurpose !== 'assertionMethod'
        || credential.proof.canonicalization !== 'custodychain-json-c14n-v1'
        || typeof credential.proof.proofValue !== 'string') {
        throw new Error('Prova da VC de permissão inválida.');
    }
    const { proof, ...credentialWithoutProof } = credential;
    verificarProvaDid(credentialWithoutProof, proof.proofValue, publicKeyMultibase);
}

function validarVcPermissao(credential) {
    if (!credential || typeof credential !== 'object' || Array.isArray(credential)
        || !Array.isArray(credential.type)
        || !credential.type.includes('VerifiableCredential')
        || !credential.type.includes('CustodyChainPermissionCredential')
        || typeof credential.id !== 'string' || typeof credential.issuer !== 'string'
        || credential.proof?.verificationMethod?.startsWith(`${credential.issuer}#`) !== true) {
        throw new Error('Envelope da VC de permissão inválido.');
    }
}

function canonicalizarJson(valor) {
    if (valor === null || typeof valor === 'string' || typeof valor === 'boolean' || typeof valor === 'number') return JSON.stringify(valor);
    if (Array.isArray(valor)) return `[${valor.map(canonicalizarJson).join(',')}]`;
    if (typeof valor === 'object') return `{${Object.keys(valor).sort().map((chave) => `${JSON.stringify(chave)}:${canonicalizarJson(valor[chave])}`).join(',')}}`;
    throw new Error('Tipo inválido no comando DID.');
}

function decodificarMultibaseEd25519(valor) {
    if (!valor.startsWith('z')) throw new Error('Chave pública Multikey inválida.');
    const alfabeto = '123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz';
    const bytes = [0];
    for (const caractere of valor.slice(1)) {
        let transporte = alfabeto.indexOf(caractere);
        if (transporte < 0) throw new Error('Chave pública Multikey inválida.');
        for (let indice = 0; indice < bytes.length; indice += 1) {
            transporte += bytes[indice] * 58;
            bytes[indice] = transporte & 0xff;
            transporte >>= 8;
        }
        while (transporte > 0) { bytes.push(transporte & 0xff); transporte >>= 8; }
    }
    const resultado = Buffer.from(bytes.reverse());
    if (resultado.length !== 34 || resultado[0] !== 0xed || resultado[1] !== 0x01) throw new Error('Chave pública Ed25519 Multikey inválida.');
    return resultado;
}

// evaluateTransaction/submitTransaction retornam Uint8Array, não Buffer —
// .toString() direto produz a lista de códigos de byte, não o texto.
function decodificar(resultadoBruto) {
    return Buffer.from(resultadoBruto).toString('utf8');
}

const app = express();
app.use(cors({
    origin(origin, callback) {
        if (!origin || ALLOWED_ORIGINS.includes(origin)) {
            return callback(null, true);
        }
        return callback(new Error('Origem não autorizada pelo gateway.'));
    }
}));
app.use(express.json());

app.get('/saude', (_req, res) => {
    res.json({ status: gateway ? 'conectado' : 'desconectado' });
});

app.use(autenticarServico);

app.post('/v2/bootstrap/admin', async (req, res) => {
    try {
        corpoObrigatorio(req, ['did', 'verificationMethodId', 'publicKeyMultibase']);
        const { did, verificationMethodId, publicKeyMultibase } = req.body;
        const contrato = obterContrato();
        const resultado = await contrato.submitTransaction(
            'BootstrapAdminDid', did, verificationMethodId, publicKeyMultibase
        );
        res.status(201).json(JSON.parse(decodificar(resultado)));
    } catch (erro) {
        tratarErro(res, erro);
    }
});

app.post('/v2/bootstrap/admin/legacy-migration', async (req, res) => {
    try {
        corpoObrigatorio(req, ['did', 'verificationMethodId', 'publicKeyMultibase']);
        const { did, verificationMethodId, publicKeyMultibase } = req.body;
        const resultado = await obterContrato().submitTransaction(
            'MigrarAdminDidLegadoParaV2', did, verificationMethodId, publicKeyMultibase
        );
        res.status(201).json(JSON.parse(decodificar(resultado)));
    } catch (erro) {
        tratarErro(res, erro);
    }
});

app.get('/v2/dids/:did', async (req, res) => {
    try {
        const contrato = obterContrato();
        const resultado = await contrato.evaluateTransaction('ResolverDid', req.params.did);
        res.json(JSON.parse(decodificar(resultado)));
    } catch (erro) {
        tratarErro(res, erro);
    }
});

app.post('/v2/dids/:did/revogar', async (req, res) => {
    try {
        const contrato = obterContrato();
        const resultado = await contrato.submitTransaction('RevogarDidV2', req.params.did);
        res.json(JSON.parse(decodificar(resultado)));
    } catch (erro) {
        tratarErro(res, erro);
    }
});

app.post('/v2/dids/pending', async (req, res) => {
    try {
        const { command, signature } = req.body;
        validarComandoDid(command, 'CustodyChainDidRegistration');
        if (typeof signature !== 'string' || !signature) {
            throw new Error('Assinatura do registro DID é obrigatória.');
        }
        verificarProvaDid(command, signature, command.publicKeyMultibase);
        const resultado = await obterContrato().submitTransaction(
            'RegistrarDidV2Pendente', JSON.stringify(command), signature
        );
        res.status(201).json(JSON.parse(decodificar(resultado)));
    } catch (erro) {
        tratarErro(res, erro);
    }
});

app.post('/v2/dids/:did/activate', async (req, res) => {
    try {
        const { command, keyId, signature } = req.body;
        validarComandoDid(command, 'CustodyChainDidActivation');
        if (command.subjectDid !== req.params.did || typeof keyId !== 'string' || typeof signature !== 'string') {
            throw new Error('Prova de ativação DID inválida.');
        }
        const contrato = obterContrato();
        const administrador = JSON.parse(decodificar(await contrato.evaluateTransaction('ResolverDid', command.actorDid)));
        const metodo = administrador.verificationMethod?.find((item) => item.id === keyId);
        if (!administrador.capabilityInvocation?.includes(keyId) || !metodo) {
            throw new Error('A chave não possui capacidade para ativar identidades.');
        }
        verificarProvaDid(command, signature, metodo.publicKeyMultibase);
        const resultado = await contrato.submitTransaction(
            'AtivarDidV2', JSON.stringify(command), keyId, signature
        );
        res.json(JSON.parse(decodificar(resultado)));
    } catch (erro) {
        tratarErro(res, erro);
    }
});

app.post('/dids', async (req, res) => {
    if (!LEGACY_IDENTITY_WRITES_ENABLED) {
        return res.status(410).json({ error: 'Criação de DID v1 desabilitada; use o contrato v2 com prova de posse.' });
    }
    try {
        const { did, metodoDid } = req.body;
        const contrato = obterContrato();
        const resultado = await contrato.submitTransaction('GerarDid', did, metodoDid);
        res.status(201).json(JSON.parse(decodificar(resultado)));
    } catch (erro) {
        tratarErro(res, erro);
    }
});

app.post('/dids/:did/ativar', async (req, res) => {
    if (!LEGACY_IDENTITY_WRITES_ENABLED) {
        return res.status(410).json({ error: 'Ativação de DID v1 desabilitada; use o contrato v2 assinado.' });
    }
    try {
        const { did } = req.params;
        const { didEmissor } = req.body;
        const contrato = obterContrato();
        const resultado = await contrato.submitTransaction('AtivarDid', did, didEmissor);
        res.json(JSON.parse(decodificar(resultado)));
    } catch (erro) {
        tratarErro(res, erro);
    }
});

app.get('/dids/:did', async (req, res) => {
    try {
        const { did } = req.params;
        const contrato = obterContrato();
        const resultado = await contrato.evaluateTransaction('ResolverDid', did);
        res.json(JSON.parse(decodificar(resultado)));
    } catch (erro) {
        tratarErro(res, erro);
    }
});

app.post('/credenciais/permissao', async (req, res) => {
    return res.status(410).json({ error: 'Emissão centralizada desabilitada; use a VC v2 assinada pela wallet do emissor.' });
});

app.post('/v2/credenciais/permissao', async (req, res) => {
    try {
        const { credential } = req.body;
        validarVcPermissao(credential);
        const contrato = obterContrato();
        const emissor = JSON.parse(decodificar(await contrato.evaluateTransaction('ResolverDid', credential.issuer)));
        const metodo = emissor.verificationMethod?.find((item) => item.id === credential.proof.verificationMethod);
        if (!emissor.assertionMethod?.includes(credential.proof.verificationMethod) || !metodo) {
            throw new Error('A chave do emissor não possui capacidade assertionMethod para emitir VC.');
        }
        verificarVcPermissao(credential, metodo.publicKeyMultibase);
        const resultado = await contrato.submitTransaction(
            'EmitirCredencialPermissaoV2', JSON.stringify(credential)
        );
        res.status(201).json({ credencialId: decodificar(resultado) });
    } catch (erro) {
        tratarErro(res, erro);
    }
});

app.post('/credenciais/coc', async (req, res) => {
    try {
        const { credencialId, assetId, evento, did, payloadHashSha256 } = req.body;
        const contrato = obterContrato();
        const resultado = await contrato.submitTransaction(
            'EmitirCredencialCoC', credencialId, assetId, evento, did, payloadHashSha256
        );
        res.status(201).json({ credencialId: decodificar(resultado) });
    } catch (erro) {
        tratarErro(res, erro);
    }
});

app.get('/credenciais/:credencialId/verificar', async (req, res) => {
    try {
        const { credencialId } = req.params;
        const contrato = obterContrato();
        const resultado = await contrato.evaluateTransaction('VerificarCredencial', credencialId);
        res.json(JSON.parse(decodificar(resultado)));
    } catch (erro) {
        tratarErro(res, erro);
    }
});

app.get('/credenciais/:credencialId', async (req, res) => {
    try {
        const { credencialId } = req.params;
        const contrato = obterContrato();
        const resultado = await contrato.evaluateTransaction('ObterCredencial', credencialId);
        res.json(JSON.parse(decodificar(resultado)));
    } catch (erro) {
        tratarErro(res, erro);
    }
});

app.post('/credenciais/:credencialId/revogar', async (req, res) => {
    return res.status(410).json({ error: 'Revogação centralizada desabilitada; use a prova assinada pelo emissor.' });
});

app.post('/v2/credenciais/:credencialId/revogar', async (req, res) => {
    try {
        const { command, keyId, signature } = req.body;
        validarComandoDid(command, 'CustodyChainCredentialRevocation');
        if (command.credentialId !== req.params.credencialId || typeof keyId !== 'string' || typeof signature !== 'string') {
            throw new Error('Prova de revogação de VC inválida.');
        }
        const contrato = obterContrato();
        const emissor = JSON.parse(decodificar(await contrato.evaluateTransaction('ResolverDid', command.issuerDid)));
        const metodo = emissor.verificationMethod?.find((item) => item.id === keyId);
        if (!emissor.assertionMethod?.includes(keyId) || !metodo) {
            throw new Error('A chave do emissor não possui capacidade assertionMethod para revogar VC.');
        }
        verificarProvaDid(command, signature, metodo.publicKeyMultibase);
        const resultado = await contrato.submitTransaction(
            'RevogarCredencialV2', JSON.stringify(command), keyId, signature
        );
        res.json(JSON.parse(decodificar(resultado)));
    } catch (erro) {
        tratarErro(res, erro);
    }
});

app.get('/ativos/:assetId/historico', async (req, res) => {
    try {
        const { assetId } = req.params;
        const contrato = obterContrato();
        const resultado = await contrato.evaluateTransaction('HistoricoRegistro', assetId);
        res.json(JSON.parse(decodificar(resultado)));
    } catch (erro) {
        tratarErro(res, erro);
    }
});

async function iniciar() {
    if (!SERVICE_TOKEN) {
        throw new Error('GATEWAY_SERVICE_TOKEN é obrigatório.');
    }
    await inicializarGateway();
    app.listen(PORT, () => {
        console.log(`Gateway CustodyChain ouvindo na porta ${PORT} (canal=${CHANNEL_NAME}, chaincode=${CHAINCODE_NAME})`);
    });
}

if (require.main === module) {
    iniciar().catch((erro) => {
        console.error('Falha ao iniciar o gateway:', erro);
        process.exit(1);
    });
}

process.on('SIGINT', () => {
    if (client) client.close();
    process.exit(0);
});

module.exports = {
    app, autenticarServico, decodificar, tratarErro, validarComandoDid,
    verificarProvaDid, validarVcPermissao, verificarVcPermissao
};
