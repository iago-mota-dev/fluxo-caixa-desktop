# Fluxo de Caixa - simulador desktop

Aplicação Windows Forms .NET 10 para simular fechamento de caixa e integração com uma API Cloudflare Worker existente. Inclui criação, consulta, edição de todos os campos e exclusão de lançamentos. O backend e o banco D1 são serviços separados.

## Executar

Requer Windows e SDK .NET 10 para compilar. O executável publicado é autocontido.

```powershell
powershell -ExecutionPolicy Bypass -File ./Start-Simulator.ps1 -Build
```

O modo inicial é Local, em http://127.0.0.1:8787. O servidor local deve ser iniciado separadamente; este repositório contém somente o desktop. Para usar o backend publicado, selecione **API publicada (banco remoto)**. Configure FLUXO_API_BASE_URL no ambiente do processo com a URL fornecida pelo responsável. Sem essa configuração, o programa mostra https://api.example.invalid, apenas um exemplo sem serviço real. Nenhum endereço de produção é distribuído neste repositório.

## Credenciais

Nenhum token real é distribuído neste repositório. Obtenha a credencial com o responsável pela API. Informe o token original (não SHA-256) na interface e clique em Salvar credencial, ou importe um arquivo local. O token é protegido usando DPAPI CurrentUser e não pode ser compartilhado entre usuários Windows. Não publique arquivos de credencial nem dados de caixa.

Opcionalmente, configure FLUXO_DESKTOP_TOKEN_LOCAL e FLUXO_DESKTOP_TOKEN_PRODUCTION no ambiente do processo antes de abrir o programa. Essas variáveis são lidas na troca de ambiente. .env.example tem somente placeholders; o programa não carrega .env automaticamente. Não inserir tokens no código, README ou argumentos de linha de comando. Testes usam tokens fictícios, sem acesso à produção.

## Funcionalidades

- Fechar caixa e enviar: persiste o lote local antes do POST.
- Salvar sem enviar: mantém Pending e pausa envio automático.
- Reenviar pendências: envia a fila com o token do ambiente atual.
- Consultar dia na API: GET com intervalo inclusivo do mesmo dia.
- Editar lançamento na API: selecione o dia original na tela; no diálogo escolha forma original, nova forma, data/hora e valor.
- Excluir lançamento na API: selecione dia e forma, depois confirme.

Edição e exclusão são online. Sincronize pendências dos dias envolvidos primeiro. Após confirmação, o histórico local recebe RemoteChanged; consulte o servidor para ver o estado atual. O programa não transforma essas operações em retries automáticos. Um novo fechamento pode sobrescrever valores editados ou recriar registros excluídos.

## Contrato da API

Autenticação desktop: X-Desktop-Token. Não enviar simultaneamente Authorization.

POST /api/fechamentos recebe array de 1 a 6 itens do mesmo dia, sem forma repetida. Cada item contém Data ISO-8601, FormaPagamento e Valor em reais (number JSON com até duas casas, não negativo). Formas: credito, debito, pix, dinheiro, vale refeicao e delivery.

PUT /api/fechamentos/{dataAtual}/{formaAtual} recebe um objeto com todos os novos campos. A URL usa a chave original, mesmo quando data ou forma mudar. Retorna 404 se o original não existir; 409 se o destino estiver ocupado. DELETE na mesma URL não tem corpo; retorna 200 com Excluido true ou false quando a chave já estiver ausente. Codificar os segmentos, por exemplo vale%20refeicao.

GET /api/fechamentos?dataInicio=YYYY-MM-DD&dataFim=YYYY-MM-DD retorna dias consolidados. Datas inclusivas. Limite padrão 30 dias com registros, até 90 usando limit=90.

Dados e credenciais locais são separados por ambiente em %LOCALAPPDATA%/FluxoCaixaSimulator. FLUXO_SIMULATOR_DATA_DIR permite escolher outro diretório. O simulador usa arquivo JSON com escrita atômica para demonstrar a outbox; o sistema desktop real deve salvar fechamento e outbox na mesma transação do banco operacional.

## Testes

```powershell
dotnet run --project FluxoCaixa.Tests -c Release
```

Integração completa opcional, somente com servidor loopback e dados fictícios:

```powershell
dotnet run --project FluxoCaixa.Tests -c Release -- --integration http://127.0.0.1:8787 CAMINHO_DO_ARQUIVO_TOKEN_LOCAL
```

Os testes verificam persistência, retries, autenticação recusada, revisão concorrente, confirmação incompleta, bloqueio de edição com Pending e chamadas PUT/DELETE. O teste integrado também cria, move e exclui registros locais.

## Estrutura

FluxoCaixa.Core: contrato, persistência e transporte HTTP. FluxoCaixa.Simulator: interface e DPAPI. FluxoCaixa.Tests: testes executáveis sem dependências externas de teste.
