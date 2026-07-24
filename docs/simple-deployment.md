# Deploy simples do backend central

O repositório já contém um Blueprint do Render (`render.yaml`). Ele cria, em uma única operação:

- a API HTTPS do Clonar DC;
- um banco PostgreSQL conectado automaticamente;
- deploy automático a partir da branch `main`;
- verificação de saúde em `/status`.

## O único fluxo manual

1. Entre no Render usando o GitHub.
2. Abra **New → Blueprint**.
3. Selecione `XlionHz/clonar-dc`.
4. O Render detectará `render.yaml`.
5. Preencha somente os campos secretos solicitados:
   - `CLONARDC_ADMIN_EMAIL`;
   - `CLONARDC_ADMIN_PASSWORD`;
   - `MERCADOPAGO_ACCESS_TOKEN` de teste.
6. Confirme em **Apply**.
7. Aguarde o serviço ficar `Live` e abra `/status`.

Não é necessário criar manualmente o serviço web nem o banco de dados. O Blueprint faz isso junto.

## Primeiro teste, sem cobrança

Os preços começam em `0`, então nenhum plano pode gerar cobrança por acidente. Depois que a API estiver online, defina no painel do serviço os valores desejados:

- `CLONARDC_PRICE_1M`
- `CLONARDC_PRICE_3M`
- `CLONARDC_PRICE_6M`
- `CLONARDC_PRICE_12M`
- `CLONARDC_PRICE_PERMANENT`

Use ponto como separador decimal, por exemplo `19.90`.

## Webhook do Mercado Pago

Depois que o Render mostrar a URL pública, configure no Mercado Pago:

```text
https://SEU-SERVICO.onrender.com/webhooks/mercadopago
```

Selecione o evento **Pagamentos**. O Mercado Pago fornecerá uma chave secreta; salve-a no Render como:

```text
MERCADOPAGO_WEBHOOK_SECRET
```

Nunca coloque Access Token ou chave do webhook no GitHub, no aplicativo ou em mensagens.

## Conectar o aplicativo Windows

O desktop aceita uma API central por:

1. variável `CLONARDC_API`;
2. registro do Windows `HKCU\Software\Clonar DC\ApiUrl`;
3. arquivo `api-url.txt` ao lado do executável.

Até a URL central ser definida, ele continua usando o backend local como fallback.

## Limite do plano gratuito

O Blueprint gratuito é adequado para validar cadastro, login, banco, Checkout Pro e webhook. Para produção contínua, substitua os recursos gratuitos antes do vencimento/hibernação aplicável ao provedor, mantendo a mesma arquitetura e variáveis.