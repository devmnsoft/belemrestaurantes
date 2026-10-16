# Segurança e limites operacionais

Status: fundação inicial, não homologada em produção.

- Credenciais apenas em secret manager; nunca nos arquivos estáticos, commits ou logs.
- Servidor deve rodar com HTTPS, reverse proxy, isolamento de rede e banco acessível só internamente.
- Exigir autenticação forte e MFA para áreas administrativas antes de abrir qualquer endpoint de moderação.
- Todos os resultados automáticos permanecem pendentes até revisão. Não copiar fotos/avaliações de plataformas sem licença.
- Validar URL de provedores por allowlist (prevenir SSRF); nunca solicitar URLs arbitrárias do usuário pelo servidor.
- Definir retenção, direitos de exclusão/correção LGPD, backups testados, revisão de dependências e pentest.
- Limites de chamadas e orçamento externo. Não tratar o intervalo de dez minutos como obrigação de consultar todas as cidades em cada ciclo.
- O worker usa fonte externa apenas quando há credenciais e deve reduzir velocidade em erros 429/5xx.
- Comentários e fotos enviados por usuários exigem autenticação, antivírus, licenciamento e moderação.
- Tabela de avaliações não está exposta para escrita pública até existir autenticação.
- Não usar IA para executar comandos, alterar autorizações ou publicar avaliações; saídas são sugestões sem confiança.
