# Executar o site no Visual Studio

Abra `BelemRestaurantes.sln` no Visual Studio com suporte ao .NET 10. Na solução, clique com o botão direito em **BelemRestaurantes.Web** e escolha **Definir como projeto de inicialização**. Selecione o perfil **BelemRestaurantes.Web** e execute com F5 ou Ctrl+F5.

Endereço preferencial: **https://127.0.0.1:63859/**. O perfil abre esse endereço para evitar cookies acumulados de outros projetos em `localhost`. O certificado de desenvolvimento instalado foi validado para esse IP. Cookies não são separados por porta; mudar apenas a porta de `localhost` não resolve esse acúmulo. A mudança de host pode exigir novo login.

O servidor também continua aceitando `https://localhost:63859/`. O HTTP em `http://localhost:63860/` redireciona para HTTPS. `localhost` sem porta não é o endereço deste projeto. A página só responde enquanto o processo Web estiver executando.

O arquivo local ignorado `BelemRestaurantes.slnLaunch.user` também oferece **Somente site**, preservando o perfil anterior de Web + Worker. Reabra a solução se o novo perfil ainda não aparecer. Não é necessário iniciar o Worker para abrir o site; ele já pode estar executando pelo supervisor local.

Alternativa no terminal, na raiz:

```powershell
dotnet run --project src/BelemRestaurantes.Web --launch-profile BelemRestaurantes.Web
```

Aguarde `Now listening on: https://localhost:63859` antes de abrir a página. Pare esse comando com Ctrl+C antes de executar outra instância pelo Visual Studio: duas cópias não podem escutar na mesma porta.

Verificação em 20/10/2026: inicialização Debug pelo perfil do projeto; página inicial, `/admin/login` e `/health` responderam 200, certificado HTTPS confiável e banco acessível. Antes da verificação não havia processo Web deste projeto nas portas configuradas; apenas o Worker estava ativo. Não foi reproduzida falha de inicialização da aplicação. A compilação inicial demorou antes de o servidor começar a escutar.

As instâncias Web usadas nessa verificação foram encerradas para liberar as portas ao Visual Studio. Nenhum outro projeto ou Worker foi encerrado. Sem commit ou deploy.

## HTTP 431 e diagnóstico centralizado

Reprodução em 20/10/2026: página inicial sem cookies externos retorna 200; um cabeçalho Cookie sintético de 40 KB retorna 431 antes de executar a aplicação. Isso reproduz o mecanismo relatado, sem inspecionar nem apagar cookies privados do navegador do usuário. Não foi aumentado o limite de cabeçalhos do servidor nem alterada a segurança de produção.

O perfil agora abre `https://127.0.0.1:63859/`, isolando os cookies host-only de `localhost`. Se insistir em usar `localhost`, remova os dados desse host nas configurações do navegador; isso pode encerrar sessões de outros projetos locais. A janela anônima também serve para comparar. Não é necessário apagar dados de todos os sites.

Após alterar o perfil, pare a execução no Visual Studio, recompile e execute novamente; uma instância já aberta ainda usa o código anterior. Não inicie uma segunda instância na mesma porta.

Diagnóstico novo:

- Development: `Microsoft.AspNetCore.Server.Kestrel.BadRequests` em Debug mostra rejeições 431, que não chegam aos controllers.
- Arquivos JSONL em `%LOCALAPPDATA%\MNSOFT\BelemRestaurantes\logs\web-AAAAMMDD.jsonl`, com rotação por tamanho de 10 MB por arquivo diário e uma cópia anterior. Sem mensagens externas, valores de cookies, cabeçalhos, corpos ou parâmetros da URL.
- Middleware central captura exceções não tratadas, registra tipo/stack e correlação e mantém os handlers e contratos existentes. Não transforma falhas em sucesso nem adiciona catch vazio em cada método. Cancelamento pelo cliente é distinguido de erro interno.
- Contadores por requisição: status, bytes totais de cabeçalhos/cookies, tempo e `X-Correlation-ID`. Inicialização também possui captura com registro e relançamento da exceção.
- No Visual Studio, consulte **Exibir → Saída → ASP.NET Core Web Server / Depuração**; o terminal integrado pode não receber a saída do depurador.

Evidências locais: `artifacts/sprint48/header431-build.log` (zero erros/avisos), `header431-tests.log` (oito testes aprovados: diagnóstico, disponibilidade, login administrativo e contas), `header431-real.json` (200 para localhost/IP sem cookies; 431 para Cookie de 40 KB). A rejeição real foi gravada no arquivo de diagnóstico com status 431 sem o conteúdo do cookie. Validação no navegador em `header431-browser.json`.
