# Expansão estadual: meta de 100 mil e evolução do design

Implementação de 18/10/2026, após as sprints 16 e 17. A meta é ampliar um catálogo real de alimentação no Pará. Não há garantia de que as fontes abertas contenham 100 mil estabelecimentos ativos, identificados e elegíveis para este segmento.

## Resultado verificado

- Base inicial: **5.747 cadastros**, todos pendentes de moderação.
- Consulta original concluída até o fim do cursor: **71.565 registros examinados** nas quatro atividades originais; 7.446 candidatos ativos com nome comercial antes da validação completa; 7.354 aceitos pelo parser.
- Importação da consulta original completa: **2.006 novos**, 5.348 atualizados, zero falhas, ciclo 1146. Total após essa etapa: **7.753**.
- Primeiras 17 páginas da consulta ampliada: **1.692 novos**, dois atualizados, zero falhas, ciclo 1147. Total após essa etapa: **9.445**.
- Replay real dessas 17 páginas em outro processo: **zero novos**, 17 páginas reconhecidas pelo checkpoint, zero falhas, ciclo 1149.
- A coleta ampliada e as importações seguintes continuam em lotes. Os números acima são um marco confirmado, não o total final de uma coleta ainda ativa. O painel de cobertura mostra os totais atuais.
- Nenhum desses resultados significa 100 mil cadastros obtidos ou publicados. A quantidade de linhas consultadas não é a quantidade de estabelecimentos adicionados.

## Coleta, qualidade e retomada

A fonte consultada é a API pública [Minha Receita](https://docs.minhareceita.org/como-usar/), derivada de dados cadastrais da Receita Federal. Sua busca é experimental e tem limites por instância do servidor. Usamos páginas de até 1.024 itens, uma consulta por vez, intervalo mínimo de 12 segundos e recuo em HTTP 429/502/503/504 ou timeout. Não há troca de IP nem contorno de limites. Cinco tentativas sem sucesso pausam o processo para retomada explícita.

Dois perfis separados evitam refazer a consulta original e associar um cursor a filtros diferentes:

| Perfil | CNAEs principais consultados |
| --- | --- |
| `legacy` | 5611201, 5611203, 1091102, 4721102 |
| `expanded` | 5611204, 5611205, 5612100, 5620101, 5620102, 5620103, 5620104, 4721103, 4721104, 4729699, 4711301, 4711302, 4712100, 4722901, 4722902, 4724500, 4723700 |

As novas atividades abrangem bares, comida de rua, refeições prontas/coletivas, buffet, cantinas, doces, laticínios, lojas de alimentos, supermercados, mercearias, açougues, peixarias, hortifrúti e bebidas. O CNAE 4729699 inclui comércio de sorvete e polpa de açaí segundo a [CONCLA/IBGE](https://cnae.ibge.gov.br/?subclasse=4729699&tipo=cnae&versao=9&view=subclasse). O nome comercial só refina categorias de alimentação compatíveis: por exemplo, “Açaí” ou “Sorveteria”. Um supermercado não se torna restaurante por ter “pizza” no nome.

Somente CNPJ válido, situação cadastral ativa, UF PA, município oficial consistente e nome comercial apropriado podem entrar. Não se usa razão social como alternativa a nome comercial ausente. Sócios, CPF, e-mail e demais campos pessoais da resposta não são persistidos. Endereço, telefone, fonte e data da consulta permanecem sujeitos a revisão; CNPJ ativo não confirma atendimento ao público, horário, qualidade, preço ou disponibilidade.

O importador mantém a deduplicação por identificador e por nome/endereço/cidade, protege dados de parceiros e preserva a publicação sob moderação. Não cria coordenadas de centro de cidade, avaliações ou fotos de estabelecimentos.

A migração **024**, sem alterar scripts aplicados, cria `registry_import_pages`. O SHA-256 de cada página concluída fica no próprio banco de destino. Página com erro ou cortada pelo limite não é confirmada; seu replay reaproveita os CNPJs já inseridos. Páginas concluídas são ignoradas antes de reprocessar empresas. Metadados do coletor são substituídos por arquivo temporário, e o cursor fica vinculado à consulta. Um novo mês da fonte deve usar outro diretório de coleta; não se inventa um cursor para continuar um dataset trocado.

O teto passou de 20 mil para **100 mil novos registros por chamada**, com leitura em fluxo de arquivo de até 2 GB e páginas de até 4 MB. São limites operacionais; não constituem benchmark de 100 mil inserções nem certificação de capacidade em produção.

## Operação local

Requer Python, curl, .NET 10 e conexão já configurada no User Secrets do Worker. Não exige Places nem uma chave paga. O script usa explicitamente Development e a configuração existente; não contém credenciais.

```powershell
dotnet build BelemRestaurantes.sln -c Release -m:1

# Coleta/importação ampliada, retomável. Não iniciar outro coletor no mesmo diretório.
./scripts/expand-food-catalog.ps1 -Output artifacts/sprint18/receita-expanded -Target 100000 -BatchPages 10

# Coleta isolada, sem importar:
python scripts/fetch-food-registry.py --profile expanded --output artifacts/receita-2026-10 --target 100000

# Importação explícita de um snapshot local:
dotnet run --project src/BelemRestaurantes.Worker -c Release --no-build --no-launch-profile -- --environment Development --import-food-registry --file=artifacts/receita-2026-10/food-pages.ndjson --max-new=100000
```

O orquestrador importa um snapshot estável após cada dez páginas, retoma páginas já confirmadas pelo banco e para ao esgotar o cursor, atingir o alvo de candidatos, chegar ao limite de páginas ou encontrar erro. O alvo de candidatos antecede a validação completa e não garante igual quantidade de novas inserções. `-MaxPages` limita a execução e permite continuar depois no mesmo diretório. Há bloqueio exclusivo por diretório para evitar dois orquestradores simultâneos.

O processo local independe do Visual Studio, mas não foi instalado como serviço agendado. O computador precisa permanecer ligado. Logs completos e arquivos de origem ficam em `artifacts/`, ignorados pelo Git. `import.log` contém o último lote; os ciclos e checkpoints permanecem no banco. Após uma pausa, executar novamente o mesmo comando é seguro. Alterações de binários exigem encerrar os processos do projeto antes de compilar.

## Experiência e acompanhamento

- **Página inicial:** novo módulo territorial com seis regiões, quantidades oficiais de municípios e navegação para o diretório regional. Grafismo vetorial abstrato de rios, verde profundo, açaí, tucupi, trama de cores e tipografia editorial. O desenho é decorativo e não representa uma rota geográfica.
- **Descoberta combinada:** formulário acessível por município e categoria, funcional sem JavaScript, com filtros preservados na busca. Não promete funcionamento em tempo real.
- **PT/EN/ES:** textos próprios e navegação adaptada; demais idiomas mantêm o fallback editorial inglês já informado no site.
- **`/admin/cobertura`:** acesso de administrador/operador com as proteções de sessão existentes; editor sem acesso. Totais reais, meta identificada como referência, cadastros pendentes/publicados, seis regiões, 144 municípios, busca sem acento, categorias e lacunas de endereço/coordenadas. As agregações ocorrem no SQL, sem carregar 100 mil entidades na página. Municípios não reconhecidos ficam fora da meta territorial. Contagens incluem cadastros ainda não revisados.

## Validação

- Build Release: zero avisos/erros.
- 223 testes unitários .NET.
- 90 testes de integração selecionados, incluindo migrações, autenticação/MFA/papéis, filtros e checkpoint; sem consumir filas operacionais.
- Cinco testes offline do coletor: cursor opaco, minimização, troca de perfil proibida, retomada, cursor repetido e pausa por limite da API.
- 40 casos visuais EN/ES e seis jornadas do novo explorador em PT/EN/ES, celular/desktop, com navegação regional e busca combinada.
- A validação responsiva completa e a execução de CI desta revisão têm resultados registrados nos artefatos locais e no workflow do repositório; números finais são atualizados ao concluir esses checks.

As pendências externas das sprints anteriores permanecem descritas no [relatório 16/17](SPRINT16-17-REPORT.md): chave válida de Places/Routes e dados oficiais de transporte, entre outras. Elas não impedem esta expansão cadastral. Atingir 100 mil locais elegíveis e publicar cadastros após revisão continuam sendo resultados dependentes dos dados e da operação, não fatos já alcançados.
