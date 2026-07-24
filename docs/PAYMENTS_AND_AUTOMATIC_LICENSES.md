# Automatic payments and license activation

## Goal

A customer pays for a Clonar DC plan and the account is activated automatically without manual approval.

## Recommended architecture

```text
Discord command or website checkout
        ↓
Stripe Checkout (global) / Mercado Pago (Brazil)
        ↓ signed webhook
Clonar DC central HTTPS backend
        ↓
Verify payment with provider API
        ↓
Idempotent license transaction
        ↓
Activate or extend app license
        ↓ optional
Assign Discord customer role and send receipt/status message
```

The Discord bot is the user interface, not the source of truth. Payment status and license state must be controlled by the central backend.

## Identity linking

Each checkout session must include an internal immutable account ID and, when available, the Discord user ID. Do not rely only on display names. The app account email may be used for user-facing confirmation, but the internal account ID is the canonical link.

## Plans

- 1 month
- 3 months
- 6 months
- 12 months
- Permanent

Each provider product/price maps to one license duration. The backend must reject unknown product IDs instead of trusting a duration sent by the client.

## Required webhook behavior

1. Validate the provider signature before reading the event.
2. Fetch the payment/session from the provider API instead of trusting the webhook body alone.
3. Record the provider event ID with a unique constraint.
4. Ignore already-processed events safely.
5. Activate only approved/paid payments.
6. On refund, dispute, chargeback, cancellation, or failed subscription renewal, update the license according to the commercial policy.
7. Write an audit record without card data or provider secrets.
8. Return a fast 2xx response and process retries idempotently.

## Discord bot flow

Suggested commands:

- `/buy` — shows plan buttons and creates checkout.
- `/license` — shows current plan and expiration.
- `/link` — links a Discord account to an existing Clonar DC account using a short-lived one-time code.
- `/unlink` — removes the Discord link after confirmation.

After an approved payment, the bot can assign a customer role. It needs `MANAGE_ROLES`, and its highest role must be above the customer role.

## Security

- Provider secret keys and Discord bot token exist only on the server.
- The desktop app never receives payment-provider credentials.
- Webhook secrets are stored as deployment secrets.
- Checkout creation requires an authenticated Clonar DC account or a signed, short-lived link code.
- License updates are server-side only.
- Use HTTPS in production.
- Add rate limits and audit logs.

## Deployment requirement

This feature cannot work reliably with the current per-computer local backend. It requires one central backend and database reachable over HTTPS so every app installation, the payment provider, and the Discord bot share the same account and license state.

## Provider strategy

- Primary global provider: Stripe Checkout and Stripe Billing.
- Optional Brazil provider: Mercado Pago Checkout/Pix and Subscriptions.
- Keep the backend provider-agnostic so both can map into the same internal `Payment`, `Plan`, and `LicenseGrant` records.
