# Sprint 68 — o Worker aplica as migrações de novo: o mistério `Database:ApplyMigrations=false` resolvido e a flag ganha observabilidade

Execução local em 23/10/2026. Sem commit, push ou deploy. Credenciais permanecem fora do código.

## Origem

Desde os primeiros ciclos do Worker independente, cada boot logava **"Aplicação de migrações desativada (Database:ApplyMigrations=false)"** — determinístico em toda geração — apesar de `appsettings.json` **e** `appsettings.Development.json` do projeto Worker declararem `true` sob `Database`. A consequência prática documentada nos checkpoints anteriores: as migrações do banco real só entravam via boot da Web, testes de integração ou as flags one-shot do CLI (`--geocode-pending`, `--collect-culinary`, que aplicam incondicionalmente). A Web lia a mesma chave e resolvia `true`; um app console de probe com pipeline idêntico também resolvia `true`.

## Causa raiz

O cofre local de **user-secrets** da assembly do Worker (`%APPDATA%\Microsoft\UserSecrets\belemrestaurantes-worker\secrets.json`) carregava:

```json
"Database:ApplyMigrations": false
```

User-secrets têm precedência **acima** dos dois appsettings versionados na cadeia de configuração do host genérico. A chave não era bug: era a configuração operacional **intencional da Sprint 48**, documentada no próprio `LOCAL-WORKER.md` ("migrações já aplicadas e verificadas antes da execução contínua"), feita quando o Worker ainda não aplicava schema. Quando o design mudou — Worker aplica no boot, serializado por lock no banco (`GET_LOCK`) — a sobreposição ficou stale no cofre e ninguém mais olhou para ela. Tudo bate: a Web (cofre sem a chave) resolvia `true`; o probe (outra assembly → outro cofre) resolvia `true`; o Worker (cofre com `false`) sempre via `false`. Variáveis de ambiente nas três escopos estavam vazias; o bin recém-recompilado continuava lendo `false` (descartando cópia obsoleta de appsettings).

## O que mudou

- **Cofre corrigido**: `dotnet user-secrets remove "Database:ApplyMigrations" --id belemrestaurantes-worker`. O valor vivo voltou a ser o dos appsettings versionados (`true`); nenhuma entrada de segredo foi adicionada ao repositório.
- **Diagnóstico permanente** (`ConfigurationDiagnosticsService`): no boot, o serviço compara o valor efetivo de `Database:ApplyMigrations` com uma leitura construída **apenas** dos dois arquivos de appsettings do content root. Divergência → `Warning` nomeando a flag, os dois valores e o comando para inspecionar o cofre (`dotnet user-secrets list --id belemrestaurantes-worker`). Valores bool não são segredo; só a presença de chaves sensíveis continua mascarada, como sempre.
- **`IsValueOverridden(bool?, bool?)`** público estático no serviço: true somente quando ambos os lados têm valor e divergem — ausência em qualquer lado nunca é sobrescrita (o padrão do código vale, sem opinião).
- **Testes**: referência de projeto Worker → UnitTests (o helper é puro — sem banco, rede nem sistema de arquivos, dentro da filosofia da suíte) e `Sprint68MigrationFlagOverrideTests` cobrindo iguais, divergentes e ausentes.
- **`LOCAL-WORKER.md`**: ponteiro no bullet histórico da Sprint 48 e nova seção documentando como inspecionar o cofre e como desligar a flag localmente quando um dia fizer sentido (user-secret ou `Database__ApplyMigrations`).

## Decisões

- **Remover a chave, não virá-la para `true` no cofre**: a fonte única da verdade volta a ser o appsettings versionado; a capacidade de sobrescrever por máquina permanece disponível pelos mecanismos padrão, sem duplicar o valor em dois lugares.
- **Aviso, não falha**: uma configuração operacional por máquina é legítima — o boot segue; o que não é legítimo é o silêncio. O próximo "mudança invisível de precedência" aparece na primeira linha do log, não em três dias de investigação.
- **Ausência nunca é sobrescrita**: ambientes sem a chave em qualquer lugar (o padrão do código vale) não geram ruído.
- **Web fora do escopo desta Sprint**: hoje ela resolve corretamente e o mesmo padrão de diagnóstico pode ser portado se um dia a situação mudar — registrar aqui sem expandir a superfície desta mudança.
- **Nada versionado carrega o valor removido**: a correção vive no cofre do usuário (fora do repositório), conforme a convenção "nenhum novo segredo em arquivo versionado".

## Validação

- Compilação Release sem avisos nem erros (`--no-incremental`).
- Suítes completas: **516 testes unitários aprovados** (9 novos em `Sprint68MigrationFlagOverrideTests`) e **238 testes de integração aprovados** mais 1 preexistente ignorado. Sem migração nesta Sprint.
- Ao vivo (evidência em `artifacts/sprint68-live-evidence.txt`): passada A, com a chave stale ainda no cofre, o **novo diagnóstico nomeia a sobreposição no primeiro boot** ("appsettings=True, efetivo=False") e a gate continua desativada; após a remoção do cofre, a passada B (geração final) loga **"Migrações verificadas: 0 aplicadas, 50 já em vigor"** — gate habilitada, verificação completa do runner contra o banco local, sem aviso.

## Operação

Worker parado durante compilação/testes e reiniciado duas vezes para as passadas de evidência. Geração final viva: supervisor 37164 / worker 47376, processo único verificado, heartbeat `running`, padrões do `appsettings.json` (ciclo de geocodificação 10 min). Nenhuma senha, MFA, cota de serviço ou envio de e-mail alterado; nenhum segredo adicionado ou versionado.

## Encerramento operacional

O Worker recupera a autossuficiência de schema: aplica (e re-verifica) as migrações embutidas no próprio boot, serializado pelo lock do banco, coexistindo em paz com a Web. E a classe inteira desse incidente — "uma fonte de maior precedência mudando o comportamento em silêncio" — passa a se anunciar na primeira linha do log, com os dois valores lado a lado.
