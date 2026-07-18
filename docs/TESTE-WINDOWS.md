# Clonar DC — validação Windows

## Estado desta build

Esta é uma build **alpha**. O pipeline compila o cliente Windows x64 self-contained, compila o backend, gera um instalador e executa um smoke test de processo no runner Windows.

Isso **não substitui** testes funcionais reais com servidores descartáveis do Discord.

## Checklist manual obrigatório

1. Instalar pelo `Clonar-DC-Setup.exe` sem instalar Node, Go, Python ou SDK .NET.
2. Confirmar que abre uma janela própria do Windows e não abre Edge/Chrome/localhost como interface.
3. Confirmar ícone, atalhos e desinstalação.
4. Entrar em `modo local de desenvolvimento` para testar a interface enquanto o backend de produção não está hospedado.
5. Usar apenas Token de **bot oficial** criado no Discord Developer Portal; nunca usar token pessoal de usuário.
6. Em dois servidores descartáveis, testar: Token válido/inválido, análise, backup, modo seguro, modo exato, restauração e interrupção de rede.
7. Verificar cargos gerenciados, hierarquia do bot, permissões insuficientes, canais especiais, emojis e rate limits.
8. Não desligar nem adicionar exceção ampla no antivírus para testar. Builds alpha não assinadas podem gerar alertas de reputação.

## Critério de produção

Não chamar de produção antes de concluir: backend HTTPS hospedado, recuperação de conta, MFA administrativo, banco de produção, assinatura de código, atualização assinada, testes destrutivos em servidores descartáveis, testes em outro PC e revisão de segurança.
