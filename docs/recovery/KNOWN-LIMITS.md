# Limites ainda dependentes de teste externo

- O CI pode confirmar que o aplicativo publicado e instalado permanece aberto, mas não substitui inspeção visual humana no Windows.
- O modo de pré-visualização pode ser validado sem credencial real; carregamento e clonagem reais exigem Token aceito pela API do Discord.
- Clonagem destrutiva deve ser testada somente entre servidores descartáveis.
- A GitHub Release permanente é criada apenas após o merge em `main` e sucesso integral do workflow de release.

Nenhum destes itens pode ser declarado testado sem evidência correspondente.
