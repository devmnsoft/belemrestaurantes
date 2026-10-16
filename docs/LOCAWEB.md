# Implantação para belemrestaurantes.com.br
Hospedagem fornecida: Locaweb Hospedagem I Linux, MySQL `belrestaurante`, raiz `/home/belemrestaurante1/`, IP compartilhado `179.188.55.7`.
## Limitação principal
Não há confirmação de execução persistente de ASP.NET Core no plano compartilhado. **Não** publicar runtime como se fosse funcional. Recomendação: hospedar API .NET + worker em VPS/container gerenciado, usando site estático da Locaweb como frontend apenas se CORS/SSL estiverem configurados; ou apontar domínio para hospedagem .NET e manter MySQL somente se acesso remoto seguro for permitido.
Cron do plano compartilhado pode não permitir executar a cada 10 minutos durante 24h. O worker deve funcionar no servidor .NET, não depender do cron compartilhado.
## SQL
Aplicar `sql/mysql/001_schema.sql` usando ferramenta SQL do painel, depois backup e teste. Requer MySQL 8.0+ por compatibilidade com JSON e collation. Se a versão for 5.7, testar separadamente; não assumir.
## Variáveis privadas do servidor .NET
`ConnectionStrings__MySql=Server=DB_HOST;Port=3306;Database=belrestaurante;User ID=DB_USER;Password=DB_PASSWORD;SslMode=Required;`
`GOOGLE_PLACES_API_KEY` segredo com restrição de API, orçamento e quota no Google Cloud.
`Discovery__Enabled=false` até validar SSL de banco, conexão, SQL, auditoria e cobrança.
`Auth__Authority` e `Auth__Audience` para identidade do moderador.
## DNS
O IP da hospedagem foi informado como compartilhado; não transferir o domínio nem modificar registros MX sem confirmar servidor efetivo e certificado SSL. Nunca publicar chaves em GitHub, HTML, ou arquivos acessíveis por FTP.
## Notas operacionais
Depois de ativar, verificar `/health/ready`, tabelas `sync_runs` e `discovery_targets`, custos do Google Places, e se a moderação mantém todo novo registro pendente. Dados do Google devem seguir termos de licenciamento, especialmente retenção e atribuição.

## Decisão de arquitetura — VPS externa (16/10/2026)
A política da Locaweb para Hospedagem de Sites informa que bancos MySQL/PostgreSQL/SQL Server deste plano **não podem ser usados por sites ou aplicações hospedados fora da Locaweb**. Referência: https://www.locaweb.com.br/politicas/ . Portanto, **não** configurar a aplicação ASP.NET Core na VPS externa para utilizar o MySQL do plano Hospedagem I Linux em produção. O acesso externo restrito (até uma conexão) não autoriza uso permanente da aplicação externa.

Arquitetura recomendada: domínio/DNS na Locaweb; ASP.NET Core MVC + worker + MySQL `belrestaurante` instalados na VPS com firewall, TLS, backups e acesso privado. Para migração de conteúdo existente, verificar exportação autorizada pelo phpMyAdmin e importar no MySQL da VPS. Não apontar o DNS para a VPS antes de validar HTTPS e deploy.
