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
