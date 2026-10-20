(() => {
    const walletUi = window.CustodyChainWallet;
    if (!walletUi) return;

    const mensagemErro = (erro, padrao) => erro instanceof Error && erro.message ? erro.message : padrao;
    const campos = form => ({
        botao: form.querySelector('button[type="submit"]'), senha: form.querySelector('[name="SenhaWallet"]'),
        pareamento: form.querySelector('[name="CodigoPareamento"]'), status: form.querySelector('[data-operacao-status], [data-laudo-status]'),
        erro: form.querySelector('[data-operacao-error], [data-laudo-error]'), token: walletUi.obterTokenAntiforgery(form)
    });

    function atualizarCredenciais(form, assinada) {
        ['SenhaWallet', 'CodigoPareamento'].forEach(nome => {
            const campo = form.querySelector(`[name="${nome}"]`);
            if (campo) campo.required = !assinada;
        });
    }

    function payload(form) {
        const nomes = ['Justificativa', 'Tipo', 'QuantidadeDescrita', 'OutrosVestigiosOrigemIds', 'RotuloEvidenciaResultante', 'DescricaoResultante'];
        const chaves = ['justificativa', 'tipo', 'quantidadeDescrita', 'outrosVestigiosOrigemIds', 'rotuloEvidenciaResultante', 'descricaoResultante'];
        return Object.fromEntries(nomes.map((nome, indice) => [chaves[indice], form.querySelector(`[name="${nome}"]`)?.value]).filter(([, valor]) => valor !== undefined));
    }

    function invalidarAoEditar(form, estado) {
        form.querySelectorAll('[name="Justificativa"], [name="Tipo"], [name="QuantidadeDescrita"], [name="OutrosVestigiosOrigemIds"], [name="RotuloEvidenciaResultante"], [name="DescricaoResultante"], [name="Conteudo"]')
            .forEach(campo => campo.addEventListener(campo.tagName === 'SELECT' ? 'change' : 'input', () => {
                estado.assinada = null;
                atualizarCredenciais(form, false);
            }));
    }

    async function obterAssinatura(form, estado, ui, corpo) {
        if (estado.assinada) return estado.assinada;
        if (!walletUi.camposPreenchidos(ui.senha, ui.pareamento)) throw new Error('Informe a senha e o código de pareamento da wallet.');
        walletUi.definirStatus(ui.status, 'Preparando a operação para assinatura.', 'processando');
        const comando = await walletUi.requisicaoAplicacao({ url: form.dataset.prepararUrl, token: ui.token, body: corpo, mensagemErro: 'Não foi possível preparar a operação pericial.' });
        walletUi.definirStatus(ui.status, 'Aguardando a assinatura na wallet.', 'aguardando');
        const resposta = await walletUi.requisicaoWallet({ endpoint: comando.walletEndpoint, path: '/v1/proofs/signed-operation', pairingToken: ui.pareamento.value, body: { did: comando.signerDid, password: ui.senha.value, operation: comando.operation }, mensagemErro: 'A wallet recusou a assinatura da operação.' });
        estado.assinada = resposta.operation;
        walletUi.limparCamposSensíveis(ui.senha, ui.pareamento);
        atualizarCredenciais(form, true);
        return estado.assinada;
    }

    function vincular(form, ehLaudo) {
        const ui = campos(form);
        const estado = { assinada: null };
        invalidarAoEditar(form, estado);
        form.addEventListener('submit', async evento => {
            evento.preventDefault();
            walletUi.limparErro(ui.erro);
            if (!form.reportValidity()) return;
            const corpo = ehLaudo ? { conteudo: form.querySelector('[name="Conteudo"]').value } : payload(form);
            if (corpo.justificativa !== undefined && !corpo.justificativa.trim()) {
                walletUi.exibirErro(ui.erro, 'Informe a justificativa da operação.');
                return;
            }
            walletUi.definirProcessamento({ botao: ui.botao, painel: form, processando: true });
            try {
                const operacao = await obterAssinatura(form, estado, ui, corpo);
                walletUi.definirStatus(ui.status, ehLaudo ? 'Confirmando o laudo no ledger.' : 'Confirmando a autorização no ledger.', 'processando');
                const resultado = await walletUi.requisicaoAplicacao({ url: form.dataset.concluirUrl, token: ui.token, body: { ...corpo, operation: operacao }, mensagemErro: ehLaudo ? 'Não foi possível emitir o laudo.' : 'Não foi possível confirmar a operação pericial.' });
                walletUi.definirStatus(ui.status, 'Operação confirmada. Atualizando a página.', 'sucesso');
                window.location.assign(resultado.redirectUrl);
            } catch (erro) {
                walletUi.limparCamposSensíveis(ui.senha, ui.pareamento);
                walletUi.definirStatus(ui.status, '', 'erro');
                walletUi.exibirErro(ui.erro, mensagemErro(erro, 'Não foi possível concluir a operação pericial.'));
            } finally {
                walletUi.definirProcessamento({ botao: ui.botao, painel: form, processando: false });
            }
        });
    }

    document.querySelectorAll('[data-operacao-assinada-form]').forEach(form => vincular(form, false));
    document.querySelectorAll('[data-laudo-form]').forEach(form => vincular(form, true));
})();
