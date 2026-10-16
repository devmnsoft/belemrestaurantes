# Implantação Windows Server — Belém Restaurantes

## Arquitetura recomendada
- Windows Server VPS
- IIS + ASP.NET Core Hosting Bundle correspondente ao runtime .NET 10
- ASP.NET Core MVC/Razor atrás de IIS
- MySQL 8+ **local na VPS** (banco `belrestaurante`) ou MySQL gerenciado que permita uso externo em contrato
- Worker de descoberta roda no app sob IIS **somente se Always Running + preload habilitados**, mas é mais robusto extrair worker para Windows Service dedicado na próxima etapa
- DNS do domínio continua administrado pela Locaweb e aponta para IP público da VPS somente após validação
- HTTPS com certificado válido; jamais abrir MySQL 3306 para a internet

## Checklist
1. Instale atualizações do Windows e configure firewall: entrada somente 80/443 (e acesso administrativo limitado por VPN/IP); RDP restrito.
2. Instale IIS, ASP.NET Core Hosting Bundle para .NET 10 e reinicie IIS.
3. Instale MySQL e execute `sql/mysql/001_schema.sql` no banco `belrestaurante`; faça backup.
4. Crie usuário de banco restrito e forneça a string via segredo/variável de ambiente do serviço, jamais em appsettings no GitHub.
5. Configure conta do Application Pool específica, permissões de pasta mínimas, app pool = No Managed Code, modo AlwaysRunning e Preload Enabled.
6. Use `scripts/windows/publish.ps1` para publicar o app a uma pasta de staging.
7. Configure as variáveis `ConnectionStrings__MySql`, `Auth__Authority`, `Auth__Audience` e, quando ativar descoberta, `GOOGLE_PLACES_API_KEY`.
8. **Não** habilite `Discovery__Enabled` antes de validar compilação, migrations, custo/quotas Google e segurança administrativa.
9. Faça testes em endereço temporário/hosts antes de alterar DNS; certifique-se de preservar MX/SPF/DKIM.
10. Configure backups automáticos, logs centralizados, monitoramento e alertas; confirme `/health/ready`.

## Banco da Locaweb
Hostname informado: `belrestaurante.mysql.dbaas.com.br`. O hostname não implica permissão de uso em aplicativo externo. Não conectar ao banco do plano compartilhado a partir desta VPS sem autorização expressa do provedor. Na ausência, mantenha banco privado na VPS e migre dados por export/import.
