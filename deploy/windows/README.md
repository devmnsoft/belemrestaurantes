# Publicação manual — Visual Studio 2026 e IIS

A Web não executa descoberta. O Worker roda como Windows Service em diretório próprio, fora da pasta IIS. Não aplique SQL legado: a fonte de schema são as migrações embutidas.

1. Instale IIS e ASP.NET Core Hosting Bundle .NET 10, aplique atualizações e reinicie IIS.
2. Crie `C:\Sites\BelemRestaurantes`, `C:\Services\BelemRestaurantes.Worker` e `C:\ProgramData\BelemRestaurantes\keys`. Use contas diferentes para IIS e Worker.
3. Crie Application Pool dedicado, **No Managed Code**, identidade `IIS AppPool\BelemRestaurantes`. Dê leitura/execução na pasta do site e escrita somente no diretório privado de chaves. Configure `DataProtection__KeysPath` apontando para esse diretório; preserve-o em atualizações e backups. DPAPI vincula as chaves à identidade Windows: restaurar em outra conta exige planejamento.
4. Configure `ConnectionStrings__MySql` exclusivamente em configuração privada do processo. Banco obrigatório: `belrestaurante`. Conexão remota exige `SslMode=VerifyFull` e certificado confiável; localhost exige TLS também. Não abra 3306 publicamente. Não use usuário root.
5. Antes da publicação, faça backup verificável do banco e dos artefatos/chaves. Pare Worker e site para migração e cópia. Aplique migrações com usuário temporário de DDL: `dotnet BelemRestaurantes.Web.dll --migrate`. Esse comando termina sem abrir servidor HTTP. Retire a credencial de DDL do processo depois.
6. O primeiro administrador deve ser criado **no console local interativo**: `dotnet BelemRestaurantes.Web.dll --provision-admin`. A senha é digitada de forma oculta; não passe senha em argumentos. O primeiro login exige inscrição TOTP. Não existe endpoint HTTP de bootstrap.
7. No Visual Studio 2026, clique com botão direito em Web → Publicar → importe/selecione `FolderProfile` → Release → Publicar. Saída: `artifacts\publish\web`, separada dos fontes. Validação equivalente: `dotnet publish src/BelemRestaurantes.Web -c Release /p:PublishProfile=FolderProfile`.
8. Configure binding HTTPS e certificado válido no IIS. Mantenha configuração privada no servidor e nunca copie arquivos de segredos para Git. Use `app_offline.htm` temporário, copie os artefatos, retire a manutenção e valide `/health`, `/health/ready`, login e MFA. Readiness não contém mensagens de erro do banco.
9. Publique o Worker separadamente: `dotnet publish src/BelemRestaurantes.Worker -c Release -r win-x64 --self-contained false -o artifacts/publish/worker`. Copie para a pasta do serviço, configure `Database__ApplyMigrations=false`, `Discovery__Enabled=false` e segredos privados. Execute `scripts/windows/install-service.ps1 -WorkerDirectory C:\Services\BelemRestaurantes.Worker` em console elevado. A identidade virtual precisa de leitura/execução nessa pasta. O instalador não inicia antes da configuração. Depois, use `Start-Service BelemRestauranteCuiaDiscovery`.
10. Ative descoberta apenas após validar credenciais Google, orçamento, quotas e regras de retenção. Intervalo de ciclo mínimo: dez minutos; lote limitado; `Discovery__TargetRefreshMinutes` controla nova pesquisa do mesmo alvo (padrão sete dias). Controle de orçamento Places ainda é pendência impeditiva de ativação em produção.
11. Valide homologação antes de mudar DNS, mantendo os registros MX existentes. Configure SPF/DKIM/DMARC pelo painel do provedor; SMTP e envio real continuam pendentes.

## Privilégios mínimos e operação

- Web: SELECT no catálogo/referências; INSERT/UPDATE nas tabelas das suas operações (sugestões, moderação, identidade, comunidade), INSERT em auditoria. Sem CREATE, ALTER, DROP, GRANT nem privilégios globais. A lista exata deve ser validada contra os fluxos habilitados.
- Worker: SELECT em alvos/referências e INSERT/UPDATE no catálogo, proveniência, fila, telemetria, custos e moderação; sem administrar usuários nem papéis. Migrações desativadas no serviço permanente.
- Migrations: usuário separado com DDL/DML somente em `belrestaurante`; usado apenas durante manutenção.
- Backup: conta separada, destino fora de IIS, criptografia e retenção definidas pelo operador. Agendamento e teste real de restauração ainda precisam ser implantados.

## Migração interrompida e rollback

O runner serializa execuções com GET_LOCK e verifica checksums. `schema_migration_attempts` registra início/fim; tentativa incompleta sem histórico concluído interrompe a execução. MySQL faz commits implícitos de DDL: não reaplique cegamente. Pare consumidores, faça cópia forense e restaure o backup anterior ou reconcilie cada instrução com o schema e os dados. Só então um operador pode liberar o registro da tentativa. Não altere checksums de scripts antigos para mascarar divergência.

Migração 008 impede reutilização de TOTP. Contas antigas com segredos TOTP em texto puro não são aceitas pelo novo código: faça reinscrição controlada localmente, com verificação da identidade do administrador, sem expor o segredo. Não há conversão automática silenciosa.

Rollback: pare o Worker; coloque site em manutenção; restaure binários/configuração e chaves preservadas. Se houver incompatibilidade de schema, restaure o backup de banco validado, respeitando dados escritos após a publicação. Remova manutenção apenas após health/readiness e teste de login. Os procedimentos ainda exigem ensaio na VPS; não houve deploy neste trabalho.
