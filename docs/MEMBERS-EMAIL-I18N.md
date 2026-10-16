# Contas públicas, e-mail transacional e idiomas — Sprint 03

Esta etapa adiciona cadastro, confirmação de e-mail, login de membros, recuperação de senha, sessões revogáveis e interface em oito idiomas. Não substitui a homologação integral da Sprint 03.

## Idiomas

Disponíveis: português (`pt-BR`), inglês (`en`), francês (`fr`), espanhol (`es`), russo (`ru`), alemão (`de`), japonês (`ja`) e chinês simplificado (`zh-Hans`). O seletor aparece no cabeçalho de todas as páginas e funciona sem JavaScript. A preferência fica em cookie HttpOnly/Secure, válido por um ano. Sem preferência salva, o portal considera o idioma do navegador e usa português como padrão. Variantes regionais como en-US, fr-CA e zh-CN são reconhecidas; preferências com peso zero não são selecionadas.

Menus, páginas públicas, formulários administrativos/de conta, mensagens de autenticação, mensagens dinâmicas do catálogo e modelos de e-mail foram traduzidos. Datas e números seguem a cultura selecionada. Os horários apresentados de atividade usam UTC−3, com formatação do idioma. Nomes de restaurantes, endereços, categorias armazenadas e conteúdo dos usuários mantêm os dados de origem; não são enviados à IA para tradução automática. As traduções precisam de revisão editorial por falantes nativos antes de publicação internacional.

Os recursos ficam em `src/BelemRestaurantes.Web/Resources/SharedResource*.resx`. Novas chaves precisam existir em todos os idiomas; há teste para garantir essa cobertura. O catálogo agora lê o resultado paginado da API (`items`/`totalCount`) e usa a rota real `/establishment/{id}`.

## Jornadas implementadas

- `/account/register`: nome, e-mail, senha/confirmacão e aceite dos termos. CPF não é solicitado. Índice único de e-mail normalizado, honeypot, limite de cadastro por IP, validação de senha no backend e resposta neutra para endereço já cadastrado.
- `/account/confirm`: confirmação explícita por POST com CSRF. Token criptográfico de 256 bits, hash no banco, validade de 24 horas e consumo único atômico.
- `/account/login`: exige e-mail confirmado, conta ativa e senha válida. Bloqueio progressivo, resposta neutra e cookie separado da administração. Membros não recebem papéis administrativos.
- `/account/forgot` e `/account/reset`: resposta neutra, cooldown de cinco minutos por conta, link válido por 30 minutos, token de uso único, nova senha, revogação das sessões anteriores e e-mail de aviso enfileirado.
- `/account`: nome, sessões ativas, encerramento de outras sessões e logout. Sessões são persistidas com hash do identificador, expiram e são verificadas no backend a cada requisição. Bloqueio da conta, expiração, revogação e alteração da versão de segurança impedem uso posterior.
- Avaliações/favoritos da comunidade exigem o esquema de membro e e-mail confirmado. O cookie administrativo não substitui uma conta da comunidade.

Os links de confirmação/recuperação usam `#token=...`. Fragmentos não são enviados ao servidor HTTP. `account.js` transfere o token para o campo do formulário POST e remove o fragmento do histórico visível. Não há token em query string, cookie de recuperação, logs ou tabela de tokens em texto puro. Páginas de conta têm `Cache-Control: no-store`; não configure captura de corpos POST em ferramentas externas de observabilidade.

Os textos de termos/privacidade descrevem o funcionamento atual. Exportação e exclusão são atendidas operacionalmente pelo endereço oficial; automação de exportação/exclusão e revisão jurídica final continuam pendentes. Alteração autenticada de e-mail/senha, gestão completa de dispositivos e recuperação de MFA administrativo ainda precisam de implementação própria.

## Migração

`009_member_accounts.sql` cria `member_users`, `member_sessions`, `member_tokens` e `transactional_email_outbox`. Colações foram definidas explicitamente, inclusive para identificadores e security stamps. Os scripts 001–008 permanecem inalterados. Aplique com o comando local `--migrate` e credencial de migração, antes de atualizar a Web/Worker.

## Configuração privada na VPS

Use Hosting Bundle/runtime .NET 10 atualizado (10.0.13 ou posterior). Confirme host, porta e modo TLS no painel da Locaweb; nenhum hostname SMTP foi inventado. Remetente fixo: `comercial@belemrestaurantes.com.br`.

| Processo | Variável | Conteúdo |
| --- | --- | --- |
| Web e Worker | `ConnectionStrings__MySql` | Credenciais distintas por papel para o banco `belrestaurante`. |
| Web e Worker | `MailEncryption__KeyRingPath` | Mesmo diretório privado absoluto, fora de IIS/publicação, por exemplo `C:\ProgramData\BelemRestaurantes\MailKeys`. |
| Web | `MemberAccounts__PublicOrigin` | Origem HTTPS confiável dos links; padrão `https://belemrestaurantes.com.br`. Para homologação, usar a origem HTTPS desse ambiente. |
| Worker | `Smtp__Enabled` | `true` somente após configurar e testar as credenciais. Padrão `false`. |
| Worker | `Smtp__Host`, `Smtp__Port` | Configurações reais confirmadas no painel do provedor. |
| Worker | `Smtp__Username`, `Smtp__Password` | Segredos privados, nunca em arquivos versionados nem argumentos de linha de comando. |
| Worker | `Smtp__TlsMode` | `StartTls` obrigatório ou `SslOnConnect`; sem fallback para conexão aberta. |
| Worker | `Smtp__BatchSize`, `Smtp__IntervalSeconds` | Padrão: cinco mensagens por lote a cada trinta segundos, com limites no código. |

A Web apenas insere mensagens na outbox; somente `EmailHostedService`, no Worker independente do IIS, realiza envio. O serviço de e-mail funciona mesmo com descoberta gastronômica desativada. Se o SMTP estiver habilitado sem credenciais/diretório de chaves, o Worker rejeita a configuração antes de consumir a fila.

Mensagens da outbox são cifradas com Data Protection e chave própria da fila. No Windows, as chaves são protegidas por DPAPI da máquina para permitir as duas identidades dedicadas. **Restrinja a ACL do diretório de chaves a SYSTEM, administradores e às identidades da Web/Worker**, com leitura e gravação para rotação; não o coloque sob uma pasta pública nem herde leitura para usuários comuns. Use `scripts/windows/configure-mail-keyring.ps1` em console elevado para preparar um diretório novo. Esse diretório é separado do keyring de autenticação administrativa existente. Faça backup protegido das chaves e planeje restauração na máquina/identidade correta.

O transportador SMTP usa MailKit, valida certificado por padrão e não registra tráfego de protocolo. O modo TLS obrigatório segue a [documentação oficial do MailKit](https://mimekit.net/docs/html/M_MailKit_Net_Smtp_SmtpClient_ConnectAsync_2.htm). Não houve configuração de MX, SPF, DKIM ou DMARC nesta etapa.

## Outbox e limites

Fila MySQL com reserva concorrente `FOR UPDATE SKIP LOCKED`, lease de dois minutos, timeout de envio de 45 segundos, recuperação de lease vencido, retry exponencial e dead-letter após seis tentativas. Durante o processamento, o payload é apagado após envio, expiração ou dead-letter; erros guardam apenas o tipo da exceção. Mensagens de recuperação expiram em trinta minutos. Entrega SMTP é **pelo menos uma vez**: uma queda após o servidor aceitar o e-mail e antes do commit pode gerar reenvio com o mesmo Message-ID. Não há promessa de entrega exatamente uma vez.

O prazo do token é aplicado independentemente do estado do e-mail. Uma conta não ganha acesso por a mensagem ter sido aceita pelo SMTP. O cooldown limita reenvios de confirmação/recuperação; registro duplicado não produz nova mensagem. Cadastros adicionais e publicação de conteúdo ainda exigem controles de reputação/moderação previstos no restante da sprint.

## Verificação e limites de homologação

O script `scripts/test-sprint03.ps1` requer Docker e Node.js, usa MySQL descartável no loopback e executa toda a suite, sem credenciais da VPS. Foram adicionados testes HTTP dos oito idiomas, cookie de preferência, cultura do navegador, CSRF, redirecionamento local e igualdade das chaves de tradução; testes reais de cadastro/duplicidade, confirmação concorrente/expiração, login, recuperação/replay, revogação, isolamento administrativo, outbox/retry e apagamento do payload.

O teste SMTP abre um servidor de teste TLS **somente no loopback**, verifica certificado com CA própria exclusivamente no teste e recebe uma mensagem MIME real localizada. Nenhum certificado foi instalado no sistema e não há bypass de certificado no transportador de produção. Isso valida o transporte local, **não a entrega pela conta Locaweb ou chegada à caixa do destinatário**. As credenciais reais e o teste externo autorizado continuam necessários.

MailKit foi adicionado e dependências .NET/criptográficas foram alinhadas à versão de manutenção 10.0.13 após o NuGet apontar vulnerabilidades nas versões transitivas iniciais. Os analisadores e verificações de vulnerabilidade permanecem habilitados.

A etapa não implementa ainda experiências com fotos, feed, compartilhamento/OG, denúncias, reputação/Selo Cuia, todos os painéis, backup/restauração, validação Google real ou deploy na VPS. Consulte também o relatório histórico [SPRINT03-REPORT.md](SPRINT03-REPORT.md).

## Resultado desta execução

- Build da Solution em Debug e Release: sucesso, sem avisos ou erros.
- Testes: **91 aprovados, zero falhas e zero ignorados** — 46 unitários .NET, 41 de integração .NET e quatro testes JavaScript.
- Evidências .NET: `artifacts/test-results/sprint03_net10.0_20261016172656.trx` e `artifacts/test-results/sprint03_net10.0_20261016172716.trx`. Os quatro testes frontend integram o mesmo script de validação.
- Consulta de vulnerabilidades NuGet, incluindo transitivas: nenhum pacote vulnerável nas fontes consultadas após atualização.
- Publicação Release da Web com os oito assemblies de recursos e publicação win-x64 do Worker verificadas em `artifacts/publish`; nenhuma publicação na VPS.
- Sintaxe dos scripts JavaScript e do script PowerShell de ACL validada. O script de ACL não foi executado no servidor.
