# GuildSync pricing

## Approved international list prices

| Plan | List price | Effective monthly price |
|---|---:|---:|
| 1 month | US$ 4.99 | US$ 4.99 |
| 3 months | US$ 12.99 | US$ 4.33 |
| 6 months | US$ 22.99 | US$ 3.83 |
| 12 months | US$ 39.99 | US$ 3.33 |
| Permanent | US$ 89.99 | One-time payment |

Recommended merchandising:

- **3 months**: label as “Most popular”.
- **12 months**: label as “Best value”.
- **Permanent**: standard list price US$ 89.99.
- Optional founder launch: US$ 59.99 for a clearly limited first cohort, then return to US$ 89.99.

## Current payment environment

The current Mercado Pago staging integration is configured in BRL and has only the one-month test plan enabled at R$ 1.00. Do not replace the test amount with the commercial prices until the complete buyer-test flow, signed webhook, order validation, idempotency, and automatic license activation have passed.

The dollar amounts above are the international product strategy. A production decision is still required between:

1. displaying USD prices while charging a defined BRL amount through the Brazilian Mercado Pago account; or
2. adding an international processor that can settle transactions directly in USD.

Do not dynamically convert prices at checkout without storing the quoted amount and currency in the order. The webhook must continue comparing the approved payment against the exact amount and currency recorded when the order was created.
