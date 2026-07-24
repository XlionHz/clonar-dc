# Clonar DC

Aplicativo Windows para clonar e sincronizar estruturas de servidores usando a API oficial do Discord.

## Backend central em um clique

[![Deploy to Render](https://render.com/images/deploy-to-render-button.svg)](https://render.com/deploy?repo=https://github.com/XlionHz/clonar-dc)

O Blueprint `render.yaml` cria a API HTTPS e o PostgreSQL juntos. Ele solicita apenas as variáveis secretas que não podem ficar no repositório.

Guia: [`docs/simple-deployment.md`](docs/simple-deployment.md)

## Pagamentos

O backend 0.4 inclui:

- criação de preferências do Mercado Pago Checkout Pro;
- retorno de sucesso, pendência e falha;
- webhook assinado;
- confirmação do pagamento pela API do Mercado Pago;
- validação de valor e moeda;
- liberação ou renovação automática da licença;
- proteção contra reaplicação do mesmo pagamento.

Os preços ficam desativados por padrão (`0`) e são configurados somente no servidor.

## Segurança

Nunca envie ou versione:

- `MERCADOPAGO_ACCESS_TOKEN`;
- `MERCADOPAGO_WEBHOOK_SECRET`;
- senha administrativa;
- token do bot do Discord.

O arquivo `.env.example` contém apenas os nomes das variáveis.