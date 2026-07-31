# Recovery workflow

A versão 0.8.3.2 existe para reconstruir de forma verificável as correções que haviam sido entregues apenas em armazenamento temporário.

A branch só deve ser mesclada quando o workflow de pull request concluir compilação, contratos, testes de API, testes do motor, payload autocontido e smoke test de abertura.

Depois do merge, o workflow `GuildSync Verified Windows Build` deve publicar a tag e a GitHub Release permanente `v0.8.3.2`. A publicação da release é fail-closed: sem assets permanentes, a build não é registrada como sucesso.
