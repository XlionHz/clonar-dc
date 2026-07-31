# Resumo da recuperação 0.8.3.2

Esta branch corrige os defeitos observados na entrega temporária 0.8.3.1 e transforma a publicação numa etapa obrigatória e verificável.

## Produto

- recuperação de cadastro com conta local de preview quando o serviço central falhar;
- login local limitado a preview, sem fingir licença central;
- cartão de login interativo no hover;
- Token salvo sem `MessageBox` e sem som do Windows;
- carregamento real de servidores quando a API aceitar a conexão;
- fallback de preview para qualquer valor, claramente rotulado e sem escrita no Discord;
- análise e simulação seguras dentro do preview.

## Release

- versão exclusiva 0.8.3.2;
- desktop/backend autocontidos;
- limite mínimo de payload e instalador;
- smoke test antes e depois da instalação;
- publicação permanente fail-closed;
- instalador, portátil, fonte, atualização e hashes.
