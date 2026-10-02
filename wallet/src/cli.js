'use strict';

const path = require('node:path');
const { WalletStore } = require('./walletStore');

function argumento(nome) {
    const indice = process.argv.indexOf(nome);
    return indice >= 0 ? process.argv[indice + 1] : undefined;
}

function lerSenha(rotulo) {
    if (!process.stdin.isTTY) {
        throw new Error('A criação da identidade exige um terminal interativo.');
    }

    return new Promise((resolve, reject) => {
        let senha = '';
        process.stdout.write(rotulo);
        process.stdin.setRawMode(true);
        process.stdin.resume();
        process.stdin.setEncoding('utf8');

        const onData = (caractere) => {
            if (caractere === '\u0003') {
                process.stdin.setRawMode(false);
                process.stdin.pause();
                reject(new Error('Operação cancelada.'));
                return;
            }
            if (caractere === '\r' || caractere === '\n') {
                process.stdin.off('data', onData);
                process.stdin.setRawMode(false);
                process.stdin.pause();
                process.stdout.write('\n');
                resolve(senha);
                return;
            }
            if (caractere === '\u007f') {
                senha = senha.slice(0, -1);
                return;
            }
            senha += caractere;
        };
        process.stdin.on('data', onData);
    });
}

async function main() {
    if (process.argv[2] !== 'create-admin') {
        throw new Error('Uso: npm run create-admin -- --did did:legal:admin:<identificador>');
    }

    const did = argumento('--did');
    if (!did) {
        throw new Error('O argumento --did é obrigatório.');
    }

    const password = await lerSenha('Senha da wallet (mínimo 12 caracteres): ');
    const confirmation = await lerSenha('Confirme a senha: ');
    if (password !== confirmation) {
        throw new Error('As senhas não coincidem.');
    }

    const databasePath = process.env.WALLET_DATABASE_PATH
        || path.resolve(__dirname, '..', 'data', 'wallet.db');
    const store = new WalletStore(databasePath);
    try {
        const identity = store.createAdminIdentity({ did, password });
        process.stdout.write(`${JSON.stringify(identity, null, 2)}\n`);
    } finally {
        store.close();
    }
}

main().catch((error) => {
    console.error(error.message);
    process.exitCode = 1;
});
